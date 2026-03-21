using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NotifyDispatchApp.Models;
using NotifyDispatchApp.Services;
using Xunit;

namespace NotifyDispatchApp.Tests;

/// <summary>
/// 消防出動情報の大量データ（1000件）に対するテストクラスです。
/// サービス層・ViewModel層のパフォーマンスと正確性を検証します。
/// </summary>
public class LargeDatasetTests
{
    private const int LargeCount = 1000;

    private static readonly string[] Regions = ["富山市", "高岡市", "射水市", "氷見市", "砺波市", "小矢部市", "南砺市", "滑川市", "黒部市", "魚津市"];
    private static readonly string[] Reasons = ["建物火災", "救助", "救急", "その他", "車両火災", "林野火災", "ガス漏れ", "水難救助", "交通事故", "危険排除"];

    /// <summary>
    /// 指定件数のモック出動情報を生成します。
    /// </summary>
    /// <param name="count">生成件数です。</param>
    /// <returns>モック出動情報のリストです。</returns>
    private static List<DispatchInfo> GenerateDispatchInfos(int count)
    {
        var baseTime = DateTime.Now;
        return Enumerable.Range(0, count).Select(i => new DispatchInfo(
            Id: $"dispatch_{i:D4}",
            PartitionKey: "Toyama",
            Region: Regions[i % Regions.Length],
            StrDateTime: baseTime.AddMinutes(-i).ToString("MM/dd HH:mm"),
            Place: $"{Regions[i % Regions.Length]}テスト町{i + 1}-{i % 10 + 1}",
            Reason: Reasons[i % Reasons.Length],
            IsCompleted: i % 3 == 0
        )).ToList();
    }

    // ========================================
    // サービス層テスト
    // ========================================

    /// <summary>
    /// APIから1000件のデータを正常に取得できることを検証します。
    /// </summary>
    [Fact]
    public async Task Service_Returns1000Items_WhenApiReturns1000()
    {
        var expected = GenerateDispatchInfos(LargeCount);
        var service = CreateService(_ => CreateJsonResponse(expected));

        var result = await service.GetDispatchInfoAsync(LargeCount, "富山市");

        Assert.True(result.IsSuccess);
        Assert.Equal(LargeCount, result.Data.Count);
    }

    /// <summary>
    /// 1000件のデータ取得が500ms以内に完了することを検証します。
    /// </summary>
    [Fact]
    public async Task Service_Fetches1000Items_Within500ms()
    {
        var expected = GenerateDispatchInfos(LargeCount);
        var service = CreateService(_ => CreateJsonResponse(expected));

        var sw = Stopwatch.StartNew();
        var result = await service.GetDispatchInfoAsync(LargeCount, "富山市");
        sw.Stop();

        Assert.True(result.IsSuccess);
        Assert.Equal(LargeCount, result.Data.Count);
        Assert.True(sw.ElapsedMilliseconds < 500, $"取得に{sw.ElapsedMilliseconds}msかかりました（上限500ms）");
    }

    /// <summary>
    /// 1000件のデータがすべて正しいプロパティを持つことを検証します。
    /// </summary>
    [Fact]
    public async Task Service_AllItems_HaveValidProperties()
    {
        var expected = GenerateDispatchInfos(LargeCount);
        var service = CreateService(_ => CreateJsonResponse(expected));

        var result = await service.GetDispatchInfoAsync(LargeCount, "富山市");

        Assert.All(result.Data, item =>
        {
            Assert.False(string.IsNullOrEmpty(item.Id));
            Assert.False(string.IsNullOrEmpty(item.Region));
            Assert.False(string.IsNullOrEmpty(item.Place));
            Assert.False(string.IsNullOrEmpty(item.Reason));
            Assert.False(string.IsNullOrEmpty(item.StrDateTime));
        });
    }

    /// <summary>
    /// 1000件のデータに含まれる地域の分布が正しいことを検証します。
    /// </summary>
    [Fact]
    public async Task Service_1000Items_RegionDistribution_IsCorrect()
    {
        var expected = GenerateDispatchInfos(LargeCount);
        var service = CreateService(_ => CreateJsonResponse(expected));

        var result = await service.GetDispatchInfoAsync(LargeCount, "富山市");

        var regionGroups = result.Data.GroupBy(d => d.Region).ToList();
        Assert.Equal(Regions.Length, regionGroups.Count);
        Assert.All(regionGroups, g => Assert.Equal(LargeCount / Regions.Length, g.Count()));
    }

