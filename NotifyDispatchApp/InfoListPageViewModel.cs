using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using NotifyDispatchApp.Models;
using NotifyDispatchApp.Services;
#if !WINDOWS
using Shiny.Push;
#endif
using System.Collections.ObjectModel;

namespace NotifyDispatchApp;

/// <summary>
/// 消防出動情報一覧ページの ViewModel です。
/// </summary>
public partial class InfoListPageViewModel : ObservableObject, IDisposable
{
    private readonly IDispatchService _dispatchService;
    private readonly ILogger<InfoListPageViewModel> _logger;
    private readonly AppSettings _settings;
    private readonly ILocalCacheService _cacheService;
    private Timer? _autoRefreshTimer;
    private bool _disposed;

#if IOS
    private readonly IPushManager _pushManager;
#endif

    /// <summary>
    /// InfoListPageViewModel の新しいインスタンスを初期化します。
    /// </summary>
    public InfoListPageViewModel(IDispatchService dispatchService, ILogger<InfoListPageViewModel> logger, AppSettings settings, ILocalCacheService cacheService
#if IOS
        , IPushManager pushManager
#endif
    )
    {
        _dispatchService = dispatchService;
        _logger = logger;
        _settings = settings;
        _cacheService = cacheService;
#if IOS
        _pushManager = pushManager;
#endif
    }

    /// <summary>
    /// データ取得中かどうかを示します。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isBusy;

    /// <summary>
    /// リストが空かどうかを示します。
    /// </summary>
    public bool IsEmpty => !IsBusy && ListDispatchInfo.Count == 0;

    /// <summary>
    /// 現在選択中の地域名です。
    /// </summary>
    [ObservableProperty]
    private string _selectedRegion = "富山市";

    /// <summary>
    /// 最終更新日時の表示テキストです。
    /// </summary>
    [ObservableProperty]
    private string _lastUpdatedText = "";

    /// <summary>
    /// 出動中の件数です。
    /// </summary>
    [ObservableProperty]
    private int _activeCount;

    /// <summary>
    /// 完了済みの件数です。
    /// </summary>
    [ObservableProperty]
    private int _completedCount;

    /// <summary>
    /// 検索テキストです。
    /// </summary>
    [ObservableProperty]
    private string _searchText = "";

    /// <summary>
    /// エラーメッセージです。空の場合エラーなしです。
    /// </summary>
    [ObservableProperty]
    private string _errorMessage = "";

    /// <summary>
    /// エラーが発生しているかどうかです。
    /// </summary>
    [ObservableProperty]
    private bool _hasError;

    /// <summary>
    /// 自動更新が有効かどうかです。
    /// </summary>
    [ObservableProperty]
    private bool _isAutoRefreshEnabled = true;

    /// <summary>
    /// マップ表示モードかどうかです。
    /// </summary>
    [ObservableProperty]
    private bool _isMapMode;

    /// <summary>
    /// 出動中レイヤーが有効かどうかです。
    /// </summary>
    [ObservableProperty]
    private bool _showActive = true;

    /// <summary>
    /// 鎮火レイヤーが有効かどうかです。
    /// </summary>
    [ObservableProperty]
    private bool _showCompleted = true;

    /// <summary>
    /// マップに表示する出動情報（座標解決済み）です。
    /// </summary>
    public ObservableCollection<DispatchMapPin> MapPins { get; } = [];

    /// <summary>
    /// マップピンの一括更新が完了したときに発生します。
    /// </summary>
    public event Action? MapPinsUpdated;

    /// <summary>
    /// 出動中レイヤーの表示を切り替えます。
    /// </summary>
    [RelayCommand]
    private void ToggleActive()
    {
        ShowActive = !ShowActive;
        ApplyMapFilter();
    }

    /// <summary>
    /// 鎮火レイヤーの表示を切り替えます。
    /// </summary>
    [RelayCommand]
    private void ToggleCompleted()
    {
        ShowCompleted = !ShowCompleted;
        ApplyMapFilter();
    }

    /// <summary>
    /// リスト/マップ表示を切り替えます。
    /// </summary>
    [RelayCommand]
    private void ToggleMapMode()
    {
        IsMapMode = !IsMapMode;
    }

    /// <summary>
    /// レイヤーフィルタに基づいてMapPinsを更新します。
    /// </summary>
    public void ApplyMapFilter()
    {
        var filtered = _allMapPins
            .Where(pin => (pin.IsCompleted && ShowCompleted) || (!pin.IsCompleted && ShowActive))
            .ToList();

        var currentIds = filtered.Select(p => p.Info.Id ?? "").ToHashSet();

        // フィルタ結果が前回と同じ場合は再描画をスキップ
        if (currentIds.SetEquals(_previousMapPinIds))
        {
            return;
        }

        _previousMapPinIds = currentIds;

        MapPins.Clear();
        foreach (var pin in filtered)
        {
            MapPins.Add(pin);
        }

        MapPinsUpdated?.Invoke();
    }

