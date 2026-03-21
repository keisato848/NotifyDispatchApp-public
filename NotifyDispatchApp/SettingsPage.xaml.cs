using NotifyDispatchApp.Models;
using NotifyDispatchApp.Services;

namespace NotifyDispatchApp;

/// <summary>
/// アプリ設定画面です。通知や表示のカスタマイズを行います。
/// </summary>
public partial class SettingsPage : ContentPage
{
    private readonly AppSettings _settings;
    private readonly ILocalCacheService _cacheService;
    private readonly IDispatchService _dispatchService;
    private readonly IBearSightingService _bearService;

    /// <summary>
    /// 一括ダウンロード時の1ページあたりの取得件数です。
    /// fetchAll: true によるページネーションで全件取得します。
    /// </summary>
    private const int BulkPageSize = 100;

    /// <summary>
    /// デフォルトの表示件数です。
    /// </summary>
    private const int DefaultDisplayCount = 50;

    /// <summary>
    /// SettingsPage の新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="settings">アプリケーション設定です。</param>
    /// <param name="cacheService">ローカルキャッシュサービスです。</param>
    /// <param name="dispatchService">消防出動情報サービスです。</param>
    /// <param name="bearService">熊出没情報サービスです。</param>
    public SettingsPage(AppSettings settings, ILocalCacheService cacheService, IDispatchService dispatchService, IBearSightingService bearService)
    {
        InitializeComponent();
        _settings = settings;
        _cacheService = cacheService;
        _dispatchService = dispatchService;
        _bearService = bearService;
        LoadSavedSettings();
        UpdateCacheInfo();
    }

    /// <summary>
    /// 保存済みの設定を読み込みます。
    /// </summary>
    private void LoadSavedSettings()
    {
        switchAutoRefresh.IsToggled = Preferences.Default.Get("AutoRefresh", true);
        var region = Preferences.Default.Get("DefaultRegion", "富山市");
        for (int i = 0; i < pickerRegion.Items.Count; i++)
        {
            if (pickerRegion.Items[i] == region)
            {
                pickerRegion.SelectedIndex = i;
                break;
            }
        }

        var dispatchCount = Preferences.Default.Get("DispatchDisplayCount", DefaultDisplayCount);
        sliderDispatchCount.Value = dispatchCount;
        lblDispatchCount.Text = $"{dispatchCount} 件";

        var bearCount = Preferences.Default.Get("BearDisplayCount", DefaultDisplayCount);
        sliderBearCount.Value = bearCount;
        lblBearCount.Text = $"{bearCount} 件";
    }

    /// <summary>
    /// 自動更新トグルの変更イベントハンドラーです。
    /// </summary>
    private void OnAutoRefreshToggled(object? sender, ToggledEventArgs e)
    {
        Preferences.Default.Set("AutoRefresh", e.Value);
    }

    /// <summary>
    /// デフォルト地域の変更イベントハンドラーです。
    /// </summary>
    private void OnRegionChanged(object? sender, EventArgs e)
    {
        if (pickerRegion.SelectedIndex >= 0)
        {
            Preferences.Default.Set("DefaultRegion", pickerRegion.Items[pickerRegion.SelectedIndex]);
        }
    }

    /// <summary>
    /// 消防出動情報の表示件数スライダー変更ハンドラーです。
    /// </summary>
    private void OnDispatchCountChanged(object? sender, ValueChangedEventArgs e)
    {
        var snapped = SnapToStep(e.NewValue);
        if (Math.Abs(sliderDispatchCount.Value - snapped) > 0.1)
            sliderDispatchCount.Value = snapped;

        lblDispatchCount.Text = $"{snapped} 件";
        Preferences.Default.Set("DispatchDisplayCount", snapped);
        _settings.DispatchDisplayCount = snapped;
    }

    /// <summary>
    /// 熊出没情報の表示件数スライダー変更ハンドラーです。
    /// </summary>
    private void OnBearCountChanged(object? sender, ValueChangedEventArgs e)
    {
        var snapped = SnapToStep(e.NewValue);
        if (Math.Abs(sliderBearCount.Value - snapped) > 0.1)
            sliderBearCount.Value = snapped;

        lblBearCount.Text = $"{snapped} 件";
        Preferences.Default.Set("BearDisplayCount", snapped);
        _settings.BearDisplayCount = snapped;
    }

