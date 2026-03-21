using NotifyDispatchApp.Models;
using NotifyDispatchApp.Services;

namespace NotifyDispatchApp.Tests;

/// <summary>
/// テスト用のモックローカルキャッシュサービスです。
/// キャッシュせずそのまま返します。
/// </summary>
internal sealed class MockLocalCacheService : ILocalCacheService
{
    /// <summary>
    /// 空のリストを返します。
    /// </summary>
    public Task<List<DispatchInfo>> LoadDispatchCacheAsync(string region) => Task.FromResult(new List<DispatchInfo>());

    /// <summary>
    /// 新規アイテムをそのまま返します。
    /// </summary>
    public Task<List<DispatchInfo>> MergeDispatchCacheAsync(string region, List<DispatchInfo> newItems) => Task.FromResult(newItems);

    /// <summary>
    /// 空のリストを返します。
    /// </summary>
    public Task<List<BearSighting>> LoadBearCacheAsync() => Task.FromResult(new List<BearSighting>());

    /// <summary>
    /// 新規アイテムをそのまま返します。
    /// </summary>
    public Task<List<BearSighting>> MergeBearCacheAsync(List<BearSighting> newItems) => Task.FromResult(newItems);

    /// <summary>
    /// 何もしません。
    /// </summary>
    public Task ClearAllAsync() => Task.CompletedTask;

    /// <summary>
    /// 空の統計を返します。
    /// </summary>
    public CacheStats GetStats() => new(0, 0, 0, 0);
}
