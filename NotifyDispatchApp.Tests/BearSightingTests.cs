using Microsoft.Extensions.Logging.Abstractions;
using NotifyDispatchApp.Models;
using NotifyDispatchApp.Services;
using Xunit;

namespace NotifyDispatchApp.Tests;

/// <summary>
/// 熊出没情報のサービス・ViewModel・レイヤー機能のテストクラスです。
/// </summary>
public class BearSightingTests
{
    // ========================================
    // サービス層テスト
    // ========================================

    /// <summary>
    /// 全件取得でモックデータが返ることを検証します。
    /// </summary>
    [Fact]
    public async Task Service_GetAll_ReturnsAllMockData()
    {
        var service = new MockBearSightingService();

        var result = await service.GetSightingsAsync();

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Data);
        Assert.Equal(10, result.Data.Count);
    }

    /// <summary>
    /// 市名フィルタが正しく機能することを検証します。
    /// </summary>
    [Fact]
    public async Task Service_FilterByCity_ReturnsFilteredData()
    {
        var service = new MockBearSightingService();

        var result = await service.GetSightingsAsync("富山市");

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Data);
        Assert.All(result.Data, s => Assert.Contains("富山市", s.City));
    }

    /// <summary>
    /// 該当なしの市名で空リストが返ることを検証します。
    /// </summary>
    [Fact]
    public async Task Service_FilterByCity_NoMatch_ReturnsEmpty()
    {
        var service = new MockBearSightingService();

        var result = await service.GetSightingsAsync("存在しない市");

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Data);
    }

    /// <summary>
    /// 全データのプロパティが正しく設定されていることを検証します。
    /// </summary>
    [Fact]
    public async Task Service_AllItems_HaveValidProperties()
    {
        var service = new MockBearSightingService();

        var result = await service.GetSightingsAsync();

        Assert.All(result.Data, s =>
        {
            Assert.False(string.IsNullOrEmpty(s.Id));
            Assert.False(string.IsNullOrEmpty(s.Location));
            Assert.False(string.IsNullOrEmpty(s.City));
            Assert.False(string.IsNullOrEmpty(s.Category));
            Assert.True(s.Latitude > 0);
            Assert.True(s.Longitude > 0);
        });
    }

    /// <summary>
    /// カテゴリが3種類（目撃・痕跡・被害）存在することを検証します。
    /// </summary>
    [Fact]
    public async Task Service_AllCategories_Present()
    {
        var service = new MockBearSightingService();

        var result = await service.GetSightingsAsync();
        var categories = result.Data.Select(s => s.Category).Distinct().OrderBy(c => c).ToList();

        Assert.Contains("目撃", categories);
        Assert.Contains("痕跡", categories);
        Assert.Contains("被害", categories);
    }

    // ========================================
    // ViewModel テスト
    // ========================================

    /// <summary>
    /// テスト用のViewModelを作成します。期間フィルタは「すべて」に設定します。
    /// </summary>
    private static BearInfoPageViewModel CreateViewModel(IBearSightingService? service = null)
    {
        service ??= new MockBearSightingService();
        var vm = new BearInfoPageViewModel(service, NullLogger<BearInfoPageViewModel>.Instance, new AppSettings(), new MockLocalCacheService());
        vm.SelectPeriod(BearInfoPageViewModel.PeriodOptions.First(p => p.Days == 0));
        return vm;
    }

    /// <summary>
    /// 初期状態でレイヤーが3つ登録されていることを検証します。
    /// </summary>
    [Fact]
    public void ViewModel_InitialState_Has3Layers()
    {
        var vm = CreateViewModel();

        Assert.Equal(3, vm.Layers.Count);
        Assert.All(vm.Layers, l => Assert.True(l.IsVisible));
    }

    /// <summary>
    /// LoadDataAsyncで全データが読み込まれることを検証します。
    /// </summary>
    [Fact]
    public async Task ViewModel_LoadData_PopulatesCollections()
    {
        var vm = CreateViewModel();

        await vm.LoadDataAsync();

        Assert.Equal(10, vm.AllSightings.Count);
        Assert.Equal(10, vm.VisibleSightings.Count);
        Assert.Equal(10, vm.TotalCount);
        Assert.False(vm.IsBusy);
    }

    /// <summary>
    /// 直近件数が正しく計算されることを検証します。
    /// </summary>
    [Fact]
    public async Task ViewModel_LoadData_RecentCount_IsCorrect()
    {
        var vm = CreateViewModel();

        await vm.LoadDataAsync();

        var expectedRecent = vm.AllSightings.Count(s => s.IsRecent);
        Assert.Equal(expectedRecent, vm.RecentCount);
        Assert.True(vm.RecentCount > 0);
    }

    // ========================================
    // レイヤー切り替えテスト
    // ========================================

    /// <summary>
    /// 目撃レイヤーをOFFにすると目撃情報が非表示になることを検証します。
    /// </summary>
    [Fact]
    public async Task ViewModel_ToggleSightingLayer_HidesSightings()
    {
        var vm = CreateViewModel();
        await vm.LoadDataAsync();
        var totalBefore = vm.VisibleSightings.Count;

        vm.ToggleLayer("sighting");

        Assert.True(vm.VisibleSightings.Count < totalBefore);
        Assert.DoesNotContain(vm.VisibleSightings, s => s.Category == "目撃");
    }

    /// <summary>
    /// 痕跡レイヤーをOFFにすると痕跡情報が非表示になることを検証します。
    /// </summary>
    [Fact]
    public async Task ViewModel_ToggleTraceLayer_HidesTraces()
    {
        var vm = CreateViewModel();
        await vm.LoadDataAsync();

        vm.ToggleLayer("trace");

        Assert.DoesNotContain(vm.VisibleSightings, s => s.Category == "痕跡");
    }

    /// <summary>
    /// 被害レイヤーをOFFにすると被害情報が非表示になることを検証します。
    /// </summary>
    [Fact]
    public async Task ViewModel_ToggleDamageLayer_HidesDamage()
    {
        var vm = CreateViewModel();
        await vm.LoadDataAsync();

        vm.ToggleLayer("damage");

        Assert.DoesNotContain(vm.VisibleSightings, s => s.Category == "被害");
    }

    /// <summary>
    /// レイヤーを再度ONにすると情報が復帰することを検証します。
    /// </summary>
    [Fact]
    public async Task ViewModel_ToggleLayerTwice_RestoresAll()
    {
        var vm = CreateViewModel();
        await vm.LoadDataAsync();
        var totalBefore = vm.VisibleSightings.Count;

        vm.ToggleLayer("sighting");
        Assert.True(vm.VisibleSightings.Count < totalBefore);

        vm.ToggleLayer("sighting");
        Assert.Equal(totalBefore, vm.VisibleSightings.Count);
    }

    /// <summary>
    /// 全レイヤーをOFFにするとVisibleSightingsが空になることを検証します。
    /// </summary>
    [Fact]
    public async Task ViewModel_AllLayersOff_ShowsNothing()
    {
        var vm = CreateViewModel();
        await vm.LoadDataAsync();

        vm.ToggleLayer("sighting");
        vm.ToggleLayer("trace");
        vm.ToggleLayer("damage");

        Assert.Empty(vm.VisibleSightings);
        Assert.Equal(0, vm.TotalCount);
    }

    /// <summary>
    /// TotalCountがレイヤー切り替えで正しく更新されることを検証します。
    /// </summary>
    [Fact]
    public async Task ViewModel_ToggleLayer_UpdatesTotalCount()
    {
        var vm = CreateViewModel();
        await vm.LoadDataAsync();
        var initialTotal = vm.TotalCount;

        vm.ToggleLayer("sighting");

        Assert.True(vm.TotalCount < initialTotal);
        Assert.Equal(vm.VisibleSightings.Count, vm.TotalCount);
    }

    // ========================================
    // カテゴリマッピング
    // ========================================

    /// <summary>
    /// カテゴリからレイヤーIDへのマッピングが正しいことを検証します。
    /// </summary>
    [Theory]
    [InlineData("目撃", "sighting")]
    [InlineData("痕跡", "trace")]
    [InlineData("被害", "damage")]
    [InlineData("不明", "sighting")]
    public void MapCategoryToLayerId_ReturnsExpected(string category, string expected)
    {
        Assert.Equal(expected, BearInfoPageViewModel.MapCategoryToLayerId(category));
    }

    // ========================================
    // エラーハンドリング
    // ========================================

    /// <summary>
    /// サービス例外時にエラー状態が設定されることを検証します。
    /// </summary>
    [Fact]
    public async Task ViewModel_ServiceError_SetsHasError()
    {
        var vm = CreateViewModel(new FailingBearService());

        await vm.LoadDataAsync();

        Assert.True(vm.HasError);
        Assert.False(string.IsNullOrEmpty(vm.ErrorMessage));
    }

    /// <summary>
    /// DismissErrorでエラー状態がクリアされることを検証します。
    /// </summary>
    [Fact]
    public async Task ViewModel_DismissError_ClearsError()
    {
        var vm = CreateViewModel(new FailingBearService());
        await vm.LoadDataAsync();
        Assert.True(vm.HasError);

        vm.DismissErrorCommand.Execute(null);

        Assert.False(vm.HasError);
        Assert.Equal("", vm.ErrorMessage);
    }

    // ========================================
    // 出没日抽出テスト
    // ========================================

    /// <summary>
    /// region/place の MM/dd プレフィックスから出没日と市名が正しく分離されることを検証します。
    /// </summary>
    [Theory]
    [InlineData("10/26富山市", "富山市")]
    [InlineData("1/5黒部市宇奈月町", "黒部市宇奈月町")]
    [InlineData("12/31南砺市利賀村", "南砺市利賀村")]
    public void ExtractSightingDate_ParsesDatePrefix_ReturnsCleanValue(string input, string expectedClean)
    {
        var (date, clean) = BearSightingService.ExtractSightingDate(input);

        Assert.NotNull(date);
        Assert.Equal(expectedClean, clean);
        Assert.Matches(@"^\d{4}/\d{2}/\d{2}$", date);
    }

    /// <summary>
    /// 日付プレフィックスがない場合はそのまま返されることを検証します。
    /// </summary>
    [Theory]
    [InlineData("富山市")]
    [InlineData("")]
    [InlineData("abc/12test")]
    public void ExtractSightingDate_NoPrefix_ReturnsOriginal(string input)
    {
        var (date, clean) = BearSightingService.ExtractSightingDate(input);

        Assert.Null(date);
        Assert.Equal(input, clean);
    }

    /// <summary>
    /// 抽出された年が未来にならないことを検証します。
    /// </summary>
    [Fact]
    public void ExtractSightingDate_FutureDate_UsesPreviousYear()
    {
        var futureMonth = DateTime.Now.AddMonths(2);
        var input = $"{futureMonth:M/d}富山市";

        var (date, _) = BearSightingService.ExtractSightingDate(input);

        Assert.NotNull(date);
        var parsed = DateTime.Parse(date);
        Assert.True(parsed <= DateTime.Now.Date);
    }

    // ========================================
    // スタブ
    // ========================================

    /// <summary>
    /// 常に例外を投げるテスト用サービスです。
    /// </summary>
    private sealed class FailingBearService : IBearSightingService
    {
        /// <summary>
        /// 常にネットワークエラーを返します。
        /// </summary>
        public Task<FetchResult<List<BearSighting>>> GetSightingsAsync(string city = "", int fetchCount = 0, bool fetchAll = false, Func<List<BearSighting>, Task>? onPageFetched = null)
            => Task.FromResult(FetchResult<List<BearSighting>>.Failure([], "Network error", FetchErrorKind.Network));
    }

    /// <summary>
    /// テスト用のモック熊出没情報サービスです。
    /// </summary>
    private sealed class MockBearSightingService : IBearSightingService
    {
        /// <summary>
        /// モックの熊出没情報を取得します。
        /// </summary>
        /// <param name="city">市区町村名です。空の場合は全件取得します。</param>
        /// <param name="fetchCount">取得件数です。</param>
        /// <param name="fetchAll">全件取得フラグです。</param>
        /// <param name="onPageFetched">ページ取得コールバックです（モックでは使用しません）。</param>
        /// <returns>熊出没情報のリストです。</returns>
        public Task<FetchResult<List<BearSighting>>> GetSightingsAsync(string city = "", int fetchCount = 0, bool fetchAll = false, Func<List<BearSighting>, Task>? onPageFetched = null)
        {
            var now = DateTime.Now;
            var all = new List<BearSighting>
            {
                new("bear_01", now.AddDays(-1).ToString("yyyy/MM/dd HH:mm"),
                    "富山市八尾町桐谷 山林付近", "富山市", 36.5245, 137.1052,
                    "成獣1頭が目撃されました。体長約120cm。", "目撃", true),
                new("bear_02", now.AddDays(-2).ToString("yyyy/MM/dd HH:mm"),
                    "黒部市宇奈月町 県道沿い", "黒部市", 36.8120, 137.5935,
                    "親子連れ（成獣1頭＋子熊2頭）が目撃されました。", "目撃", true),
                new("bear_03", now.AddDays(-3).ToString("yyyy/MM/dd HH:mm"),
                    "南砺市利賀村 集落近く", "南砺市", 36.4312, 136.9756,
                    "成獣1頭が田んぼ付近で目撃されました。", "目撃", true),
                new("bear_04", now.AddDays(-7).ToString("yyyy/MM/dd HH:mm"),
                    "魚津市三ケ 山間部", "魚津市", 36.7890, 137.4321,
                    "登山者が遠方で成獣1頭を目撃。", "目撃", false),
                new("bear_05", now.AddDays(-2).ToString("yyyy/MM/dd HH:mm"),
                    "立山町芦峅寺 登山道脇", "立山町", 36.5800, 137.3050,
                    "爪痕と糞が確認されました。", "痕跡", true),
                new("bear_06", now.AddDays(-5).ToString("yyyy/MM/dd HH:mm"),
                    "氷見市速川 柿畑", "氷見市", 36.8540, 136.9876,
                    "柿の木にクマ棚が確認されました。", "痕跡", false),
                new("bear_07", now.AddDays(-4).ToString("yyyy/MM/dd HH:mm"),
                    "上市町大岩 林道", "上市町", 36.6700, 137.3600,
                    "足跡と樹皮の剥がれが確認されました。", "痕跡", true),
                new("bear_08", now.AddDays(-3).ToString("yyyy/MM/dd HH:mm"),
                    "富山市大山町 養蜂場", "富山市", 36.5500, 137.2200,
                    "養蜂箱3箱が破壊される被害が発生。", "被害", true),
                new("bear_09", now.AddDays(-10).ToString("yyyy/MM/dd HH:mm"),
                    "砺波市庄川町 果樹園", "砺波市", 36.6100, 136.9600,
                    "りんごの木5本に食害が確認されました。", "被害", false),
                new("bear_10", now.AddDays(-6).ToString("yyyy/MM/dd HH:mm"),
                    "小矢部市名ケ滝 畑地", "小矢部市", 36.6400, 136.8800,
                    "トウモロコシ畑が荒らされる被害。", "被害", false),
            };

            var filtered = string.IsNullOrEmpty(city)
                ? all
                : all.Where(s => s.City.Contains(city, StringComparison.OrdinalIgnoreCase)).ToList();

            return Task.FromResult(FetchResult<List<BearSighting>>.Success(filtered));
        }
    }
}
