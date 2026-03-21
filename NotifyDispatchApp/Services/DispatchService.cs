using Microsoft.Extensions.Logging;
using NotifyDispatchApp.Models;
using System.Net.Http.Json;

namespace NotifyDispatchApp.Services;

/// <summary>
/// 消防出動情報を取得・管理するサービスです。
/// </summary>
public interface IDispatchService
{
    /// <summary>
    /// WebAPIから出動情報を取得します。
    /// </summary>
    /// <param name="count">取得件数です。</param>
    /// <param name="region">地域名です。</param>
    /// <param name="fetchAll">true の場合ページネーションで全件取得します。false の場合は1ページのみ取得します。</param>
    /// <param name="onPageFetched">ページ取得ごとに呼ばれるコールバックです。インクリメンタルキャッシュ保存に使用します。</param>
    /// <returns>取得結果です。</returns>
    Task<FetchResult<List<DispatchInfo>>> GetDispatchInfoAsync(int count, string region, bool fetchAll = false, Func<List<DispatchInfo>, Task>? onPageFetched = null);

    /// <summary>
    /// インターネット接続を確認します。
    /// </summary>
    /// <returns>接続されている場合trueです。</returns>
    bool IsConnected();

    /// <summary>
    /// HeartRails Geo APIから町域情報を取得します。
    /// </summary>
    /// <param name="prefecture">都道府県名です。</param>
    /// <param name="city">市区町村名です。</param>
    /// <returns>町域情報です。取得できない場合はnullです。</returns>
    Task<GeoResponse?> GetTownInfoAsync(string prefecture, string city);
}

/// <summary>
/// ネットワーク接続状態を提供するインターフェースです。
/// </summary>
public interface IConnectivityService
{
    /// <summary>
    /// インターネット接続が利用可能かどうかを返します。
    /// </summary>
    /// <returns>利用可能な場合はtrueです。</returns>
    bool HasInternetAccess();
}

/// <summary>
/// アプリケーション設定を保持するクラスです。
/// </summary>
public class AppSettings
{
    /// <summary>
    /// 新 API のベース URL です。
    /// </summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>
    /// 新 API の関数キーです。
    /// </summary>
    public string FunctionKey { get; set; } = "";

    /// <summary>
    /// 出動情報APIのベースURLです（旧 API 互換用）。
    /// </summary>
    public string DispatchUrl { get; set; } = "";

    /// <summary>
    /// 出動情報APIのアクセスキーです（旧 API 互換用）。
    /// </summary>
    public string DispatchApiKey { get; set; } = "";

    /// <summary>
    /// デフォルトの地域名です。
    /// </summary>
    public string DefaultRegion { get; set; } = "富山市";

    /// <summary>
    /// デフォルトの都道府県名です。
    /// </summary>
    public string DefaultPrefecture { get; set; } = "富山県";

    /// <summary>
    /// デフォルトの取得件数です。
    /// </summary>
    public int FetchCount { get; set; } = 50;

    /// <summary>
    /// 消防出動情報の表示件数です。Preferences から上書きされます。
    /// </summary>
    public int DispatchDisplayCount { get; set; } = 50;

    /// <summary>
    /// 熊出没情報の表示件数です。Preferences から上書きされます。
    /// </summary>
    public int BearDisplayCount { get; set; } = 50;

    /// <summary>
    /// 自動更新の間隔（秒）です。
    /// </summary>
    public int AutoRefreshSeconds { get; set; } = 30;
}

