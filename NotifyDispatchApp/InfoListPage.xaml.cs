using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Maps;
using NotifyDispatchApp.Models;
using SensorLocation = Microsoft.Maui.Devices.Sensors.Location;

namespace NotifyDispatchApp;

/// <summary>
/// 消防出動情報一覧ページです。
/// </summary>
public partial class InfoListPage : ContentPage
{
    private readonly InfoListPageViewModel _viewModel;

    /// <summary>
    /// 富山県中心座標です。
    /// </summary>
    private static readonly SensorLocation ToyamaCenter = new(36.6953, 137.2113);

    /// <summary>
    /// ハイライトゾーン定義（外側から内側へ、徐々に濃くなる）です。
    /// </summary>
    private static readonly HeatZone[] HeatZones =
    [
        new(500, 0x18, 0x00),   // 外側: ぼんやり
        new(300, 0x30, 0x10),   // 中間
        new(150, 0x55, 0x40),   // 内側: はっきり
    ];

    /// <summary>
    /// 出動中ゾーンの事前計算済みカラー配列です。
    /// </summary>
    private static readonly (Color Fill, Color Stroke)[] ActiveZoneColors = HeatZones
        .Select(z => (
            Color.FromArgb($"{z.FillOpacity:X2}{ActiveColorRgb}"),
            Color.FromArgb($"{z.StrokeOpacity:X2}{ActiveColorRgb}")))
        .ToArray();

    /// <summary>
    /// 鎮火済みゾーンの事前計算済みカラー配列です。
    /// </summary>
    private static readonly (Color Fill, Color Stroke)[] CompletedZoneColors = HeatZones
        .Select(z => (
            Color.FromArgb($"{z.FillOpacity:X2}{CompletedColorRgb}"),
            Color.FromArgb($"{z.StrokeOpacity:X2}{CompletedColorRgb}")))
        .ToArray();

    /// <summary>
    /// サークル描画のズームしきい値（度単位）です。LatitudeDegrees がこの値以下のときのみ描画します。
    /// </summary>
    private const double CircleZoomThreshold = 0.15;

    /// <summary>
    /// 現在サークルが表示されているかどうかです。
    /// </summary>
    private bool _circlesVisible;

    /// <summary>
    /// マップ移動デバウンス用のキャンセルトークンです。
    /// </summary>
    private CancellationTokenSource? _mapDebounce;

    /// <summary>
    /// 出動中のヒートカラー（RGB）です。
    /// </summary>
    private const string ActiveColorRgb = "FF6B6B";

    /// <summary>
    /// 鎮火済みのヒートカラー（RGB）です。
    /// </summary>
    private const string CompletedColorRgb = "2ECC71";

