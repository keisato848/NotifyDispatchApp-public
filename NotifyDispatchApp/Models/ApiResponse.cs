using System.Text.Json.Serialization;

namespace NotifyDispatchApp.Models;

/// <summary>
/// ページネーション付き API レスポンスのラッパーです。
/// </summary>
/// <typeparam name="T">アイテムの型です。</typeparam>
public record ApiPagedResponse<T>
{
    /// <summary>
    /// 返却された件数です。
    /// </summary>
    [JsonPropertyName("count")]
    public int Count { get; init; }

    /// <summary>
    /// リクエストで指定された最大取得件数です。
    /// </summary>
    [JsonPropertyName("limit")]
    public int Limit { get; init; }

    /// <summary>
    /// リクエストで指定された取得開始位置です。
    /// </summary>
    [JsonPropertyName("offset")]
    public int Offset { get; init; }

    /// <summary>
    /// 情報の配列です。
    /// </summary>
    [JsonPropertyName("items")]
    public List<T> Items { get; init; } = [];
}

/// <summary>
/// 熊出没情報の API レスポンスアイテムです。
/// </summary>
public record BearInfoItem
{
    /// <summary>
    /// 情報のIDです。
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    /// <summary>
    /// パーティションキーです。
    /// </summary>
    [JsonPropertyName("partitionKey")]
    public string PartitionKey { get; init; } = "";

    /// <summary>
    /// 地域名です。
    /// </summary>
    [JsonPropertyName("region")]
    public string Region { get; init; } = "";

    /// <summary>
    /// 発生日時文字列です。
    /// </summary>
    [JsonPropertyName("strDateTime")]
    public string StrDateTime { get; init; } = "";

    /// <summary>
    /// 発生場所です。
    /// </summary>
    [JsonPropertyName("place")]
    public string Place { get; init; } = "";

    /// <summary>
    /// 出動理由・概要です。
    /// </summary>
    [JsonPropertyName("reason")]
    public string Reason { get; init; } = "";

    /// <summary>
    /// 完了フラグです。
    /// </summary>
    [JsonPropertyName("isCompleted")]
    public bool IsCompleted { get; init; }

    /// <summary>
    /// カテゴリです。
    /// </summary>
    [JsonPropertyName("category")]
    public string Category { get; init; } = "";

    /// <summary>
    /// 緯度です。
    /// </summary>
    [JsonPropertyName("latitude")]
    public double? Latitude { get; init; }

    /// <summary>
    /// 経度です。
    /// </summary>
    [JsonPropertyName("longitude")]
    public double? Longitude { get; init; }

    /// <summary>
    /// 出典元サイト名です。
    /// </summary>
    [JsonPropertyName("sourceName")]
    public string? SourceName { get; init; }

    /// <summary>
    /// 出典元URLです。
    /// </summary>
    [JsonPropertyName("sourceUrl")]
    public string? SourceUrl { get; init; }
}