/// <summary>
/// IDispatchServiceの実装クラスです。
/// </summary>
public class DispatchService(IHttpClientFactory httpClientFactory, ILogger<DispatchService> logger, IConnectivityService connectivityService, AppSettings settings) : IDispatchService
{
    /// <summary>
    /// WebAPIから出動情報を取得します。
    /// </summary>
    /// <param name="count">取得件数です。</param>
    /// <param name="region">取得対象の地域です。</param>
    /// <param name="fetchAll">true の場合ページネーションで全件取得します。false の場合は1ページのみ取得します。</param>
    /// <param name="onPageFetched">ページ取得ごとに呼ばれるコールバックです。インクリメンタルキャッシュ保存に使用します。</param>
    /// <returns>取得結果です。</returns>
    public async Task<FetchResult<List<DispatchInfo>>> GetDispatchInfoAsync(int count, string region, bool fetchAll = false, Func<List<DispatchInfo>, Task>? onPageFetched = null)
    {
        var requestUrl = "";
        try
        {
            var client = httpClientFactory.CreateClient("DispatchApi");

            List<DispatchInfo>? infos;
            var aborted = false;
            if (!string.IsNullOrEmpty(settings.BaseUrl))
            {
                (infos, requestUrl, aborted) = await FetchFromNewApiAsync(client, count, region, fetchAll, onPageFetched);
            }
            else
            {
                (infos, requestUrl) = await FetchFromLegacyApiAsync(client, count, region);
            }

            if (infos != null && infos.Count > 0)
            {
                if (aborted)
                    return FetchResult<List<DispatchInfo>>.Failure(infos, "一部のデータ取得に失敗しました。取得済みデータはキャッシュに保存済みです。", FetchErrorKind.Partial, requestUrl);
                return FetchResult<List<DispatchInfo>>.Success(infos, requestUrl);
            }

            // API は成功したがデータが空
#if DEBUG
            return FetchResult<List<DispatchInfo>>.Success(GetMockDispatchInfos(), requestUrl);
#else
            return FetchResult<List<DispatchInfo>>.Success([], requestUrl);
#endif
        }
        catch (TaskCanceledException)
        {
            logger.LogWarning("出動情報の取得がタイムアウトしました。");
            return FetchResult<List<DispatchInfo>>.Failure([], "サーバーへの接続がタイムアウトしました。", FetchErrorKind.Timeout, requestUrl);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized || ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            logger.LogError(ex, "API 認証エラー (HTTP {StatusCode})", ex.StatusCode);
            return FetchResult<List<DispatchInfo>>.Failure([], $"API 認証に失敗しました ({ex.StatusCode})。設定を確認してください。", FetchErrorKind.Auth, requestUrl);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is not null && (int)ex.StatusCode >= 500)
        {
            logger.LogError(ex, "サーバーエラー (HTTP {StatusCode})", ex.StatusCode);
            return FetchResult<List<DispatchInfo>>.Failure([], $"サーバーエラーが発生しました ({ex.StatusCode})。", FetchErrorKind.Server, requestUrl);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "ネットワークエラー");
            return FetchResult<List<DispatchInfo>>.Failure([], "ネットワーク接続に失敗しました。接続を確認してください。", FetchErrorKind.Network, requestUrl);
        }
        catch (System.Text.Json.JsonException ex)
        {
            logger.LogError(ex, "レスポンス解析エラー");
            return FetchResult<List<DispatchInfo>>.Failure([], "サーバーからの応答を解析できませんでした。", FetchErrorKind.Parse, requestUrl);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "出動情報の取得に失敗しました。");
            return FetchResult<List<DispatchInfo>>.Failure([], "データの取得に失敗しました。", FetchErrorKind.Unknown, requestUrl);
        }
    }

    /// <summary>
    /// 一括取得時の1ページあたりのタイムアウト秒数です。
    /// </summary>
    private const int BulkPageTimeoutSeconds = 30;

    /// <summary>
    /// 通常取得時の1ページあたりのタイムアウト秒数です。
    /// </summary>
    private const int DefaultPageTimeoutSeconds = 15;

    /// <summary>
    /// 一括取得時の最大リトライ回数です。
    /// </summary>
    private const int MaxRetries = 3;

    /// <summary>
    /// リトライ初回待機秒数です。以降は指数バックオフで倍増します。
    /// </summary>
    private const int InitialRetryDelaySeconds = 2;

    /// <summary>
    /// 新 API（/api/FireDispatch）から出動情報を取得します。
    /// </summary>
    /// <param name="client">HTTPクライアントです。</param>
    /// <param name="count">1ページあたりの取得件数です。</param>
    /// <param name="region">地域名です。</param>
    /// <param name="fetchAll">true の場合ページネーションで全件取得します。false の場合は1ページのみ取得します。</param>
    /// <param name="onPageFetched">ページ取得ごとに呼ばれるコールバックです。</param>
    /// <returns>出動情報のリストです。</returns>
    private async Task<(List<DispatchInfo>?, string url, bool aborted)> FetchFromNewApiAsync(HttpClient client, int count, string region, bool fetchAll, Func<List<DispatchInfo>, Task>? onPageFetched)
    {
        var allItems = new List<DispatchInfo>();
        var offset = 0;
        var firstUrl = "";
        var timeoutSeconds = fetchAll ? BulkPageTimeoutSeconds : DefaultPageTimeoutSeconds;
        var aborted = false;

        while (!aborted)
        {
            var queryParams = new List<string>();

            if (!string.IsNullOrEmpty(settings.FunctionKey))
                queryParams.Add($"code={Uri.EscapeDataString(settings.FunctionKey)}");

            queryParams.Add($"limit={count}");

            if (offset > 0)
                queryParams.Add($"offset={offset}");

            var url = $"{settings.BaseUrl}/api/FireDispatch";
            if (queryParams.Count > 0)
                url += $"?{string.Join("&", queryParams)}";

            if (string.IsNullOrEmpty(firstUrl))
                firstUrl = url;

            // リトライループ（指数バックオフ）
            var maxAttempts = fetchAll ? MaxRetries + 1 : 1;
            var retryDelay = InitialRetryDelaySeconds;
            ApiPagedResponse<DispatchInfo>? response = null;

            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    if (attempt > 1)
                    {
                        logger.LogInformation("出動情報リトライ {Attempt}/{Max} (待機{Delay}秒, offset={Offset})", attempt - 1, MaxRetries, retryDelay, offset);
                        await Task.Delay(TimeSpan.FromSeconds(retryDelay));
                        retryDelay *= 2;
                    }

                    logger.LogInformation("新API呼出: {Url}", url);
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                    response = await client.GetFromJsonAsync(url, AppJsonContext.Default.ApiPagedResponseDispatchInfo, cts.Token);
                    logger.LogInformation("新API応答: count={Count}, offset={Offset}", response?.Count, offset);
                    break;
                }
                catch (Exception ex) when (attempt < maxAttempts)
                {
                    logger.LogWarning(ex, "ページ取得失敗 (offset={Offset}, attempt={Attempt}/{Max})", offset, attempt, maxAttempts);
                }
                catch (Exception ex) when (fetchAll && allItems.Count > 0)
                {
                    logger.LogWarning(ex, "リトライ上限到達 (offset={Offset})。取得済み{Count}件で打ち切ります。", offset, allItems.Count);
                    aborted = true;
                    break;
                }
            }

            if (aborted || response?.Items is not { Count: > 0 })
                break;

            allItems.AddRange(response.Items);

            if (onPageFetched is not null)
                await onPageFetched(response.Items);

            var effectiveLimit = response.Limit > 0 ? response.Limit : count;
            if (!fetchAll || response.Items.Count < effectiveLimit)
                break;

            offset += response.Items.Count;
        }

        logger.LogInformation("新API合計取得件数: {Total}", allItems.Count);
        return (allItems.Count > 0 ? allItems : null, firstUrl, aborted);
    }

    /// <summary>
    /// 旧 API から出動情報を取得します。
    /// </summary>
    /// <param name="client">HTTPクライアントです。</param>
    /// <param name="count">取得件数です。</param>
    /// <param name="region">地域名です。</param>
    /// <returns>出動情報のリストです。</returns>
    private async Task<(List<DispatchInfo>?, string url)> FetchFromLegacyApiAsync(HttpClient client, int count, string region)
    {
        var url = $"{settings.DispatchUrl}?code={settings.DispatchApiKey}&count={count}&region={region}";
        logger.LogInformation("旧API呼出: {Url}", url);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var result = await client.GetFromJsonAsync(url, AppJsonContext.Default.ListDispatchInfo, cts.Token);
        return (result, url);
    }

