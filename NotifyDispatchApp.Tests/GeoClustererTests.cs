using NotifyDispatchApp.Controls;
using NotifyDispatchApp.Models;
using Xunit;

namespace NotifyDispatchApp.Tests;

/// <summary>
/// GeoClusterer のユニットテストクラスです。
/// </summary>
public class GeoClustererTests
{
    /// <summary>
    /// テスト用の BearSighting を生成します。
    /// </summary>
    private static BearSighting MakeSighting(
        string id, double lat, double lng, string category = "目撃") =>
        new(id, "2025-01-01", "場所", "市", lat, lng, "説明", category, false);

    // ========================================
    // 基本ケース
    // ========================================

    /// <summary>
    /// 空リストを渡すと空のクラスタリスト結果が返ることを検証します。
    /// </summary>
    [Fact]
    public void Cluster_EmptyList_ReturnsEmpty()
    {
        var result = GeoClusterer.Cluster([], 1.0);

        Assert.Empty(result);
    }

    /// <summary>
    /// 単一アイテムは単体ピンとして返ることを検証します。
    /// </summary>
    [Fact]
    public void Cluster_SingleItem_ReturnsSinglePin()
    {
        var items = new[] { MakeSighting("1", 36.69, 137.21) };

        var result = GeoClusterer.Cluster(items, 1.0);

        Assert.Single(result);
        var pin = result[0];
        Assert.False(pin.IsCluster);
        Assert.Equal(1, pin.Count);
        Assert.Equal(36.69, pin.Latitude);
        Assert.Equal(137.21, pin.Longitude);
        Assert.NotNull(pin.SingleItem);
        Assert.Equal("1", pin.SingleItem!.Id);
    }

    // ========================================
    // 離散ケース
    // ========================================

    /// <summary>
    /// 離散的な5件がズームイン時にすべて単体ピンになることを検証します。
    /// </summary>
    [Fact]
    public void Cluster_DiscretePoints_SmallLatDeg_AllSingles()
    {
        var items = new[]
        {
            MakeSighting("1", 36.690, 137.210),
            MakeSighting("2", 36.700, 137.220),
            MakeSighting("3", 36.710, 137.230),
            MakeSighting("4", 36.720, 137.240),
            MakeSighting("5", 36.730, 137.250),
        };

        // latitudeDegrees=0.001 → gridSize≈0.000167 → 各ポイントが別セル
        var result = GeoClusterer.Cluster(items, 0.001);

        Assert.Equal(5, result.Count);
        Assert.All(result, r => Assert.False(r.IsCluster));
    }

    // ========================================
    // 全件同セルケース
    // ========================================

    /// <summary>
    /// 同一座標の10件が広域表示時に1つのクラスタになることを検証します。
    /// </summary>
    [Fact]
    public void Cluster_SameLocation_AllGrouped()
    {
        var items = Enumerable.Range(1, 10)
            .Select(i => MakeSighting(i.ToString(), 36.695, 137.211))
            .ToList();

        // latitudeDegrees=1.0 → gridSize≈0.167 → 全件同じセル
        var result = GeoClusterer.Cluster(items, 1.0);

        Assert.Single(result);
        var cluster = result[0];
        Assert.True(cluster.IsCluster);
        Assert.Equal(10, cluster.Count);
        Assert.Equal(10, cluster.Items.Count);
        Assert.Equal(36.695, cluster.Latitude, 5);
        Assert.Equal(137.211, cluster.Longitude, 5);
    }

    // ========================================
    // 混合ケース
    // ========================================

    /// <summary>
    /// 2グループ＋1孤立で2クラスタ＋1単体になることを検証します。
    /// </summary>
    [Fact]
    public void Cluster_MixedGroups_CorrectClusters()
    {
        // gridSize = 0.5/6 ≈ 0.0833
        // グループA (lat≈36.69): 3件同セル
        // グループB (lat≈36.80): 2件同セル
        // 孤立C (lat≈37.00): 1件
        var items = new[]
        {
            MakeSighting("A1", 36.690, 137.210),
            MakeSighting("A2", 36.695, 137.215),
            MakeSighting("A3", 36.692, 137.212),
            MakeSighting("B1", 36.800, 137.300),
            MakeSighting("B2", 36.805, 137.305),
            MakeSighting("C1", 37.000, 137.500),
        };

        var result = GeoClusterer.Cluster(items, 0.5);

        var clusters = result.Where(r => r.IsCluster).ToList();
        var singles = result.Where(r => !r.IsCluster).ToList();

        Assert.Equal(2, clusters.Count);
        Assert.Single(singles);
        Assert.Equal("C1", singles[0].SingleItem!.Id);

        var groupA = clusters.First(c => c.Count == 3);
        Assert.Equal(3, groupA.Items.Count);

        var groupB = clusters.First(c => c.Count == 2);
        Assert.Equal(2, groupB.Items.Count);
    }

    // ========================================
    // ズームイン展開ケース
    // ========================================

