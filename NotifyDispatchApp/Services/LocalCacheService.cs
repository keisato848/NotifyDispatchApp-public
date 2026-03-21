using System.Text.Json;
using Microsoft.Extensions.Logging;
using NotifyDispatchApp.Models;

namespace NotifyDispatchApp.Services;

/// <summary>
/// ローカルキャッシュの読み書きを行うインターフェースです。
/// </summary>
public interface ILocalCacheService
{
    /// <summary>
    /// キャッシュ済み出動情報を取得します。
    /// </summary>
    /// <param name="region">地域名です。</param>
    /// <returns>キャッシュされた出動情報のリストです。</returns>
    Task<List<DispatchInfo>> LoadDispatchCacheAsync(string region);

    /// <summary>
    /// 出動情報をキャッシュにマージ保存します。
    /// </summary>
    /// <param name="region">地域名です。</param>
    /// <param name="newItems">新規取得した出動情報です。</param>
    /// <returns>マージ後の全出動情報リストです。</returns>
    Task<List<DispatchInfo>> MergeDispatchCacheAsync(string region, List<DispatchInfo> newItems);

    /// <summary>
    /// キャッシュ済み熊出没情報を取得します。
    /// </summary>
    /// <returns>キャッシュされた熊出没情報のリストです。</returns>
    Task<List<BearSighting>> LoadBearCacheAsync();

    /// <summary>
    /// 熊出没情報をキャッシュにマージ保存します。
    /// </summary>
    /// <param name="newItems">新規取得した熊出没情報です。</param>
    /// <returns>マージ後の全熊出没情報リストです。</returns>
    Task<List<BearSighting>> MergeBearCacheAsync(List<BearSighting> newItems);

    /// <summary>
    /// 全キャッシュをクリアします。
    /// </summary>
    Task ClearAllAsync();

    /// <summary>
    /// キャッシュの統計情報を取得します。
    /// </summary>
    /// <returns>キャッシュ統計です。</returns>
    CacheStats GetStats();
}

/// <summary>
/// キャッシュの統計情報です。
/// </summary>
/// <param name="DispatchRegionCount">出動情報のキャッシュ済み地域数です。</param>
/// <param name="DispatchItemCount">出動情報の合計件数です。</param>
/// <param name="BearItemCount">熊出没情報の合計件数です。</param>
/// <param name="TotalSizeBytes">合計ファイルサイズ（バイト）です。</param>
public record CacheStats(int DispatchRegionCount, int DispatchItemCount, int BearItemCount, long TotalSizeBytes);

/// <summary>
/// JSON ファイルベースのローカルキャッシュサービスです。
/// </summary>
public class LocalCacheService(ILogger<LocalCacheService> logger) : ILocalCacheService
{
    private static string CacheDir =>
#if ANDROID || IOS || MACCATALYST || WINDOWS
        Path.Combine(FileSystem.AppDataDirectory, "cache");
#else
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NotifyDispatchApp", "cache");
#endif

    /// <summary>
    /// キャッシュ済み出動情報を取得します。
    /// </summary>
    /// <param name="region">地域名です。</param>
    /// <returns>キャッシュされた出動情報のリストです。</returns>
    public async Task<List<DispatchInfo>> LoadDispatchCacheAsync(string region)
    {
        return await LoadAsync(DispatchCachePath(region), AppJsonContext.Default.ListDispatchInfo);
    }

    /// <summary>
    /// 出動情報をキャッシュにマージ保存します。
    /// </summary>
    /// <param name="region">地域名です。</param>
    /// <param name="newItems">新規取得した出動情報です。</param>
    /// <returns>マージ後の全出動情報リストです。</returns>
    public async Task<List<DispatchInfo>> MergeDispatchCacheAsync(string region, List<DispatchInfo> newItems)
    {
        var cached = await LoadDispatchCacheAsync(region);
        var merged = MergeById(cached, newItems, d => d.Id ?? "");
        await SaveAsync(DispatchCachePath(region), merged, AppJsonContext.Default.ListDispatchInfo);
        logger.LogInformation("出動情報キャッシュ更新: {Region} 既存{Old}件 + 新規{New}件 → {Total}件",
            region, cached.Count, newItems.Count, merged.Count);
        return merged;
    }

    /// <summary>
    /// キャッシュ済み熊出没情報を取得します。
    /// </summary>
    /// <returns>キャッシュされた熊出没情報のリストです。</returns>
    public async Task<List<BearSighting>> LoadBearCacheAsync()
    {
        return await LoadAsync(BearCachePath(), AppJsonContext.Default.ListBearSighting);
    }

    /// <summary>
    /// 熊出没情報をキャッシュにマージ保存します。
    /// ID ベースのマージ後、座標・日時・場所が同一のコンテンツ重複も排除します。
    /// </summary>
    /// <param name="newItems">新規取得した熊出没情報です。</param>
    /// <returns>マージ後の全熊出没情報リストです。</returns>
    public async Task<List<BearSighting>> MergeBearCacheAsync(List<BearSighting> newItems)
    {
        var cached = await LoadBearCacheAsync();
        var merged = MergeById(cached, newItems, b => b.Id);

        // 異なるIDだが同一内容の重複を排除
        var deduped = DeduplicateByContent(merged);
        if (deduped.Count < merged.Count)
        {
            logger.LogInformation("キャッシュ コンテンツ重複排除: {Before}件 → {After}件 ({Removed}件除去)",
                merged.Count, deduped.Count, merged.Count - deduped.Count);
        }

        await SaveAsync(BearCachePath(), deduped, AppJsonContext.Default.ListBearSighting);
        logger.LogInformation("熊出没情報キャッシュ更新: 既存{Old}件 + 新規{New}件 → {Total}件",
            cached.Count, newItems.Count, deduped.Count);
        return deduped;
    }

