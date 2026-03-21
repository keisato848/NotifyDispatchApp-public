using System.Text.Json.Serialization;

namespace NotifyDispatchApp.Models;

/// <summary>
/// 熊出没情報を表すレコードです。
/// </summary>
/// <param name="Id">情報のIDです。</param>
/// <param name="Date">目撃日時の文字列です。</param>
/// <param name="Location">場所の説明です。</param>
/// <param name="City">市区町村名です。</param>
/// <param name="Latitude">緯度です。</param>
/// <param name="Longitude">経度です。</param>
/// <param name="Description">詳細説明です。</param>
/// <param name="Category">カテゴリです（目撃・痕跡・被害）。</param>
/// <param name="IsRecent">直近の情報かどうかです。</param>
public record BearSighting(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("date")] string Date,
    [property: JsonPropertyName("location")] string Location,
    [property: JsonPropertyName("city")] string City,
    [property: JsonPropertyName("latitude")] double Latitude,
    [property: JsonPropertyName("longitude")] double Longitude,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("isRecent")] bool IsRecent
);

/// <summary>
/// マップ上のレイヤーを表すクラスです。
/// </summary>
public class MapLayer
{
    /// <summary>
    /// レイヤーの一意識別子です。
    /// </summary>
    public string Id { get; init; } = "";

    /// <summary>
    /// レイヤーの表示名です。
    /// </summary>
    public string Name { get; init; } = "";

    /// <summary>
    /// レイヤーのアイコン絵文字です。
    /// </summary>
    public string Icon { get; init; } = "";

    /// <summary>
    /// レイヤーのテーマカラー（Hex）です。
    /// </summary>
    public string PinColorHex { get; init; } = "#FF0000";

    /// <summary>
    /// レイヤーが表示中かどうかです。
    /// </summary>
    public bool IsVisible { get; set; } = true;

    /// <summary>
    /// このレイヤーに属するピンの数です。
    /// </summary>
    public int Count { get; set; }
}
