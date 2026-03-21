using System.Text.Json.Serialization;

namespace NotifyDispatchApp.Models;

// [Refactor]
// Who: Senior .NET Engineer
// When: 2025-12-14
// Why: HeartRails Express APIのレスポンスモデルをモダン化
// What: Renamed to avoid confusion (Response property vs Response type)
public record GeoResponse(
    [property: JsonPropertyName("response")] GeoResponseBody? ApiResponse
);

public record GeoResponseBody(
    [property: JsonPropertyName("location")] Location[]? Location
);

public record Location(
    [property: JsonPropertyName("city")] string? City,
    [property: JsonPropertyName("city_kana")] string? CityKana,
    [property: JsonPropertyName("town")] string? Town,
    [property: JsonPropertyName("town_kana")] string? TownKana,
    [property: JsonPropertyName("x")] double X,
    [property: JsonPropertyName("y")] double Y,
    [property: JsonPropertyName("prefecture")] string? Prefecture,
    [property: JsonPropertyName("postal")] string? Postal
);