    /// <summary>
    /// 1000件のIDがすべてユニークであることを検証します。
    /// </summary>
    [Fact]
    public async Task Service_1000Items_AllIds_AreUnique()
    {
        var expected = GenerateDispatchInfos(LargeCount);
        var service = CreateService(_ => CreateJsonResponse(expected));

        var result = await service.GetDispatchInfoAsync(LargeCount, "富山市");

        var uniqueIds = result.Data.Select(d => d.Id).Distinct().Count();
        Assert.Equal(LargeCount, uniqueIds);
    }

    /// <summary>
    /// 1000件のJSON シリアライズ／デシリアライズが正常に動作することを検証します。
    /// </summary>
    [Fact]
    public void Serialization_1000Items_RoundTripsCorrectly()
    {
        var items = GenerateDispatchInfos(LargeCount);

        var json = JsonSerializer.Serialize(items);
        var deserialized = JsonSerializer.Deserialize<List<DispatchInfo>>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(LargeCount, deserialized!.Count);
        Assert.Equal(items[0], deserialized[0]);
        Assert.Equal(items[LargeCount - 1], deserialized[LargeCount - 1]);
    }

    /// <summary>
    /// 1000件のJSONシリアライズが100ms以内に完了することを検証します。
    /// </summary>
    [Fact]
    public void Serialization_1000Items_Completes_Within100ms()
    {
        var items = GenerateDispatchInfos(LargeCount);

        var sw = Stopwatch.StartNew();
        var json = JsonSerializer.Serialize(items);
        var deserialized = JsonSerializer.Deserialize<List<DispatchInfo>>(json);
        sw.Stop();

        Assert.NotNull(deserialized);
        Assert.True(sw.ElapsedMilliseconds < 100, $"シリアライズに{sw.ElapsedMilliseconds}msかかりました（上限100ms）");
    }

    // ========================================
    // ViewModel テスト（ObservableCollection 1000件操作）
    // ========================================

    /// <summary>
    /// ViewModelが1000件を正常にロードできることを検証します。
    /// </summary>
    [Fact]
    public async Task ViewModel_Loads1000Items_Successfully()
    {
        var items = GenerateDispatchInfos(LargeCount);
        var service = CreateService(_ => CreateJsonResponse(items));
        var vm = CreateViewModel(service);

        await vm.LoadInfoAsync("富山市");

        Assert.Equal(LargeCount, vm.ListDispatchInfo.Count);
    }

    /// <summary>
    /// ViewModelの1000件ロードが1秒以内に完了することを検証します。
    /// </summary>
    [Fact]
    public async Task ViewModel_Loads1000Items_Within1Second()
    {
        var items = GenerateDispatchInfos(LargeCount);
        var service = CreateService(_ => CreateJsonResponse(items));
        var vm = CreateViewModel(service);

        var sw = Stopwatch.StartNew();
        await vm.LoadInfoAsync("富山市");
        sw.Stop();

        Assert.Equal(LargeCount, vm.ListDispatchInfo.Count);
        Assert.True(sw.ElapsedMilliseconds < 1000, $"ロードに{sw.ElapsedMilliseconds}msかかりました（上限1000ms）");
    }

    /// <summary>
    /// ViewModelの1000件ロード後にIsBusyがfalseに戻ることを検証します。
    /// </summary>
    [Fact]
    public async Task ViewModel_1000Items_IsBusy_ReturnsFalse()
    {
        var items = GenerateDispatchInfos(LargeCount);
        var service = CreateService(_ => CreateJsonResponse(items));
        var vm = CreateViewModel(service);

        await vm.LoadInfoAsync("富山市");

        Assert.False(vm.IsBusy);
    }

    /// <summary>
    /// ViewModelの再ロードで古い1000件が新しい1000件に置き換わることを検証します。
    /// </summary>
    [Fact]
    public async Task ViewModel_Reload1000Items_ReplacesAllData()
    {
        var items1 = GenerateDispatchInfos(LargeCount);
        var items2 = GenerateDispatchInfos(LargeCount)
            .Select(i => i with { Id = $"reload_{i.Id}" }).ToList();

        var callCount = 0;
        // 呼び出しごとに新しい HttpClient を返すファクトリ
        var service = CreateServiceWithFactory(() =>
        {
            callCount++;
            return CreateJsonResponse(callCount == 1 ? items1 : items2);
        });
        var vm = CreateViewModel(service);

        await vm.LoadInfoAsync("富山市");
        Assert.Equal(LargeCount, vm.ListDispatchInfo.Count);
        Assert.StartsWith("dispatch_", vm.ListDispatchInfo[0].Id);

        await vm.LoadInfoAsync("富山市");
        Assert.Equal(LargeCount, vm.ListDispatchInfo.Count);
        Assert.StartsWith("reload_", vm.ListDispatchInfo[0].Id);
    }

