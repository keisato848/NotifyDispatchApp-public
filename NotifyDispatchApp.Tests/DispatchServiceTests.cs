using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NotifyDispatchApp.Models;
using NotifyDispatchApp.Services;
using Xunit;

namespace NotifyDispatchApp.Tests;

/// <summary>
/// `DispatchService`の振る舞いを検証します。
/// </summary>
public class DispatchServiceTests
{
    /// <summary>
    /// APIがデータを返した場合に、そのまま結果を返すことを検証します。
    /// </summary>
    [Fact]
    public async Task GetDispatchInfoAsync_ReturnsApiResponse_WhenResponseContainsItems()
    {
        var expected = new List<DispatchInfo>
        {
            new(
                Id: "dispatch-1",
                PartitionKey: "Toyama",
                Region: "富山市",
                StrDateTime: "01/01 12:00",
                Place: "富山市桜町1-1",
                Reason: "建物火災",
                IsCompleted: false)
        };

        var service = CreateService(_ => CreateJsonResponse(expected));

        var result = await service.GetDispatchInfoAsync(1, "富山市");

        Assert.True(result.IsSuccess);
        Assert.Single(result.Data);
        Assert.Equal(expected[0], result.Data[0]);
    }

    /// <summary>
    /// APIが空配列を返した場合の動作を検証します。
    /// DEBUG ビルドではモックデータ、Release ビルドでは空リストを返します。
    /// </summary>
    [Fact]
    public async Task GetDispatchInfoAsync_ReturnsFallback_WhenApiReturnsEmptyList()
    {
        var service = CreateService(_ => CreateJsonResponse(Array.Empty<DispatchInfo>()));

        var result = await service.GetDispatchInfoAsync(3, "富山市");

        Assert.True(result.IsSuccess);
#if DEBUG
        Assert.Equal(5, result.Data.Count);
        Assert.Equal("test_1", result.Data[0].Id);
        Assert.Equal("高岡市", result.Data[2].Region);
#else
        Assert.Empty(result.Data);
#endif
    }

    /// <summary>
    /// API呼び出しで例外が発生した場合の動作を検証します。
    /// FetchResult.IsSuccess が false でエラーメッセージが設定されます。
    /// </summary>
    [Fact]
    public async Task GetDispatchInfoAsync_ReturnsFallback_WhenRequestFails()
    {
        var service = CreateService(_ => throw new HttpRequestException("boom"));

        var result = await service.GetDispatchInfoAsync(3, "富山市");

        Assert.False(result.IsSuccess);
        Assert.NotEmpty(result.ErrorMessage);
        Assert.Equal(FetchErrorKind.Network, result.ErrorKind);
    }

    /// <summary>
    /// 都道府県が空の場合は町域情報取得を行わずnullを返すことを検証します。
    /// </summary>
    [Fact]
    public async Task GetTownInfoAsync_ReturnsNull_WhenPrefectureIsEmpty()
    {
        var service = CreateService(_ => throw new InvalidOperationException("should not be called"));

        var result = await service.GetTownInfoAsync(string.Empty, "富山市");

        Assert.Null(result);
    }

    /// <summary>
    /// 市区町村が空の場合は町域情報取得を行わずnullを返すことを検証します。
    /// </summary>
    [Fact]
    public async Task GetTownInfoAsync_ReturnsNull_WhenCityIsEmpty()
    {
        var service = CreateService(_ => throw new InvalidOperationException("should not be called"));

        var result = await service.GetTownInfoAsync("富山県", string.Empty);

        Assert.Null(result);
    }

    /// <summary>
    /// 町域情報取得が成功した場合に逆シリアル化済みデータを返すことを検証します。
    /// </summary>
    [Fact]
    public async Task GetTownInfoAsync_ReturnsResponse_WhenRequestSucceeds()
    {
        var expected = new GeoResponse(
            new GeoResponseBody(
                [
                    new Location(
                        City: "富山市",
                        CityKana: "とやまし",
                        Town: "桜町",
                        TownKana: "さくらまち",
                        X: 137.2113383,
                        Y: 36.6952907,
                        Prefecture: "富山県",
                        Postal: "930-0003")
                ]));

        var service = CreateService(_ => CreateJsonResponse(expected));

        var result = await service.GetTownInfoAsync("富山県", "富山市");

        Assert.NotNull(result);
        Assert.NotNull(result!.ApiResponse);
        Assert.Single(result.ApiResponse!.Location!);
        Assert.Equal("桜町", result.ApiResponse.Location![0].Town);
    }

    /// <summary>
    /// 町域情報取得で例外が発生した場合にnullを返すことを検証します。
    /// </summary>
    [Fact]
    public async Task GetTownInfoAsync_ReturnsNull_WhenRequestFails()
    {
        var service = CreateService(_ => throw new HttpRequestException("boom"));

        var result = await service.GetTownInfoAsync("富山県", "富山市");

        Assert.Null(result);
    }

    /// <summary>
    /// 接続サービスがtrueを返す場合に接続済みと判定することを検証します。
    /// </summary>
    [Fact]
    public void IsConnected_ReturnsTrue_WhenInternetIsAvailable()
    {
        var service = CreateService(_ => CreateJsonResponse(new { }), true);

        var result = service.IsConnected();

        Assert.True(result);
    }

    /// <summary>
    /// 接続サービスがfalseを返す場合に未接続と判定することを検証します。
    /// </summary>
    [Fact]
    public void IsConnected_ReturnsFalse_WhenInternetIsUnavailable()
    {
        var service = CreateService(_ => CreateJsonResponse(new { }), false);

        var result = service.IsConnected();

        Assert.False(result);
    }

