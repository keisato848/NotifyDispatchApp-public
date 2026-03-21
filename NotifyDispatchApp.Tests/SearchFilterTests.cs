using Microsoft.Extensions.Logging.Abstractions;
using NotifyDispatchApp.Models;
using NotifyDispatchApp.Services;
using Xunit;

namespace NotifyDispatchApp.Tests;

/// <summary>
/// 検索・フィルタ機能のテストクラスです。
/// </summary>
public class SearchFilterTests
{
    private static readonly AppSettings TestSettings = new()
    {
        DispatchUrl = "https://test.example.com/api",
        DispatchApiKey = "test-key",
        DefaultRegion = "富山市",
        DefaultPrefecture = "富山県",
        FetchCount = 50,
        AutoRefreshSeconds = 30,
    };

    private static readonly List<DispatchInfo> TestData =
    [
        new("1", "Toyama", "富山市", "01/01 10:00", "富山市桜町1-1", "建物火災", false),
        new("2", "Toyama", "富山市", "01/01 10:30", "富山市新富町2-3", "救助", true),
        new("3", "Toyama", "高岡市", "01/01 11:00", "高岡市御旅屋町5", "救急", false),
        new("4", "Toyama", "射水市", "01/01 12:00", "射水市本町8-1", "車両火災", true),
        new("5", "Toyama", "富山市", "01/01 13:00", "富山市太郎丸本町", "その他", false),
    ];

    /// <summary>
    /// テスト用のViewModelを作成しデータをロードします。
    /// </summary>
    private static async Task<InfoListPageViewModel> CreateLoadedViewModelAsync()
    {
        var service = new StubDispatchService(TestData);
        var vm = new InfoListPageViewModel(service, NullLogger<InfoListPageViewModel>.Instance, TestSettings, new MockLocalCacheService());
        await vm.LoadInfoAsync("富山市");
        return vm;
    }

    // ========================================
    // 基本動作
    // ========================================

    /// <summary>
    /// 初期状態でFilteredDispatchInfoが全件を含むことを検証します。
    /// </summary>
    [Fact]
    public async Task InitialLoad_FilteredContainsAllItems()
    {
        var vm = await CreateLoadedViewModelAsync();

        Assert.Equal(5, vm.FilteredDispatchInfo.Count);
        Assert.Equal(5, vm.ListDispatchInfo.Count);
    }

    /// <summary>
    /// 空の検索テキストで全件が返ることを検証します。
    /// </summary>
    [Fact]
    public async Task Search_EmptyText_ReturnsAllItems()
    {
        var vm = await CreateLoadedViewModelAsync();
        vm.SearchText = "";
        vm.SearchCommand.Execute(null);

        Assert.Equal(5, vm.FilteredDispatchInfo.Count);
    }

    // ========================================
    // 場所（Place）で検索
    // ========================================

    /// <summary>
    /// 場所名の一部で検索できることを検証します。
    /// </summary>
    [Fact]
    public async Task Search_ByPlace_PartialMatch()
    {
        var vm = await CreateLoadedViewModelAsync();
        vm.SearchText = "桜町";
        vm.SearchCommand.Execute(null);

        Assert.Single(vm.FilteredDispatchInfo);
        Assert.Equal("富山市桜町1-1", vm.FilteredDispatchInfo[0].Place);
    }

    /// <summary>
    /// 場所名の検索が大文字小文字を区別しないことを検証します。
    /// </summary>
    [Fact]
    public async Task Search_ByPlace_CaseInsensitive()
    {
        var vm = await CreateLoadedViewModelAsync();
        vm.SearchText = "本町";
        vm.SearchCommand.Execute(null);

        // "射水市本町8-1" と "富山市太郎丸本町" の2件
        Assert.Equal(2, vm.FilteredDispatchInfo.Count);
    }

    // ========================================
    // 理由（Reason）で検索
    // ========================================

    /// <summary>
    /// 出動理由で検索できることを検証します。
    /// </summary>
    [Fact]
    public async Task Search_ByReason_FindsMatch()
    {
        var vm = await CreateLoadedViewModelAsync();
        vm.SearchText = "火災";
        vm.SearchCommand.Execute(null);

        // "建物火災" と "車両火災"
        Assert.Equal(2, vm.FilteredDispatchInfo.Count);
        Assert.All(vm.FilteredDispatchInfo, d => Assert.Contains("火災", d.Reason));
    }

    /// <summary>
    /// 特定の出動理由で検索できることを検証します。
    /// </summary>
    [Fact]
    public async Task Search_ByExactReason_FindsSingleMatch()
    {
        var vm = await CreateLoadedViewModelAsync();
        vm.SearchText = "救急";
        vm.SearchCommand.Execute(null);

        Assert.Single(vm.FilteredDispatchInfo);
        Assert.Equal("高岡市", vm.FilteredDispatchInfo[0].Region);
    }

    // ========================================
    // 地域（Region）で検索
    // ========================================