    /// <summary>
    /// 全件ダウンロードボタンのイベントハンドラーです。
    /// </summary>
    private async void OnBulkDownloadClicked(object? sender, EventArgs e)
    {
        var confirm = await DisplayAlert(
            "全件ダウンロード",
            "過去の全データをダウンロードしてキャッシュします。\n通信量が大きくなる場合があります。続行しますか？",
            "ダウンロード開始", "キャンセル");

        if (!confirm) return;

        btnBulkDownload.IsEnabled = false;
        btnBulkDownload.Text = "⏳ ダウンロード中...";

        try
        {
            var region = Preferences.Default.Get("DefaultRegion", "富山市");
            var dispatchCount = 0;
            var bearCount = 0;

            // ページ取得ごとにキャッシュ保存 + UI更新するコールバック
            async Task OnDispatchPageFetched(List<DispatchInfo> items)
            {
                await _cacheService.MergeDispatchCacheAsync(region, items);
                dispatchCount += items.Count;
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    btnBulkDownload.Text = $"⏳ 出動{dispatchCount}件 / 熊{bearCount}件...";
                    UpdateCacheInfo();
                });
            }

            async Task OnBearPageFetched(List<BearSighting> items)
            {
                await _cacheService.MergeBearCacheAsync(items);
                bearCount += items.Count;
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    btnBulkDownload.Text = $"⏳ 出動{dispatchCount}件 / 熊{bearCount}件...";
                    UpdateCacheInfo();
                });
            }

            var dispatchTask = _dispatchService.GetDispatchInfoAsync(BulkPageSize, region, fetchAll: true, onPageFetched: OnDispatchPageFetched);
            var bearTask = _bearService.GetSightingsAsync(fetchCount: BulkPageSize, fetchAll: true, onPageFetched: OnBearPageFetched);

            await Task.WhenAll(dispatchTask, bearTask);

            var dispatchResult = await dispatchTask;
            var bearResult = await bearTask;

            UpdateCacheInfo();

            var errors = new List<string>();
            if (!dispatchResult.IsSuccess)
                errors.Add($"🚒 出動情報: {dispatchResult.ErrorMessage}");
            if (!bearResult.IsSuccess)
                errors.Add($"🐻 熊出没情報: {bearResult.ErrorMessage}");

            if (errors.Count > 0 && dispatchCount == 0 && bearCount == 0)
            {
                await DisplayAlert("エラー",
                    $"ダウンロードに失敗しました:\n{string.Join("\n", errors)}",
                    "OK");
            }
            else if (errors.Count > 0)
            {
                await DisplayAlert("一部完了",
                    $"一部のデータを取得できませんでした:\n{string.Join("\n", errors)}\n\n取得済み: 🚒 {dispatchCount}件 / 🐻 {bearCount}件",
                    "OK");
            }
            else
            {
                await DisplayAlert("完了",
                    $"ダウンロード完了\n🚒 出動情報: {dispatchCount}件\n🐻 熊出没情報: {bearCount}件",
                    "OK");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("エラー", $"ダウンロードに失敗しました:\n{ex.Message}", "OK");
        }
        finally
        {
            btnBulkDownload.IsEnabled = true;
            btnBulkDownload.Text = "📥 全件ダウンロード";
            UpdateCacheInfo();
        }
    }

    /// <summary>
    /// キャッシュ削除ボタンのイベントハンドラーです。
    /// </summary>
    private async void OnClearCacheClicked(object? sender, EventArgs e)
    {
        var confirm = await DisplayAlert("キャッシュ削除", "ローカルに保存されたデータをすべて削除します。続行しますか？", "削除", "キャンセル");
        if (!confirm) return;

        await _cacheService.ClearAllAsync();
        UpdateCacheInfo();
        await DisplayAlert("完了", "キャッシュを削除しました。", "OK");
    }

    /// <summary>
    /// キャッシュ統計ラベルを更新します。
    /// </summary>
    private void UpdateCacheInfo()
    {
        var stats = _cacheService.GetStats();
        var sizeText = stats.TotalSizeBytes switch
        {
            < 1024 => $"{stats.TotalSizeBytes} B",
            < 1024 * 1024 => $"{stats.TotalSizeBytes / 1024.0:F1} KB",
            _ => $"{stats.TotalSizeBytes / (1024.0 * 1024.0):F1} MB",
        };
        lblCacheInfo.Text = $"キャッシュ: 出動{stats.DispatchItemCount}件({stats.DispatchRegionCount}地域) / 熊{stats.BearItemCount}件 ({sizeText})";
    }

    /// <summary>
    /// スライダー値を適応的ステップ単位にスナップします。
    /// 小さい値は細かく、大きい値は粗く丸めます。
    /// </summary>
    /// <param name="value">スライダーの生値です。</param>
    /// <returns>ステップに丸めた整数値です。</returns>
    private static int SnapToStep(double value)
    {
        var step = value switch
        {
            < 100 => 10,
            < 1000 => 50,
            < 10000 => 500,
            _ => 5000,
        };
        return Math.Max(10, (int)(Math.Round(value / step) * step));
    }
}