    /// <summary>
    /// `DispatchInfo`がJSONと相互変換できることを検証します。
    /// </summary>
    [Fact]
    public void DispatchInfo_SerializesAndDeserializesExpectedProperties()
    {
        var info = new DispatchInfo(
            Id: "dispatch-1",
            PartitionKey: "Toyama",
            Region: "富山市",
            StrDateTime: "01/01 12:00",
            Place: "富山市桜町1-1",
            Reason: "建物火災",
            IsCompleted: true);

        var json = JsonSerializer.Serialize(info);
        var result = JsonSerializer.Deserialize<DispatchInfo>(json);

        Assert.NotNull(result);
        Assert.Equal(info, result);
    }

    /// <summary>
    /// `GeoResponse`がJSONと相互変換できることを検証します。
    /// </summary>
    [Fact]
    public void GeoResponse_SerializesAndDeserializesExpectedProperties()
    {
        var response = new GeoResponse(
            new GeoResponseBody(
                [
                    new Location(
                        City: "富山市",
                        CityKana: "とやまし",
                        Town: "桜町",
                        TownKana: "さくらまち",
                        X: 137.2113383,
                        Y: 36.6952907,
                        Prefecture: "富山県",
                        Postal: "930-0003")
                ]));

        var json = JsonSerializer.Serialize(response);
        var result = JsonSerializer.Deserialize<GeoResponse>(json);

        Assert.NotNull(result);
        Assert.NotNull(result!.ApiResponse);
        Assert.Equal("桜町", result.ApiResponse!.Location![0].Town);
        Assert.Equal(36.6952907, result.ApiResponse.Location[0].Y);
    }

    /// <summary>
    /// `GeoInfo`が指定した値を保持することを検証します。
    /// </summary>
    [Fact]
    public void GeoInfo_StoresNameAndPostalCode()
    {
        var info = new GeoInfo("桜町", "930-0003");

        Assert.Equal("桜町", info.Name);
        Assert.Equal("930-0003", info.PostalCode);
    }

    /// <summary>
    /// `GeoInfos`の初期リストが空で初期化されることを検証します。
    /// </summary>
    [Fact]
    public void GeoInfos_InitializesEmptyList()
    {
        Assert.NotNull(GeoInfos.geoInfos);
        Assert.Empty(GeoInfos.geoInfos);
    }

    /// <summary>
    /// テスト対象サービスを作成します。
    /// </summary>
    /// <param name="handlerFactory">HTTPレスポンスを生成する処理です。</param>
    /// <param name="hasInternetAccess">接続状態です。</param>
    /// <returns>構築済みの`DispatchService`です。</returns>
    private static readonly AppSettings TestSettings = new()
    {
        DispatchUrl = "https://test.example.com/api",
        DispatchApiKey = "test-key",
        DefaultRegion = "富山市",
        DefaultPrefecture = "富山県",
        FetchCount = 50,
        AutoRefreshSeconds = 30,
    };

    private static DispatchService CreateService(Func<HttpRequestMessage, HttpResponseMessage> handlerFactory, bool hasInternetAccess = true)
    {
        var handler = new DelegatingStubHttpMessageHandler(handlerFactory);
        var client = new HttpClient(handler);
        var clientFactory = new StubHttpClientFactory(client);

        return new DispatchService(clientFactory, NullLogger<DispatchService>.Instance, new StubConnectivityService(hasInternetAccess), TestSettings);
    }

    /// <summary>
    /// JSONレスポンスを作成します。
    /// </summary>
    /// <param name="value">レスポンス本文にする値です。</param>
    /// <returns>JSON形式のHTTPレスポンスです。</returns>
    private static HttpResponseMessage CreateJsonResponse<T>(T value)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
        };
    }

    /// <summary>
    /// テスト用のHTTPメッセージハンドラーです。
    /// </summary>
    private sealed class DelegatingStubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handlerFactory;

        /// <summary>
        /// ハンドラーを初期化します。
        /// </summary>
        /// <param name="handlerFactory">HTTPレスポンスを生成する処理です。</param>
        public DelegatingStubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handlerFactory)
        {
            _handlerFactory = handlerFactory;
        }

        /// <summary>
        /// HTTP要求を処理します。
        /// </summary>
        /// <param name="request">HTTP要求です。</param>
        /// <param name="cancellationToken">キャンセルトークンです。</param>
        /// <returns>HTTPレスポンスです。</returns>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handlerFactory(request));
        }
    }

    /// <summary>
    /// テスト用の`IHttpClientFactory`実装です。
    /// </summary>
    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        /// <summary>
        /// ファクトリを初期化します。
        /// </summary>
        /// <param name="client">返却するHTTPクライアントです。</param>
        public StubHttpClientFactory(HttpClient client)
        {
            _client = client;
        }

        /// <summary>
        /// HTTPクライアントを生成します。
        /// </summary>
        /// <param name="name">クライアント名です。</param>
        /// <returns>テスト用のHTTPクライアントです。</returns>
        public HttpClient CreateClient(string name)
        {
            return _client;
        }
    }

    /// <summary>
    /// テスト用の接続状態実装です。
    /// </summary>
    private sealed class StubConnectivityService : IConnectivityService
    {
        private readonly bool _hasInternetAccess;

        /// <summary>
        /// 接続状態を初期化します。
        /// </summary>
        /// <param name="hasInternetAccess">接続状態です。</param>
        public StubConnectivityService(bool hasInternetAccess)
        {
            _hasInternetAccess = hasInternetAccess;
        }

        /// <summary>
        /// インターネット接続が利用可能かどうかを返します。
        /// </summary>
        /// <returns>インターネット接続が利用可能な場合はtrue。</returns>
        public bool HasInternetAccess()
        {
            return _hasInternetAccess;
        }
    }
}
