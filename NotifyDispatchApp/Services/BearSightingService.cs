using Microsoft.Extensions.Logging;
using NotifyDispatchApp.Models;
using System.Net.Http.Json;

namespace NotifyDispatchApp.Services;

/// <summary>
/// 熊出没情報を取得するサービスインターフェースです。
/// </summary>
public interface IBearSightingService
{
    /// <summary>
    /// 熊出没情報を取得します。
    /// </summary>
    /// <param name="city">市区町村名です。空の場合は全件取得します。</param>
    /// <param name="fetchCount">取得件数です。0以下の場合はデフォルト値を使用します。</param>
    /// <param name="fetchAll">true の場合ページネーションで全件取得します。false の場合は1ページのみ取得します。</param>
    /// <param name="onPageFetched">ページ取得ごとに呼ばれるコールバックです。インクリメンタルキャッシュ保存に使用します。</param>
    /// <returns>取得結果です。</returns>
    Task<FetchResult<List<BearSighting>>> GetSightingsAsync(string city = "", int fetchCount = 0, bool fetchAll = false, Func<List<BearSighting>, Task>? onPageFetched = null);
}

/// <summary>
/// IBearSightingServiceの実装クラスです。
/// Web API から熊出没情報を取得します。
/// </summary>
public class BearSightingService(IHttpClientFactory httpClientFactory, ILogger<BearSightingService> logger, AppSettings settings) : IBearSightingService
{
    /// <summary>
    /// 直近とみなす日数の閾値です。
    /// </summary>
    private const int RecentDaysThreshold = 7;

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
    /// Web API から熊出没情報を取得します。
    /// </summary>
    /// <param name="city">市区町村名です。空の場合は全件取得します。</param>
    /// <param name="fetchCount">取得件数です。0以下の場合はデフォルト値を使用します。</param>
    /// <param name="fetchAll">true の場合ページネーションで全件取得します。false の場合は1ページのみ取得します。</param>
    /// <param name="onPageFetched">ページ取得ごとに呼ばれるコールバックです。インクリメンタルキャッシュ保存に使用します。</param>
    /// <returns>取得結果です。</returns>
    public async Task<FetchResult<List<BearSighting>>> GetSightingsAsync(string city = "", int fetchCount = 0, bool fetchAll = false, Func<List<BearSighting>, Task>? onPageFetched = null)
    {
        try
        {
            var client = httpClientFactory.CreateClient("BearApi");
            var pageSize = fetchCount > 0 ? fetchCount : settings.FetchCount;
            var allItems = new List<BearInfoItem>();
            var offset = 0;
            var timeoutSeconds = fetchAll ? BulkPageTimeoutSeconds : DefaultPageTimeoutSeconds;
            var aborted = false;

            while (!aborted)
            {
                var url = BuildBearInfoUrl(city, pageSize, offset);

                // リトライループ（指数バックオフ）
                var maxAttempts = fetchAll ? MaxRetries + 1 : 1;
                var retryDelay = InitialRetryDelaySeconds;
                ApiPagedResponse<BearInfoItem>? response = null;

                for (var attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    try
                    {
                        if (attempt > 1)
                        {
                            logger.LogInformation("熊出没情報リトライ {Attempt}/{Max} (待機{Delay}秒, offset={Offset})", attempt - 1, MaxRetries, retryDelay, offset);
                            await Task.Delay(TimeSpan.FromSeconds(retryDelay));
                            retryDelay *= 2;
                        }

                        logger.LogInformation("熊出没API呼出: {Url}", url);
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                        response = await client.GetFromJsonAsync(url, AppJsonContext.Default.ApiPagedResponseBearInfoItem, cts.Token);
                        logger.LogInformation("熊出没API応答: count={Count}, offset={Offset}", response?.Count, offset);
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

                // ページ単位でコールバック（BearInfoItem → BearSighting に変換して通知）
                if (onPageFetched is not null)
                {
                    var pageSightings = response.Items
                        .Where(item => item.Latitude.HasValue && item.Longitude.HasValue)
                        .Select(MapToSighting)
                        .ToList();
                    if (pageSightings.Count > 0)
                        await onPageFetched(pageSightings);
                }

                var effectiveLimit = response.Limit > 0 ? response.Limit : pageSize;
                if (!fetchAll || response.Items.Count < effectiveLimit)
                    break;

                offset += response.Items.Count;
            }

            if (allItems.Count == 0)
            {
#if DEBUG
                logger.LogInformation("API からデータが取得できなかったため、モックデータを返します。");
                return FetchResult<List<BearSighting>>.Success(GetMockSightings());
#else
                return FetchResult<List<BearSighting>>.Success([]);
#endif
            }

            logger.LogInformation("熊出没API合計取得件数: {Total}", allItems.Count);

            var sightings = allItems
                .Where(item => item.Latitude.HasValue && item.Longitude.HasValue)
                .Select(MapToSighting)
                .ToList();

            var beforeDedup = sightings.Count;
            sightings = DeduplicateByContent(sightings);
            if (sightings.Count < beforeDedup)
            {
                logger.LogInformation("座標・日時・場所ベースの重複排除: {Before}件 → {After}件 ({Removed}件除去)",
                    beforeDedup, sightings.Count, beforeDedup - sightings.Count);
            }

            if (aborted)
                return FetchResult<List<BearSighting>>.Failure(sightings, "一部のデータ取得に失敗しました。取得済みデータはキャッシュに保存済みです。", FetchErrorKind.Partial);
            return FetchResult<List<BearSighting>>.Success(sightings);
        }
        catch (TaskCanceledException)
        {
            logger.LogWarning("熊出没情報の取得がタイムアウトしました。");
            return FetchResult<List<BearSighting>>.Failure([], "サーバーへの接続がタイムアウトしました。", FetchErrorKind.Timeout);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized || ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            logger.LogError(ex, "API 認証エラー (HTTP {StatusCode})", ex.StatusCode);
            return FetchResult<List<BearSighting>>.Failure([], $"API 認証に失敗しました ({ex.StatusCode})。", FetchErrorKind.Auth);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is not null && (int)ex.StatusCode >= 500)
        {
            logger.LogError(ex, "サーバーエラー (HTTP {StatusCode})", ex.StatusCode);
            return FetchResult<List<BearSighting>>.Failure([], $"サーバーエラーが発生しました ({ex.StatusCode})。", FetchErrorKind.Server);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "ネットワークエラー");
            return FetchResult<List<BearSighting>>.Failure([], "ネットワーク接続に失敗しました。", FetchErrorKind.Network);
        }
        catch (System.Text.Json.JsonException ex)
        {
            logger.LogError(ex, "レスポンス解析エラー");
            return FetchResult<List<BearSighting>>.Failure([], "サーバーからの応答を解析できませんでした。", FetchErrorKind.Parse);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "熊出没情報の取得に失敗しました。");
            return FetchResult<List<BearSighting>>.Failure([], "データの取得に失敗しました。", FetchErrorKind.Unknown);
        }
    }