    /// <summary>
    /// 全キャッシュをクリアします。
    /// </summary>
    public Task ClearAllAsync()
    {
        if (Directory.Exists(CacheDir))
        {
            Directory.Delete(CacheDir, true);
            logger.LogInformation("キャッシュを全削除しました。");
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// キャッシュの統計情報を取得します。
    /// </summary>
    /// <returns>キャッシュ統計です。</returns>
    public CacheStats GetStats()
    {
        if (!Directory.Exists(CacheDir))
            return new CacheStats(0, 0, 0, 0);

        var files = Directory.GetFiles(CacheDir, "*.json");
        var totalSize = files.Sum(f => new FileInfo(f).Length);

        var dispatchFiles = files.Where(f => Path.GetFileName(f).StartsWith("dispatch_")).ToArray();
        var dispatchRegionCount = dispatchFiles.Length;
        var dispatchItemCount = dispatchFiles.Sum(CountJsonArrayItems);

        var bearFile = files.FirstOrDefault(f => Path.GetFileName(f) == "bear.json");
        var bearItemCount = bearFile is not null ? CountJsonArrayItems(bearFile) : 0;

        return new CacheStats(dispatchRegionCount, dispatchItemCount, bearItemCount, totalSize);
    }

    /// <summary>
    /// JSON 配列ファイルの要素数を返します。
    /// </summary>
    /// <param name="filePath">JSON ファイルパスです。</param>
    /// <returns>配列の要素数です。ファイルが不正な場合は 0 です。</returns>
    private static int CountJsonArrayItems(string filePath)
    {
        try
        {
            var bytes = File.ReadAllBytes(filePath);
            using var doc = JsonDocument.Parse(bytes);
            return doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement.GetArrayLength()
                : 0;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// ID ベースでリストをマージします。新しいデータで既存を上書きします。
    /// </summary>
    /// <typeparam name="T">データ型です。</typeparam>
    /// <param name="existing">既存のリストです。</param>
    /// <param name="incoming">新規のリストです。</param>
    /// <param name="idSelector">ID を取得する関数です。</param>
    /// <returns>マージ済みリストです。</returns>
    private static List<T> MergeById<T>(List<T> existing, List<T> incoming, Func<T, string> idSelector)
    {
        var dict = new Dictionary<string, T>();
        foreach (var item in existing)
        {
            var id = idSelector(item);
            if (!string.IsNullOrEmpty(id))
                dict[id] = item;
        }
        foreach (var item in incoming)
        {
            var id = idSelector(item);
            if (!string.IsNullOrEmpty(id))
                dict[id] = item; // 新しいデータで上書き
        }
        return [.. dict.Values];
    }

    /// <summary>
    /// 座標・日時・場所が同一の熊出没情報を重複排除します。
    /// API が同一目撃情報に異なる ID を割り当てるケースに対応します。
    /// </summary>
    /// <param name="items">重複排除前のリストです。</param>
    /// <returns>重複排除後のリストです。</returns>
    private static List<BearSighting> DeduplicateByContent(List<BearSighting> items)
    {
        var seen = new HashSet<(double Lat, double Lng, string Date, string Location, string City)>();
        var result = new List<BearSighting>(items.Count);

        foreach (var s in items)
        {
            if (seen.Add((s.Latitude, s.Longitude, s.Date, s.Location, s.City)))
            {
                result.Add(s);
            }
        }

        return result;
    }

    /// <summary>
    /// JSON ファイルからリストを読み込みます。
    /// </summary>
    /// <typeparam name="T">データ型です。</typeparam>
    /// <param name="path">ファイルパスです。</param>
    /// <param name="typeInfo">JSON 型情報です。</param>
    /// <returns>読み込んだリストです。</returns>
    private async Task<List<T>> LoadAsync<T>(string path, System.Text.Json.Serialization.Metadata.JsonTypeInfo<List<T>> typeInfo)
    {
        try
        {
            if (!File.Exists(path)) return [];
            var json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize(json, typeInfo) ?? [];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "キャッシュ読込失敗: {Path}", path);
            return [];
        }
    }

    /// <summary>
    /// リストを JSON ファイルに保存します。
    /// </summary>
    /// <typeparam name="T">データ型です。</typeparam>
    /// <param name="path">ファイルパスです。</param>
    /// <param name="items">保存するリストです。</param>
    /// <param name="typeInfo">JSON 型情報です。</param>
    private async Task SaveAsync<T>(string path, List<T> items, System.Text.Json.Serialization.Metadata.JsonTypeInfo<List<T>> typeInfo)
    {
        try
        {
            Directory.CreateDirectory(CacheDir);
            var json = JsonSerializer.Serialize(items, typeInfo);
            await File.WriteAllTextAsync(path, json);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "キャッシュ保存失敗: {Path}", path);
        }
    }

    /// <summary>
    /// 出動情報キャッシュのファイルパスを返します。
    /// </summary>
    /// <param name="region">地域名です。</param>
    /// <returns>ファイルパスです。</returns>
    private static string DispatchCachePath(string region) =>
        Path.Combine(CacheDir, $"dispatch_{SanitizeFileName(region)}.json");

    /// <summary>
    /// 熊出没情報キャッシュのファイルパスを返します。
    /// </summary>
    /// <returns>ファイルパスです。</returns>
    private static string BearCachePath() =>
        Path.Combine(CacheDir, "bear.json");

    /// <summary>
    /// ファイル名に使用できない文字を除去します。
    /// </summary>
    /// <param name="name">元のファイル名です。</param>
    /// <returns>サニタイズ済みファイル名です。</returns>
    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Where(c => !invalid.Contains(c)).ToArray());
    }
}
