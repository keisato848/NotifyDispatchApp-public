using System.Text.Json.Serialization;

namespace NotifyDispatchApp.Models;

/// <summary>
/// 消防出動情報を表す不変レコードです。
/// </summary>
/// <param name="Id">出動情報のIDです。</param>
/// <param name="PartitionKey">パーティションキーです。</param>
/// <param name="Region">地域名です。</param>
/// <param name="StrDateTime">日時文字列です。</param>
/// <param name="Place">場所です。</param>
/// <param name="Reason">出動理由です。</param>
/// <param name="IsCompleted">鎮火済みかどうかです。</param>
public record DispatchInfo(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("partitionKey")] string? PartitionKey,
    [property: JsonPropertyName("region")] string? Region,
    [property: JsonPropertyName("strDateTime")] string? StrDateTime,
    [property: JsonPropertyName("place")] string? Place,
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("isCompleted")] bool IsCompleted
);

/// <summary>
/// マップ表示用の出動情報ピンです。
/// </summary>
/// <param name="Info">出動情報です。</param>
/// <param name="Latitude">緯度です。</param>
/// <param name="Longitude">経度です。</param>
/// <param name="TownName">マッチした町域名です。</param>
public record DispatchMapPin(DispatchInfo Info, double Latitude, double Longitude, string? TownName = null)
{
    /// <summary>
    /// 鎮火済みかどうかです。
    /// </summary>
    public bool IsCompleted => Info.IsCompleted;
};

/// <summary>
/// マップ上のハイライトゾーン定義です。
/// </summary>
/// <param name="RadiusMeters">半径（メートル）です。</param>
/// <param name="FillOpacity">塗りつぶし不透明度（0x00〜0xFF）です。</param>
/// <param name="StrokeOpacity">枠線不透明度（0x00〜0xFF）です。</param>
public record HeatZone(double RadiusMeters, int FillOpacity, int StrokeOpacity);