    /// <summary>
    /// 地域名で検索できることを検証します。
    /// </summary>
    [Fact]
    public async Task Search_ByRegion_FiltersCorrectly()
    {
        var vm = await CreateLoadedViewModelAsync();
        vm.SearchText = "高岡市";
        vm.SearchCommand.Execute(null);

        Assert.Single(vm.FilteredDispatchInfo);
        Assert.Equal("3", vm.FilteredDispatchInfo[0].Id);
    }

    /// <summary>
    /// 射水市で検索すると1件だけ返ることを検証します。
    /// </summary>
    [Fact]
    public async Task Search_ByRegion_Imizu_FindsOne()
    {
        var vm = await CreateLoadedViewModelAsync();
        vm.SearchText = "射水";
        vm.SearchCommand.Execute(null);

        Assert.Single(vm.FilteredDispatchInfo);
        Assert.Equal("射水市", vm.FilteredDispatchInfo[0].Region);
    }

    // ========================================
    // ヒットなし
    // ========================================

    /// <summary>
    /// 該当なしの検索で空になることを検証します。
    /// </summary>
    [Fact]
    public async Task Search_NoMatch_ReturnsEmpty()
    {
        var vm = await CreateLoadedViewModelAsync();
        vm.SearchText = "存在しないキーワード";
        vm.SearchCommand.Execute(null);

        Assert.Empty(vm.FilteredDispatchInfo);
    }

    // ========================================
    // 検索クリア
    // ========================================

    /// <summary>
    /// 検索後にテキストをクリアすると全件に戻ることを検証します。
    /// </summary>
    [Fact]
    public async Task Search_ClearText_RestoresAllItems()
    {
        var vm = await CreateLoadedViewModelAsync();

        vm.SearchText = "桜町";
        vm.SearchCommand.Execute(null);
        Assert.Single(vm.FilteredDispatchInfo);

        vm.SearchText = "";
        vm.SearchCommand.Execute(null);
        Assert.Equal(5, vm.FilteredDispatchInfo.Count);
    }

    // ========================================
    // 空白トリム
    // ========================================

    /// <summary>
    /// 前後の空白がトリムされて検索されることを検証します。
    /// </summary>
    [Fact]
    public async Task Search_WhitespaceIsTrimmed()
    {
        var vm = await CreateLoadedViewModelAsync();
        vm.SearchText = "  桜町  ";
        vm.SearchCommand.Execute(null);

        Assert.Single(vm.FilteredDispatchInfo);
    }

    /// <summary>
    /// 空白のみの入力で全件が返ることを検証します。
    /// </summary>
    [Fact]
    public async Task Search_OnlyWhitespace_ReturnsAllItems()
    {
        var vm = await CreateLoadedViewModelAsync();
        vm.SearchText = "   ";
        vm.SearchCommand.Execute(null);

        Assert.Equal(5, vm.FilteredDispatchInfo.Count);
    }

    // ========================================
    // ListDispatchInfo は検索で変わらない
    // ========================================

    /// <summary>
    /// 検索後もListDispatchInfoが変わらないことを検証します。
    /// </summary>
    [Fact]
    public async Task Search_DoesNotAffect_ListDispatchInfo()
    {
        var vm = await CreateLoadedViewModelAsync();
        vm.SearchText = "桜町";
        vm.SearchCommand.Execute(null);

        Assert.Single(vm.FilteredDispatchInfo);
        Assert.Equal(5, vm.ListDispatchInfo.Count);
    }

    // ========================================
    // 再ロード後の検索
    // ========================================

    /// <summary>
    /// データ再ロード後に検索テキストが維持されフィルタが再適用されることを検証します。
    /// </summary>
    [Fact]
    public async Task Search_AfterReload_FilterIsReapplied()
    {
        var vm = await CreateLoadedViewModelAsync();
        vm.SearchText = "桜町";
        vm.SearchCommand.Execute(null);
        Assert.Single(vm.FilteredDispatchInfo);

        // 再ロード（SearchText を維持して再適用される）
        await vm.LoadInfoAsync("富山市");

        Assert.Single(vm.FilteredDispatchInfo);
        Assert.Equal(5, vm.ListDispatchInfo.Count);
    }

    // ========================================
    // スタブ
    // ========================================

    /// <summary>
    /// テスト用の IDispatchService スタブです。
    /// </summary>
    private sealed class StubDispatchService(List<DispatchInfo> data) : IDispatchService
    {
        /// <summary>
        /// 固定データを返します。
        /// </summary>
        public Task<FetchResult<List<DispatchInfo>>> GetDispatchInfoAsync(int count, string region, bool fetchAll = false, Func<List<DispatchInfo>, Task>? onPageFetched = null) =>
            Task.FromResult(FetchResult<List<DispatchInfo>>.Success(data));

        /// <summary>
        /// 常にtrueを返します。
        /// </summary>
        public bool IsConnected() => true;

        /// <summary>
        /// 常にnullを返します。
        /// </summary>
        public Task<GeoResponse?> GetTownInfoAsync(string prefecture, string city) =>
            Task.FromResult<GeoResponse?>(null);
    }