    /// <summary>
    /// BearInfo API の URL を構築します。
    /// </summary>
    /// <param name="city">地域フィルタです。</param>
    /// <param name="limit">取得件数です。</param>
    /// <returns>構築された URL 文字列です。</returns>
    private string BuildBearInfoUrl(string city, int limit, int offset = 0)
    {
        var baseUrl = $"{settings.BaseUrl}/api/BearInfo";
        var queryParams = new List<string>();

        if (!string.IsNullOrEmpty(settings.FunctionKey))
            queryParams.Add($"code={Uri.EscapeDataString(settings.FunctionKey)}");

        if (!string.IsNullOrEmpty(city))
            queryParams.Add($"region={Uri.EscapeDataString(city)}");

        queryParams.Add($"limit={limit}");

        if (offset > 0)
            queryParams.Add($"offset={offset}");

        return queryParams.Count > 0
            ? $"{baseUrl}?{string.Join("&", queryParams)}"
            : baseUrl;
    }

    /// <summary>
    /// API レスポンスアイテムを BearSighting に変換します。
    /// </summary>
    /// <param name="item">API レスポンスアイテムです。</param>
    /// <returns>変換された BearSighting です。</returns>
    private static BearSighting MapToSighting(BearInfoItem item)
    {
        var category = InferBearCategory(item.Reason);
        // region/place の先頭 "MM/dd" プレフィックスから出没日を抽出
        var (sightingDate, cleanRegion) = ExtractSightingDate(item.Region);
        var (_, cleanPlace) = ExtractSightingDate(item.Place);
        var displayDate = sightingDate ?? item.StrDateTime;
        var isRecent = IsRecentDate(displayDate);

        return new BearSighting(
            Id: item.Id,
            Date: displayDate,
            Location: cleanPlace,
            City: cleanRegion,
            Latitude: item.Latitude ?? 0,
            Longitude: item.Longitude ?? 0,
            Description: item.Reason,
            Category: category,
            IsRecent: isRecent
        );
    }