    /// <summary>
    /// 1000件の中から特定地域でフィルタリングした件数を検証します。
    /// </summary>
    [Fact]
    public async Task ViewModel_1000Items_FilterByRegion_CorrectCount()
    {
        var items = GenerateDispatchInfos(LargeCount);
        var service = CreateService(_ => CreateJsonResponse(items));
        var vm = CreateViewModel(service);

        await vm.LoadInfoAsync("富山市");

        var toyamaCount = vm.ListDispatchInfo.Count(d => d.Region == "富山市");
        Assert.Equal(LargeCount / Regions.Length, toyamaCount);
    }

    /// <summary>
    /// 1000件の中から完了済み／未完了の件数比率を検証します。
    /// </summary>
    [Fact]
    public async Task ViewModel_1000Items_CompletedRatio_IsCorrect()
    {
        var items = GenerateDispatchInfos(LargeCount);
        var service = CreateService(_ => CreateJsonResponse(items));
        var vm = CreateViewModel(service);

        await vm.LoadInfoAsync("富山市");

        var completedCount = vm.ListDispatchInfo.Count(d => d.IsCompleted);
        // i % 3 == 0 → 約 1/3 が完了済み
        var expectedCompleted = Enumerable.Range(0, LargeCount).Count(i => i % 3 == 0);
        Assert.Equal(expectedCompleted, completedCount);
    }

    // ========================================
    // ヘルパー
    // ========================================

    private static readonly AppSettings TestSettings = new()
    {
        DispatchUrl = "https://test.example.com/api",
        DispatchApiKey = "test-key",
        DefaultRegion = "富山市",
        DefaultPrefecture = "富山県",
        FetchCount = 50,
        AutoRefreshSeconds = 30,
        DispatchDisplayCount = 10000,
    };

    /// <summary>
    /// テスト対象サービスを作成します。
    /// </summary>
    private static DispatchService CreateService(Func<HttpRequestMessage, HttpResponseMessage> handlerFactory)
    {
        var handler = new DelegatingStubHandler(handlerFactory);
        var client = new HttpClient(handler);
        var factory = new StubHttpClientFactory(client);
        return new DispatchService(factory, NullLogger<DispatchService>.Instance, new StubConnectivity(true), TestSettings);
    }

    /// <summary>
    /// 呼び出しごとに新しいHttpClientを返すサービスを作成します。
    /// </summary>
    private static DispatchService CreateServiceWithFactory(Func<HttpResponseMessage> responseFactory)
    {
        var factory = new FreshHttpClientFactory(responseFactory);
        return new DispatchService(factory, NullLogger<DispatchService>.Instance, new StubConnectivity(true), TestSettings);
    }

    /// <summary>
    /// テスト対象ViewModelを作成します。
    /// </summary>
    private static InfoListPageViewModel CreateViewModel(IDispatchService service)
    {
        return new InfoListPageViewModel(service, NullLogger<InfoListPageViewModel>.Instance, TestSettings, new MockLocalCacheService());
    }

    /// <summary>
    /// JSONレスポンスを作成します。
    /// </summary>
    private static HttpResponseMessage CreateJsonResponse<T>(T value)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
        };
    }

    private sealed class DelegatingStubHandler(Func<HttpRequestMessage, HttpResponseMessage> factory) : HttpMessageHandler
    {
        /// <summary>
        /// HTTP要求を処理します。
        /// </summary>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(factory(request));
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        /// <summary>
        /// HTTPクライアントを生成します。
        /// </summary>
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubConnectivity(bool connected) : IConnectivityService
    {
        /// <summary>
        /// インターネット接続が利用可能かどうかを返します。
        /// </summary>
        public bool HasInternetAccess() => connected;
    }

    private sealed class FreshHttpClientFactory(Func<HttpResponseMessage> responseFactory) : IHttpClientFactory
    {
        /// <summary>
        /// 呼び出しごとに新しいHttpClientを生成します。
        /// </summary>
        public HttpClient CreateClient(string name)
        {
            var handler = new DelegatingStubHandler(_ => responseFactory());
            return new HttpClient(handler);
        }
    }
}