    /// <summary>
    /// Geo API が座標を返すスタブです。
    /// </summary>
    private sealed class GeoEnabledDispatchService(List<DispatchInfo> data) : IDispatchService
    {
        private static readonly Dictionary<string, Location[]> GeoData = new()
        {
            ["富山市"] =
            [
                new Location("富山市", "とやまし", "桜町", "さくらまち", 137.2115, 36.6937, "富山県", "9300003"),
                new Location("富山市", "とやまし", "新富町", "しんとみちょう", 137.2131, 36.6944, "富山県", "9300002"),
                new Location("富山市", "とやまし", "太郎丸本町", "たろうまるほんまち", 137.1811, 36.7022, "富山県", "9390863"),
            ],
            ["高岡市"] =
            [
                new Location("高岡市", "たかおかし", "御旅屋町", "おたやまち", 136.9624, 36.7530, "富山県", "9330029"),
            ],
            ["射水市"] =
            [
                new Location("射水市", "いみずし", "本町", "ほんまち", 137.0844, 36.7201, "富山県", "9340013"),
            ],
        };

        /// <summary>
        /// 固定データを返します。
        /// </summary>
        public Task<FetchResult<List<DispatchInfo>>> GetDispatchInfoAsync(int count, string region, bool fetchAll = false, Func<List<DispatchInfo>, Task>? onPageFetched = null) =>
            Task.FromResult(FetchResult<List<DispatchInfo>>.Success(data));

        /// <summary>
        /// 常にtrueを返します。
        /// </summary>
        public bool IsConnected() => true;

        /// <summary>
        /// 市区町村名に応じた座標データを返します。
        /// </summary>
        public Task<GeoResponse?> GetTownInfoAsync(string prefecture, string city)
        {
            if (GeoData.TryGetValue(city, out var locations))
            {
                return Task.FromResult<GeoResponse?>(
                    new GeoResponse(new GeoResponseBody(locations)));
            }
            return Task.FromResult<GeoResponse?>(null);
        }
    }

    // ========================================
    // マップピン（ResolveLocationsAsync）
    // ========================================

    /// <summary>
    /// GeoAPI が null を返す場合、MapPins が空になることを検証します。
    /// </summary>
    [Fact]
    public async Task ResolveLocations_GeoReturnsNull_NoPins()
    {
        var vm = await CreateLoadedViewModelAsync();
        await vm.ResolveLocationsAsync();

        Assert.Empty(vm.MapPins);
    }

    /// <summary>
    /// GeoAPI が座標を返す場合、MapPins が生成されることを検証します。
    /// </summary>
    [Fact]
    public async Task ResolveLocations_GeoReturnsData_PinsCreated()
    {
        var service = new GeoEnabledDispatchService(TestData);
        var vm = new InfoListPageViewModel(service, NullLogger<InfoListPageViewModel>.Instance, TestSettings, new MockLocalCacheService());
        await vm.LoadInfoAsync("富山市");

        await vm.ResolveLocationsAsync();

        // 全5件すべて座標解決できるはず
        Assert.Equal(5, vm.MapPins.Count);
    }

    /// <summary>
    /// MapPinsUpdated イベントが発火されることを検証します。
    /// </summary>
    [Fact]
    public async Task ResolveLocations_GeoReturnsData_EventFired()
    {
        var service = new GeoEnabledDispatchService(TestData);
        var vm = new InfoListPageViewModel(service, NullLogger<InfoListPageViewModel>.Instance, TestSettings, new MockLocalCacheService());
        await vm.LoadInfoAsync("富山市");

        var eventFired = false;
        vm.MapPinsUpdated += () => eventFired = true;

        await vm.ResolveLocationsAsync();

        Assert.True(eventFired);
    }

    /// <summary>
    /// ピンの町域名マッチングが正しく動作することを検証します。
    /// </summary>
    [Fact]
    public async Task ResolveLocations_TownNameMatching_MatchesCorrectTown()
    {
        var service = new GeoEnabledDispatchService(TestData);
        var vm = new InfoListPageViewModel(service, NullLogger<InfoListPageViewModel>.Instance, TestSettings, new MockLocalCacheService());
        await vm.LoadInfoAsync("富山市");

        await vm.ResolveLocationsAsync();

        // "富山市桜町1-1" → GeoData の "桜町" にマッチ
        var sakuraPin = vm.MapPins.FirstOrDefault(p => p.Info.Id == "1");
        Assert.NotNull(sakuraPin);
        Assert.Equal("桜町", sakuraPin.TownName);
    }

    /// <summary>
    /// レイヤーフィルタで出動中のみ表示した場合を検証します。
    /// </summary>
    [Fact]
    public async Task ResolveLocations_FilterActiveOnly_ShowsOnlyActive()
    {
        var service = new GeoEnabledDispatchService(TestData);
        var vm = new InfoListPageViewModel(service, NullLogger<InfoListPageViewModel>.Instance, TestSettings, new MockLocalCacheService());
        await vm.LoadInfoAsync("富山市");
        await vm.ResolveLocationsAsync();

        // 鎮火レイヤーをOFF
        vm.ToggleCompletedCommand.Execute(null);

        // 出動中のみ: ID 1, 3, 5
        Assert.Equal(3, vm.MapPins.Count);
        Assert.All(vm.MapPins, p => Assert.False(p.IsCompleted));
    }
}