    /// <summary>
    /// region/place フィールドの先頭から出没日プレフィックスを抽出します。
    /// </summary>
    /// <param name="value">元の文字列です（例: "10/26富山市"）。</param>
    /// <returns>抽出された日付文字列とプレフィックスを除いた残り文字列のタプルです。</returns>
    public static (string? Date, string CleanValue) ExtractSightingDate(string value)
    {
        if (string.IsNullOrEmpty(value)) return (null, value);

        // 月部分: 先頭から '/' までの 1〜2 桁の数字
        var slashIdx = value.IndexOf('/');
        if (slashIdx < 1 || slashIdx > 2) return (null, value);

        for (var i = 0; i < slashIdx; i++)
        {
            if (!char.IsAsciiDigit(value[i])) return (null, value);
        }

        // 日部分: '/' の後の 1〜2 桁の数字
        var dayEnd = slashIdx + 1;
        while (dayEnd < value.Length && char.IsAsciiDigit(value[dayEnd])) dayEnd++;

        if (dayEnd - slashIdx - 1 < 1 || dayEnd - slashIdx - 1 > 2) return (null, value);

        if (!int.TryParse(value.AsSpan(0, slashIdx), out var month) || month < 1 || month > 12)
            return (null, value);
        if (!int.TryParse(value.AsSpan(slashIdx + 1, dayEnd - slashIdx - 1), out var day) || day < 1 || day > 31)
            return (null, value);

        var rest = value[dayEnd..];
        var year = DateTime.Now.Year;

        try
        {
            var dt = new DateTime(year, month, day);
            if (dt > DateTime.Now.Date) dt = dt.AddYears(-1);
            return (dt.ToString("yyyy/MM/dd"), rest);
        }
        catch (ArgumentOutOfRangeException)
        {
            return (null, value);
        }
    }

    /// <summary>
    /// 出動理由から熊情報カテゴリ（目撃・痕跡・被害）を推定します。
    /// </summary>
    /// <param name="reason">出動理由テキストです。</param>
    /// <returns>推定されたカテゴリです。</returns>
    private static string InferBearCategory(string reason)
    {
        if (string.IsNullOrEmpty(reason)) return "目撃";

        if (reason.Contains("被害", StringComparison.Ordinal) ||
            reason.Contains("破壊", StringComparison.Ordinal) ||
            reason.Contains("食害", StringComparison.Ordinal) ||
            reason.Contains("荒ら", StringComparison.Ordinal))
            return "被害";

        if (reason.Contains("痕跡", StringComparison.Ordinal) ||
            reason.Contains("足跡", StringComparison.Ordinal) ||
            reason.Contains("爪痕", StringComparison.Ordinal) ||
            reason.Contains("糞", StringComparison.Ordinal) ||
            reason.Contains("クマ棚", StringComparison.Ordinal) ||
            reason.Contains("剥が", StringComparison.Ordinal))
            return "痕跡";

        return "目撃";
    }

    /// <summary>
    /// 座標・日時・場所が同一のアイテムを重複排除します。
    /// API が同一目撃情報に異なる ID を割り当てて返す場合に対応します。
    /// </summary>
    /// <param name="sightings">重複排除前のリストです。</param>
    /// <returns>重複排除後のリストです。</returns>
    private static List<BearSighting> DeduplicateByContent(List<BearSighting> sightings)
    {
        var seen = new HashSet<(double Lat, double Lng, string Date, string Location, string City)>();
        var result = new List<BearSighting>(sightings.Count);

        foreach (var s in sightings)
        {
            if (seen.Add((s.Latitude, s.Longitude, s.Date, s.Location, s.City)))
            {
                result.Add(s);
            }
        }

        return result;
    }

    /// <summary>
    /// 日時文字列が直近かどうかを判定します。
    /// </summary>
    /// <param name="dateTimeStr">日時文字列です。</param>
    /// <returns>直近の場合は true です。</returns>
    private static bool IsRecentDate(string dateTimeStr)
    {
        if (DateTime.TryParse(dateTimeStr, out var dt))
        {
            return (DateTime.Now - dt).TotalDays <= RecentDaysThreshold;
        }
        return false;
    }

#if DEBUG
    /// <summary>
    /// デバッグ用モックデータを生成します。
    /// </summary>
    /// <returns>モック熊出没情報のリストです。</returns>
    private static List<BearSighting> GetMockSightings()
    {
        var now = DateTime.Now;
        return
        [
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
        ];
    }
#endif
}