    /// <summary>
    /// 全出動情報の座標解決済みピン一覧です。
    /// </summary>
    private List<DispatchMapPin> _allMapPins = [];

    /// <summary>
    /// 前回の MapPins の ID セットです。変化検知に使用します。
    /// </summary>
    private HashSet<string> _previousMapPinIds = [];

    /// <summary>
    /// 市区町村名ごとの座標情報キャッシュです。同一市名の重複APIコールを排除します。
    /// </summary>
    private readonly Dictionary<string, Models.Location[]?> _geoCache = new(StringComparer.Ordinal);

    /// <summary>
    /// 出動情報の座標を解決してマップピンリストを構築します。
    /// </summary>
    public async Task ResolveLocationsAsync()
    {
        _allMapPins.Clear();
        MapPins.Clear();
        _previousMapPinIds = [];

        _logger.LogInformation("ResolveLocationsAsync 開始: _allItems={Count}", _allItems.Count);

        foreach (var info in _allItems)
        {
            var cityName = ExtractCityName(info.Place);
            if (string.IsNullOrEmpty(cityName))
            {
                _logger.LogDebug("ExtractCityName が null を返却: Place={Place}", info.Place);
                continue;
            }

            try
            {
                // キャッシュ済みの市区町村はAPIコールをスキップ
                if (!_geoCache.TryGetValue(cityName, out var locations))
                {
                    var geoResponse = await _dispatchService.GetTownInfoAsync(_settings.DefaultPrefecture, cityName);
                    locations = geoResponse?.ApiResponse?.Location;
                    _geoCache[cityName] = locations;
                    _logger.LogDebug("GeoAPI結果: city={City}, locations={Count}", cityName, locations?.Length ?? 0);
                }

                if (locations is { Length: > 0 })
                {
                    var matched = locations.FirstOrDefault(loc =>
                        !string.IsNullOrEmpty(info.Place) &&
                        !string.IsNullOrEmpty(loc.Town) &&
                        info.Place.Contains(loc.Town));

                    var target = matched ?? locations[0];
                    _allMapPins.Add(new DispatchMapPin(info, target.Y, target.X, target.Town));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "座標解決に失敗: {Place}", info.Place);
            }
        }

        _logger.LogInformation("ResolveLocationsAsync 完了: _allMapPins={Count}", _allMapPins.Count);

        ApplyMapFilter();

        _logger.LogInformation("ApplyMapFilter 完了: MapPins={Count}", MapPins.Count);
    }

    /// <summary>
    /// Place 文字列から市名を抽出します。
    /// </summary>
    /// <param name="place">場所文字列です。</param>
    /// <returns>抽出された市名です。</returns>
    private static string? ExtractCityName(string? place)
    {
        if (string.IsNullOrEmpty(place)) return null;
        // "富山市桜町1-1" → "富山市", "高岡市○○" → "高岡市"
        var suffixes = new[] { "市", "町", "村" };
        foreach (var suffix in suffixes)
        {
            var idx = place.IndexOf(suffix, StringComparison.Ordinal);
            if (idx > 0) return place[..(idx + suffix.Length)];
        }
        return null;
    }

    /// <summary>
    /// 地域選択肢のリストです。
    /// </summary>
    public List<string> Regions { get; } = ["富山市", "高岡市", "射水市", "氷見市", "砺波市", "小矢部市", "南砺市", "滑川市", "黒部市", "魚津市"];

    /// <summary>
    /// 地域選択肢が存在するかどうかを示します。
    /// </summary>
    public bool HasRegions => Regions.Count > 0;

    /// <summary>
    /// 出動情報の全件コレクションです。
    /// </summary>
    public ObservableCollection<DispatchInfo> ListDispatchInfo { get; } = [];

    /// <summary>
    /// フィルタ済みの出動情報コレクションです。
    /// </summary>
    [ObservableProperty]
    private List<DispatchInfo> _filteredDispatchInfo = [];

    private List<DispatchInfo> _allItems = [];