#if DEBUG
    /// <summary>
    /// デバッグ時に使用するモック出動情報を生成します。
    /// </summary>
    /// <returns>モック出動情報のリストです。</returns>
    private static List<DispatchInfo> GetMockDispatchInfos()
    {
        var now = DateTime.Now;
        return
        [
            new("test_1", "Toyama", "富山市", now.ToString("MM/dd HH:mm"), "富山市桜町1-1 TEST", "建物火災(テスト)", false),
            new("test_2", "Toyama", "富山市", now.AddMinutes(-30).ToString("MM/dd HH:mm"), "富山市新富町1-2 TEST", "救助(テスト)", true),
            new("test_3", "Toyama", "高岡市", now.AddHours(-2).ToString("MM/dd HH:mm"), "高岡市御旅屋町 TEST", "その他(テスト)", true),
            new("test_4", "Toyama", "富山市", now.AddMinutes(-5).ToString("MM/dd HH:mm"), "富山市太郎丸本町 TEST", "救急(テスト)", false),
            new("test_5", "Toyama", "射水市", now.AddHours(-1).ToString("MM/dd HH:mm"), "射水市本町 TEST", "車両火災(テスト)", true),
        ];
    }
#endif

    /// <summary>
    /// インターネット接続を確認します。
    /// </summary>
    /// <returns>接続されている場合はtrueです。</returns>
    public bool IsConnected() => connectivityService.HasInternetAccess();

    /// <summary>
    /// HeartRails Geo APIから町域情報を取得します。
    /// </summary>
    /// <param name="prefecture">都道府県名です。</param>
    /// <param name="city">市区町村名です。</param>
    /// <returns>取得した町域情報です。取得できない場合はnullです。</returns>
    public async Task<GeoResponse?> GetTownInfoAsync(string prefecture, string city)
    {
        if (string.IsNullOrEmpty(prefecture) || string.IsNullOrEmpty(city)) return null;

        try
        {
            var client = httpClientFactory.CreateClient("GeoApi");
            var url = $"https://geoapi.heartrails.com/api/json?method=getTowns&prefecture={Uri.EscapeDataString(prefecture)}&city={Uri.EscapeDataString(city)}";
            return await client.GetFromJsonAsync<GeoResponse>(url);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "町域情報の取得に失敗しました。");
            return null;
        }
    }
}