    /// <summary>
    /// 近接3件がズームイン時にすべて単体ピンに分解されることを検証します。
    /// </summary>
    [Fact]
    public void Cluster_NearbyPoints_ZoomIn_SplitsToSingles()
    {
        // 近接3件 (差 ≈ 0.001度)
        var items = new[]
        {
            MakeSighting("1", 36.6950, 137.2100),
            MakeSighting("2", 36.6960, 137.2110),
            MakeSighting("3", 36.6970, 137.2120),
        };

        // latitudeDegrees=0.005 → gridSize≈0.000833 → 各ポイントが別セルに分解
        var result = GeoClusterer.Cluster(items, 0.005);

        Assert.Equal(3, result.Count);
        Assert.All(result, r => Assert.False(r.IsCluster));
    }

    /// <summary>
    /// 同じ近接3件がズームアウト時にクラスタに集約されることを検証します。
    /// </summary>
    [Fact]
    public void Cluster_NearbyPoints_ZoomOut_Grouped()
    {
        var items = new[]
        {
            MakeSighting("1", 36.6950, 137.2100),
            MakeSighting("2", 36.6960, 137.2110),
            MakeSighting("3", 36.6970, 137.2120),
        };

        // latitudeDegrees=1.0 → gridSize≈0.167 → 全件同じセル
        var result = GeoClusterer.Cluster(items, 1.0);

        Assert.Single(result);
        Assert.True(result[0].IsCluster);
        Assert.Equal(3, result[0].Count);
    }

    // ========================================
    // 重心計算ケース
    // ========================================

    /// <summary>
    /// クラスタの緯度・経度が構成アイテムの重心であることを検証します。
    /// </summary>
    [Fact]
    public void Cluster_Centroid_IsAverage()
    {
        var items = new[]
        {
            MakeSighting("1", 36.0, 137.0),
            MakeSighting("2", 36.5, 137.5),
        };

        // gridSize = 10.0/6 ≈ 1.667 → 両方同セル
        var result = GeoClusterer.Cluster(items, 10.0);

        Assert.Single(result);
        var cluster = result[0];
        Assert.Equal(36.25, cluster.Latitude, 5);
        Assert.Equal(137.25, cluster.Longitude, 5);
    }

    // ========================================
    // DominantCategory ケース
    // ========================================

    /// <summary>
    /// 最頻カテゴリが DominantCategory に設定されることを検証します。
    /// </summary>
    [Fact]
    public void Cluster_DominantCategory_IsMostFrequent()
    {
        var items = new[]
        {
            MakeSighting("1", 36.69, 137.21, "目撃"),
            MakeSighting("2", 36.69, 137.21, "痕跡"),
            MakeSighting("3", 36.69, 137.21, "目撃"),
            MakeSighting("4", 36.69, 137.21, "被害"),
            MakeSighting("5", 36.69, 137.21, "目撃"),
        };

        var result = GeoClusterer.Cluster(items, 1.0);

        Assert.Single(result);
        Assert.Equal("目撃", result[0].DominantCategory);
    }

    /// <summary>
    /// 単体ピンの DominantCategory がそのアイテムのカテゴリと一致することを検証します。
    /// </summary>
    [Fact]
    public void Cluster_SingleItem_DominantCategoryMatchesItem()
    {
        var items = new[] { MakeSighting("1", 36.69, 137.21, "被害") };

        var result = GeoClusterer.Cluster(items, 1.0);

        Assert.Single(result);
        Assert.Equal("被害", result[0].DominantCategory);
    }

    // ========================================
    // エッジケース
    // ========================================

    /// <summary>
    /// latitudeDegrees が 0 以下の場合でもクラッシュせず結果を返すことを検証します。
    /// </summary>
    [Fact]
    public void Cluster_ZeroLatDeg_DoesNotCrash()
    {
        var items = new[]
        {
            MakeSighting("1", 36.69, 137.21),
            MakeSighting("2", 36.70, 137.22),
        };

        var result = GeoClusterer.Cluster(items, 0.0);

        Assert.NotEmpty(result);
    }

    /// <summary>
    /// 負の latitudeDegrees でもクラッシュせず結果を返すことを検証します。
    /// </summary>
    [Fact]
    public void Cluster_NegativeLatDeg_DoesNotCrash()
    {
        var items = new[] { MakeSighting("1", 36.69, 137.21) };

        var result = GeoClusterer.Cluster(items, -5.0);

        Assert.Single(result);
    }

    // ========================================
    // 合計件数の保全ケース
    // ========================================

    /// <summary>
    /// クラスタリング後の全 Count 合計が入力件数と一致することを検証します。
    /// </summary>
    [Fact]
    public void Cluster_TotalCount_PreservesInputCount()
    {
        var items = Enumerable.Range(0, 100)
            .Select(i => MakeSighting(
                i.ToString(),
                36.5 + (i % 10) * 0.01,
                137.0 + (i / 10) * 0.01))
            .ToList();

        var result = GeoClusterer.Cluster(items, 0.5);

        var totalCount = result.Sum(r => r.Count);
        Assert.Equal(100, totalCount);

        var totalItems = result.Sum(r => r.Items.Count);
        Assert.Equal(100, totalItems);
    }
}