    /// <summary>
    /// 指定した地域の出動情報を読み込みます。
    /// </summary>
    /// <param name="region">地域名です。</param>
    [RelayCommand]
    public async Task LoadInfoAsync(string region = "富山市")
    {
        if (IsBusy) return;

        IsBusy = true;
        HasError = false;
        ErrorMessage = "";
        SelectedRegion = region;

        try
        {
            // API から最新データを取得しキャッシュとマージ
            var result = await _dispatchService.GetDispatchInfoAsync(_settings.FetchCount, region);

            // [DEBUG] API リクエスト URL をモーダル表示
#if DEBUG && (ANDROID || IOS || MACCATALYST || WINDOWS)
            if (!string.IsNullOrEmpty(result.RequestUrl))
            {
                var status = result.IsSuccess ? $"✅ 成功 ({result.Data.Count}件)" : $"❌ 失敗: {result.ErrorMessage}";
                if (Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Page is Microsoft.Maui.Controls.Page page)
                {
                    await page.DisplayAlert(
                        "API デバッグ情報",
                        $"URL:\n{result.RequestUrl}\n\nステータス:\n{status}",
                        "OK");
                }
            }
#endif

            // API エラー時はエラー通知しつつキャッシュにフォールバック
            if (!result.IsSuccess)
            {
                HasError = true;
                ErrorMessage = result.ErrorMessage;
            }

            var fetched = result.Data;
            List<DispatchInfo> allCached;
            try
            {
                allCached = fetched.Count > 0
                    ? await _cacheService.MergeDispatchCacheAsync(region, fetched)
                    : await _cacheService.LoadDispatchCacheAsync(region);
            }
            catch (Exception cacheEx)
            {
                _logger.LogWarning(cacheEx, "キャッシュ操作に失敗。API データのみ使用します。");
                allCached = fetched;
            }

            _allItems = allCached;

            // 表示件数を制限してコレクションに反映
            var displayItems = allCached.Take(_settings.DispatchDisplayCount).ToList();

            ListDispatchInfo.Clear();
            foreach (var info in displayItems)
            {
                ListDispatchInfo.Add(info);
            }
            FilteredDispatchInfo = displayItems;

            ActiveCount = allCached.Count(d => !d.IsCompleted);
            CompletedCount = allCached.Count(d => d.IsCompleted);
            LastUpdatedText = $"最終更新: {DateTime.Now:HH:mm:ss}";
            OnPropertyChanged(nameof(IsEmpty));

            // 検索テキストがあればフィルタ適用
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                ApplyFilter();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "出動情報の読み込みに失敗しました。");
            HasError = true;
            ErrorMessage = "データの取得に失敗しました。ネットワーク接続を確認してください。";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Pull-to-refresh でデータを再読み込みします。
    /// </summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadInfoAsync(SelectedRegion);
    }

    /// <summary>
    /// 検索テキストに基づきフィルタリングします。
    /// </summary>
    [RelayCommand]
    private void Search()
    {
        ApplyFilter();
    }

    /// <summary>
    /// エラーメッセージを閉じます。
    /// </summary>
    [RelayCommand]
    private void DismissError()
    {
        HasError = false;
        ErrorMessage = "";
    }

    /// <summary>
    /// 自動更新を開始します。
    /// </summary>
    public void StartAutoRefresh()
    {
        StopAutoRefresh();
        if (!IsAutoRefreshEnabled) return;

        var interval = TimeSpan.FromSeconds(_settings.AutoRefreshSeconds);
        _autoRefreshTimer = new Timer(async _ =>
        {
            try
            {
                // Timer コールバックはバックグラウンドスレッドで実行されるため
                // UI 操作を含む LoadInfoAsync は UI スレッドで呼び出す
#if ANDROID || IOS || MACCATALYST || WINDOWS
                await MainThread.InvokeOnMainThreadAsync(() => LoadInfoAsync(SelectedRegion));
#else
                await LoadInfoAsync(SelectedRegion);
#endif
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "自動更新に失敗しました。");
            }
        }, null, interval, interval);
    }

    /// <summary>
    /// 自動更新を停止します。
    /// </summary>
    public void StopAutoRefresh()
    {
        _autoRefreshTimer?.Dispose();
        _autoRefreshTimer = null;
    }

    /// <summary>
    /// フィルタを適用します。
    /// </summary>
    private void ApplyFilter()
    {
        var query = SearchText?.Trim() ?? "";

        FilteredDispatchInfo = string.IsNullOrEmpty(query)
            ? _allItems
            : _allItems.Where(d =>
                (d.Place?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (d.Reason?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (d.Region?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
    }

    /// <summary>
    /// プッシュ通知のアクセスを要求します。
    /// </summary>
    /// <returns>アクセス状態を示すオブジェクトです。</returns>
#if !WINDOWS
    public async Task<PushAccessState?> RequestAccess()
    {
#if IOS
        return await _pushManager.RequestAccess();
#else
        return await Task.FromResult<PushAccessState?>(null);
#endif
    }
#else
    public Task<object?> RequestAccess() => Task.FromResult<object?>(null);
#endif

    /// <summary>
    /// リソースを解放します。
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            StopAutoRefresh();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
