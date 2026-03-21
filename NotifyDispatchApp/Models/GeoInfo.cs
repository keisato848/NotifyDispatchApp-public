namespace NotifyDispatchApp.Models;

/// <summary>
/// 地域情報（名前と郵便番号）を表すレコードです。
/// </summary>
/// <param name="Name">地域名です。</param>
/// <param name="PostalCode">郵便番号です。</param>
public record GeoInfo(string Name, string PostalCode);

/// <summary>
/// 地域情報のキャッシュリストです。
/// </summary>
public static class GeoInfos
{
    /// <summary>
    /// キャッシュ済みの地域情報リストです。
    /// </summary>
    public static List<GeoInfo> geoInfos = [];
}

