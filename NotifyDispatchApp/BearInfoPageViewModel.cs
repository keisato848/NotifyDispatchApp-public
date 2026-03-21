using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using NotifyDispatchApp.Models;
using NotifyDispatchApp.Services;
using System.Collections.ObjectModel;

namespace NotifyDispatchApp;

/// <summary>
/// 熊出没情報マップページの ViewModel です。
/// </summary>
public partial class BearInfoPageViewModel : ObservableObject
{
    private readonly IBearSightingService _bearService;
    private readonly ILogger<BearInfoPageViewModel> _logger;
    private readonly AppSettings _settings;
    private readonly ILocalCacheService _cacheService;
    private List<BearSighting> _allSightings = [];

    /// <summary>
    /// 期間フィルタの選択肢リストです。
    /// </summary>
    public static readonly List<PeriodOption> PeriodOptions =
    [
        new("3日間",   3),
        new("1週間",   7),
        new("2週間",  14),
        new("1ヶ月",  30),
        new("すべて",  0),
    ];

    /// <summary>
    /// BearInfoPageViewModel の新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="bearService">熊出没情報サービスです。</param>
    /// <param name="logger">ロガーです。</param>
    /// <param name="settings">アプリケーション設定です。</param>
    /// <param name="cacheService">ローカルキャッシュサービスです。</param>
    public BearInfoPageViewModel(IBearSightingService bearService, ILogger<BearInfoPageViewModel> logger, AppSettings settings, ILocalCacheService cacheService)
    {
        _bearService = bearService;
        _logger = logger;
        _settings = settings;
        _cacheService = cacheService;
        _selectedPeriod = PeriodOptions[1]; // デフォルト: 1週間

        Layers =
        [
            new MapLayer { Id = "sighting", Name = "目撃", Icon = "👁️", PinColorHex = "#E53935", IsVisible = true },
            new MapLayer { Id = "trace",    Name = "痕跡", Icon = "🐾", PinColorHex = "#FB8C00", IsVisible = true },
            new MapLayer { Id = "damage",   Name = "被害", Icon = "⚠️", PinColorHex = "#8E24AA", IsVisible = true },
        ];
    }

    /// <summary>
    /// データ取得中かどうかを示します。
    /// </summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>
    /// エラーが発生しているかどうかです。
    /// </summary>
    [ObservableProperty]
    private bool _hasError;

    /// <summary>
    /// エラーメッセージです。
    /// </summary>
    [ObservableProperty]
    private string _errorMessage = "";

    /// <summary>
    /// 表示中の情報の合計件数です。
    /// </summary>
    [ObservableProperty]
    private int _totalCount;

    /// <summary>
    /// 直近の情報件数です。
    /// </summary>
    [ObservableProperty]
    private int _recentCount;

    /// <summary>
    /// 選択中の目撃情報です。
    /// </summary>
    [ObservableProperty]
    private BearSighting? _selectedSighting;

    /// <summary>
    /// 選択中の期間フィルタです。
    /// </summary>
    [ObservableProperty]
    private PeriodOption _selectedPeriod;

    /// <summary>
    /// 現在の地図表示領域の範囲です。
    /// </summary>
    private MapBounds? _currentBounds;

    /// <summary>
    /// 前回の VisibleSightings の ID セットです。変化検知に使用します。
    /// </summary>
    private HashSet<string> _previousVisibleIds = [];

    /// <summary>
    /// マップレイヤーのリストです。
    /// </summary>
    public ObservableCollection<MapLayer> Layers { get; }

    /// <summary>
    /// 表示中の目撃情報コレクションです。
    /// </summary>
    public ObservableCollection<BearSighting> VisibleSightings { get; } = [];

    /// <summary>
    /// 表示中の目撃情報の一括更新が完了したときに発生します。
    /// </summary>
    public event Action? VisibleSightingsUpdated;

    /// <summary>
    /// 全目撃情報コレクション（リスト表示用）です。
    /// </summary>
    public ObservableCollection<BearSighting> AllSightings { get; } = [];

    /// <summary>
    /// 期間フィルタ適用後・レイヤーフィルタ適用前の件数です。
    /// </summary>
    [ObservableProperty]
    private int _periodFilteredCount;

    /// <summary>
    /// 熊出没データを読み込みます。
    /// </summary>
    [RelayCommand]
    public async Task LoadDataAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        HasError = false;
        ErrorMessage = "";

        try
        {
            // API から最新データを取得しキャッシュとマージ
            var result = await _bearService.GetSightingsAsync(
                fetchCount: _settings.FetchCount);

            // API エラー時はエラー通知しつつキャッシュにフォールバック
            if (!result.IsSuccess)
            {
                HasError = true;
                ErrorMessage = result.ErrorMessage;
            }

            var fetched = result.Data;
            List<BearSighting> allCached;
            try
            {
                allCached = fetched.Count > 0
                    ? await _cacheService.MergeBearCacheAsync(fetched)
                    : await _cacheService.LoadBearCacheAsync();
            }
            catch (Exception cacheEx)
            {
                _logger.LogWarning(cacheEx, "キャッシュ操作に失敗。API データのみ使用します。");
                allCached = fetched;
            }

            _allSightings = allCached;
            _previousVisibleIds = [];

            // 表示件数を制限してコレクションに反映
            var displayItems = allCached.Take(_settings.BearDisplayCount).ToList();

            AllSightings.Clear();
            foreach (var s in displayItems)
            {
                AllSightings.Add(s);
            }

            UpdateLayerCounts();
            ApplyAllFilters();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "熊出没情報の読み込みに失敗しました。");
            HasError = true;
            ErrorMessage = "データの取得に失敗しました。";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 指定レイヤーの表示/非表示を切り替えます。
    /// </summary>
    /// <param name="layerId">レイヤーIDです。</param>
    [RelayCommand]
    public void ToggleLayer(string layerId)
    {
        var layer = Layers.FirstOrDefault(l => l.Id == layerId);
        if (layer == null) return;

        layer.IsVisible = !layer.IsVisible;

        var idx = Layers.IndexOf(layer);
        if (idx >= 0)
        {
            Layers[idx] = layer;
        }

        ApplyAllFilters();
    }

