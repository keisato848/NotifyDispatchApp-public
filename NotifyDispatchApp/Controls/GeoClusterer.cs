using NotifyDispatchApp.Models;

namespace NotifyDispatchApp.Controls;

/// <summary>
/// グリッドベースの空間クラスタリングアルゴリズムです。
/// 表示領域の緯度幅に応じてグリッドサイズを決定し、近接するアイテムを集約します。
/// </summary>
public static class GeoClusterer
{
    /// <summary>
    /// 表示領域をグリッドに分割する際の分割数です。
    /// </summary>
    private const int GridDivisor = 6;

    /// <summary>
    /// 指定されたアイテムをグリッドベースでクラスタリングします。
    /// </summary>
    /// <param name="items">クラスタリング対象の目撃情報リストです。</param>
    /// <param name="latitudeDegrees">表示領域の緯度幅（度）です。グリッドサイズの決定に使用します。</param>
    /// <returns>クラスタリング結果のリストです。</returns>
    public static List<ClusterItem> Cluster(IReadOnlyList<BearSighting> items, double latitudeDegrees)
    {
        if (items.Count == 0)
            return [];

        if (latitudeDegrees <= 0)
            latitudeDegrees = 0.001;

        var gridSize = latitudeDegrees / GridDivisor;

        var cells = new Dictionary<(long row, long col), List<BearSighting>>();

        foreach (var item in items)
        {
            var row = (long)Math.Floor(item.Latitude / gridSize);
            var col = (long)Math.Floor(item.Longitude / gridSize);
            var key = (row, col);

            if (!cells.TryGetValue(key, out var list))
            {
                list = [];
                cells[key] = list;
            }
            list.Add(item);
        }

        var results = new List<ClusterItem>(cells.Count);

        foreach (var cell in cells.Values)
        {
            if (cell.Count == 1)
            {
                var single = cell[0];
                results.Add(new ClusterItem
                {
                    IsCluster = false,
                    Latitude = single.Latitude,
                    Longitude = single.Longitude,
                    Count = 1,
                    DominantCategory = single.Category,
                    SingleItem = single,
                    Items = [single],
                });
            }
            else
            {
                var centroidLat = 0.0;
                var centroidLng = 0.0;
                foreach (var item in cell)
                {
                    centroidLat += item.Latitude;
                    centroidLng += item.Longitude;
                }
                centroidLat /= cell.Count;
                centroidLng /= cell.Count;

                var dominant = FindDominantCategory(cell);

                results.Add(new ClusterItem
                {
                    IsCluster = true,
                    Latitude = centroidLat,
                    Longitude = centroidLng,
                    Count = cell.Count,
                    DominantCategory = dominant,
                    SingleItem = null,
                    Items = cell,
                });
            }
        }

        return results;
    }

    /// <summary>
    /// アイテムリスト内で最も多いカテゴリを返します。
    /// </summary>
    /// <param name="items">対象のアイテムリストです。</param>
    /// <returns>最頻カテゴリの文字列です。</returns>
    private static string FindDominantCategory(List<BearSighting> items)
    {
        var counts = new Dictionary<string, int>();
        foreach (var item in items)
        {
            counts[item.Category] = counts.GetValueOrDefault(item.Category) + 1;
        }

        var maxCount = 0;
        var dominant = "";
        foreach (var (category, count) in counts)
        {
            if (count > maxCount)
            {
                maxCount = count;
                dominant = category;
            }
        }
        return dominant;
    }
}
