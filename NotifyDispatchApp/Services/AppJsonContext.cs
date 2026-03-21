using System.Text.Json.Serialization;
using NotifyDispatchApp.Models;

namespace NotifyDispatchApp.Services;

/// <summary>
/// AOT / トリミング対応の JSON シリアライズコンテキストです。
/// Source Generator により型メタデータをコンパイル時に生成します。
/// </summary>
[JsonSerializable(typeof(ApiPagedResponse<DispatchInfo>))]
[JsonSerializable(typeof(ApiPagedResponse<BearInfoItem>))]
[JsonSerializable(typeof(List<DispatchInfo>))]
[JsonSerializable(typeof(List<BearSighting>))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class AppJsonContext : JsonSerializerContext;