    /// <summary>
    /// 期間フィルタを変更します。
    /// </summary>
    /// <param name="period">選択された期間です。</param>
    [RelayCommand]
    public void SelectPeriod(PeriodOption period)
    {
        SelectedPeriod = period;
        ApplyAllFilters();
    }

    /// <summary>
    /// 地図の表示範囲を更新し、フィルタを再適用します。
    /// </summary>
    /// <param name="bounds">新しい表示範囲です。</param>
    public void UpdateMapBounds(MapBounds? bounds)
    {
        _currentBounds = bounds;
        ApplyAllFilters();
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
    /// レイヤーごとの件数を更新します。
    /// </summary>
    private void UpdateLayerCounts()
    {
        foreach (var layer in Layers)
        {
            layer.Count = _allSightings.Count(s => MapCategoryToLayerId(s.Category) == layer.Id);
        }
    }

    /// <summary>
    /// 期間・レイヤー・地図範囲の全フィルタを適用します。
    /// </summary>
    public void ApplyAllFilters()
    {
        var visibleLayerIds = Layers.Where(l => l.IsVisible).Select(l => l.Id).ToHashSet();

        // 1) 期間フィルタ
        var periodFiltered = ApplyPeriodFilter(_allSightings);
        PeriodFilteredCount = periodFiltered.Count;

        // 2) レイヤーフィルタ
        var layerFiltered = periodFiltered
            .Where(s => visibleLayerIds.Contains(MapCategoryToLayerId(s.Category)));

        // 3) 地図範囲フィルタ
        var boundsFiltered = _currentBounds != null
            ? layerFiltered.Where(s => _currentBounds.Contains(s.Latitude, s.Longitude))
            : layerFiltered;

        var filtered = boundsFiltered.ToList();
        var currentIds = filtered.Select(s => s.Id).ToHashSet();

        TotalCount = filtered.Count;
        RecentCount = filtered.Count(s => s.IsRecent);

        // フィルタ結果が前回と同じ場合は再描画をスキップ
        if (currentIds.SetEquals(_previousVisibleIds))
        {
            return;
        }

        _previousVisibleIds = currentIds;

        VisibleSightings.Clear();
        foreach (var s in filtered)
        {
            VisibleSightings.Add(s);
        }

        VisibleSightingsUpdated?.Invoke();
    }

    /// <summary>
    /// 互換性のための旧メソッドです。ApplyAllFilters に委譲します。
    /// </summary>
    public void ApplyLayerFilter() => ApplyAllFilters();

    /// <summary>
    /// 期間フィルタを適用します。
    /// </summary>
    /// <param name="sightings">フィルタ対象のリストです。</param>
    /// <returns>期間内の目撃情報リストです。</returns>
    private List<BearSighting> ApplyPeriodFilter(List<BearSighting> sightings)
    {
        if (SelectedPeriod.Days == 0) return sightings;

        var cutoff = DateTime.Now.AddDays(-SelectedPeriod.Days);
        return sightings.Where(s => ParseDate(s.Date) >= cutoff).ToList();
    }

    /// <summary>
    /// 日付文字列をパースします。
    /// </summary>
    /// <param name="dateStr">日付文字列です。</param>
    /// <returns>パースした日時です。パース失敗時は MinValue を返します。</returns>
    public static DateTime ParseDate(string dateStr)
    {
        if (DateTime.TryParse(dateStr, out var dt)) return dt;
        return DateTime.MinValue;
    }

    /// <summary>
    /// カテゴリ名をレイヤーIDにマッピングします。
    /// </summary>
    /// <param name="category">カテゴリ名です。</param>
    /// <returns>対応するレイヤーIDです。</returns>
    public static string MapCategoryToLayerId(string category) => category switch
    {
        "目撃" => "sighting",
        "痕跡" => "trace",
        "被害" => "damage",
        _ => "sighting",
    };
}

/// <summary>
/// 期間フィルタの選択肢です。
/// </summary>
/// <param name="Label">表示ラベルです。</param>
/// <param name="Days">日数です。0の場合は全件を意味します。</param>
public record PeriodOption(string Label, int Days);

/// <summary>
/// 地図の表示範囲を表します。
/// </summary>
public class MapBounds
{
    /// <summary>
    /// 表示範囲の南端の緯度です。
    /// </summary>
    public double SouthLat { get; init; }

    /// <summary>
    /// 表示範囲の北端の緯度です。
    /// </summary>
    public double NorthLat { get; init; }

    /// <summary>
    /// 表示範囲の西端の経度です。
    /// </summary>
    public double WestLng { get; init; }

    /// <summary>
    /// 表示範囲の東端の経度です。
    /// </summary>
    public double EastLng { get; init; }

    /// <summary>
    /// 指定座標がこの範囲内に含まれるかを判定します。
    /// </summary>
    /// <param name="lat">緯度です。</param>
    /// <param name="lng">経度です。</param>
    /// <returns>範囲内の場合 true です。</returns>
    public bool Contains(double lat, double lng) =>
        lat >= SouthLat && lat <= NorthLat && lng >= WestLng && lng <= EastLng;
}