    /// <summary>
    /// InfoListPage の新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="viewModel">ビューモデルです。</param>
    public InfoListPage(InfoListPageViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;

        _viewModel.MapPinsUpdated += RefreshMapPins;
        dispatchMap.PropertyChanged += OnDispatchMapPropertyChanged;
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(InfoListPageViewModel.IsMapMode))
            {
                UpdateToggleButton();
                UpdateLayerChips();
                if (_viewModel.IsMapMode)
                {
                    dispatchMap.MoveToRegion(new MapSpan(ToyamaCenter, 0.3, 0.3));
                    _ = _viewModel.ResolveLocationsAsync();
                }
            }
            if (e.PropertyName is nameof(InfoListPageViewModel.ShowActive) or nameof(InfoListPageViewModel.ShowCompleted))
            {
                UpdateLayerChips();
            }
        };
    }

    /// <summary>
    /// ページ表示時にデータを読み込み、自動更新を開始します。
    /// </summary>
    protected override async void OnAppearing()
    {
        base.OnAppearing();

#if IOS
        await _viewModel.RequestAccess();
#endif
        // 設定画面で保存されたデフォルト地域を反映
        var defaultRegion = Preferences.Default.Get("DefaultRegion", "富山市");
        _viewModel.SelectedRegion = defaultRegion;

        // 設定画面での一括DLやデフォルト地域変更を即反映するため、毎回読み込む
        await _viewModel.LoadInfoAsync(defaultRegion);

        var autoRefresh = Preferences.Default.Get("AutoRefresh", true);
        _viewModel.IsAutoRefreshEnabled = autoRefresh;
        if (autoRefresh)
        {
            _viewModel.StartAutoRefresh();
        }
    }

    /// <summary>
    /// ページ非表示時に自動更新を停止します。
    /// </summary>
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.StopAutoRefresh();
    }

    /// <summary>
    /// 地域ドロップダウンタップ時にポップアップを表示します。
    /// </summary>
    private void OnRegionDropdownTapped(object? sender, EventArgs e)
    {
        regionSearchEntry.Text = "";
        regionSearchResults.ItemsSource = _viewModel.Regions;
        regionPopup.IsVisible = true;
        regionSearchEntry.Focus();
    }

    /// <summary>
    /// 地域検索テキスト変更時にリストをフィルタします。
    /// </summary>
    private void OnRegionSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        var query = e.NewTextValue?.Trim() ?? "";
        regionSearchResults.ItemsSource = string.IsNullOrEmpty(query)
            ? _viewModel.Regions
            : _viewModel.Regions.Where(r => r.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>
    /// 地域検索結果タップ時に選択を確定しポップアップを閉じます。
    /// </summary>
    private async void OnRegionSearchResultSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is string region)
        {
            regionPopup.IsVisible = false;
            _viewModel.SelectedRegion = region;
            await _viewModel.LoadInfoAsync(region);
            if (_viewModel.IsMapMode)
            {
                await _viewModel.ResolveLocationsAsync();
            }
        }
    }

    /// <summary>
    /// 地域ポップアップの背景タップ時にポップアップを閉じます。
    /// </summary>
    private void OnRegionPopupBackdropTapped(object? sender, EventArgs e)
    {
        regionPopup.IsVisible = false;
    }

    /// <summary>
    /// 地域ポップアップ本体タップ時のイベント伝播を防止します。
    /// </summary>
    private static void OnRegionPopupContentTapped(object? sender, EventArgs e)
    {
        // 背景タップで閉じないようイベントを消費
    }

    /// <summary>
    /// 検索デバウンス用のキャンセルトークンです。
    /// </summary>
    private CancellationTokenSource? _searchDebounce;

    /// <summary>
    /// 検索テキスト変更時にデバウンス付きフィルタを適用します。
    /// </summary>
    private async void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        _searchDebounce?.Cancel();
        _searchDebounce = new CancellationTokenSource();
        try
        {
            await Task.Delay(300, _searchDebounce.Token);
            _viewModel.SearchCommand.Execute(null);
        }
        catch (TaskCanceledException)
        {
            // デバウンス中: 次の入力を待機
        }
    }

    /// <summary>
    /// 出動情報タップのイベントハンドラーです。地図画面へ遷移します。
    /// </summary>
    private async void dispatch_infos_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is DispatchInfo info)
        {
            var mapPage = Handler?.MauiContext?.Services.GetService<MapPage>();
            if (mapPage != null)
            {
                mapPage.SetInfo(info);
                await Navigation.PushAsync(mapPage);
            }

            if (sender is CollectionView cv)
            {
                cv.SelectedItem = null;
            }
        }
    }

    /// <summary>
    /// マップのズーム変化を監視し、デバウンス付きでサークル表示しきい値を超えたら再描画します。
    /// </summary>
    private async void OnDispatchMapPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(Microsoft.Maui.Controls.Maps.Map.VisibleRegion)) return;
        if (!_viewModel.IsMapMode) return;

        _mapDebounce?.Cancel();
        _mapDebounce = new CancellationTokenSource();
        try
        {
            await Task.Delay(250, _mapDebounce.Token);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        var shouldShow = dispatchMap.VisibleRegion is { } r && r.LatitudeDegrees <= CircleZoomThreshold;
        if (shouldShow != _circlesVisible)
        {
            _circlesVisible = shouldShow;
            RefreshMapPins();
        }
    }

    /// <summary>
    /// マップ上のエリアハイライトとピンを再描画します。
    /// </summary>
    private void RefreshMapPins()
    {
        dispatchMap.Pins.Clear();
        dispatchMap.MapElements.Clear();

        var drawCircles = dispatchMap.VisibleRegion is { } visibleRegion
                          && visibleRegion.LatitudeDegrees <= CircleZoomThreshold;
        _circlesVisible = drawCircles;

        foreach (var mp in _viewModel.MapPins)
        {
            var center = new SensorLocation(mp.Latitude, mp.Longitude);

            // ── ヒートゾーン（ズームイン時のみ描画）──
            if (drawCircles)
            {
                var zoneColors = mp.IsCompleted ? CompletedZoneColors : ActiveZoneColors;
                for (var i = 0; i < HeatZones.Length; i++)
                {
                    dispatchMap.MapElements.Add(new Circle
                    {
                        Center = center,
                        Radius = new Distance(HeatZones[i].RadiusMeters),
                        FillColor = zoneColors[i].Fill,
                        StrokeColor = zoneColors[i].Stroke,
                        StrokeWidth = i == HeatZones.Length - 1 ? 2 : 0,
                    });
                }
            }

            // ── 中心ピン（常に表示）──
            var statusIcon = mp.IsCompleted ? "✅" : "🔥";
            dispatchMap.Pins.Add(new Pin
            {
                Label = $"{statusIcon} {mp.TownName ?? mp.Info.Place}",
                Address = $"{mp.Info.Reason} | {mp.Info.StrDateTime}",
                Location = center,
                Type = PinType.Place,
            });
        }
    }

    /// <summary>
    /// リスト/マップ切り替えボタンのテキストを更新します。
    /// </summary>
    private void UpdateToggleButton()
    {
        btnToggleView.Text = _viewModel.IsMapMode ? "📋 リスト" : "🗺️ マップ";
    }

    /// <summary>
    /// レイヤーチップの外観を更新します。
    /// </summary>
    private void UpdateLayerChips()
    {
        ApplyChipStyle(chipActive, _viewModel.ShowActive, "#FF6B6B", "#FFEBEE");
        ApplyChipStyle(chipCompleted, _viewModel.ShowCompleted, "#2ECC71", "#E8F5E9");
    }

    /// <summary>
    /// チップの ON/OFF 外観を適用します。
    /// </summary>
    private static void ApplyChipStyle(Border chip, bool isOn, string accentHex, string bgHex)
    {
        if (isOn)
        {
            chip.Opacity = 1.0;
            chip.StrokeThickness = 2.5;
            chip.Stroke = Color.FromArgb(accentHex);
            chip.BackgroundColor = Color.FromArgb(bgHex);
        }
        else
        {
            chip.Opacity = 0.4;
            chip.StrokeThickness = 1;
            chip.Stroke = Colors.Gray;
            chip.BackgroundColor = Color.FromArgb("#F0F0F0");
        }
    }
}
