using NotifyDispatchApp.Models;

namespace NotifyDispatchApp.Controls;

/// <summary>
/// クラスタリング結果の 1 要素を表すレコードです。
/// 単体ピンまたは複数アイテムの集約マーカーのいずれかを保持します。
/// </summary>
public record ClusterItem
{
    /// <summary>
    /// クラスタ（集約マーカー）かどうかです。false の場合は単体ピンです。
    /// </summary>
    public bool IsCluster { get; init; }

    /// <summary>
    /// 表示座標の緯度です。集約の場合は構成アイテムの重心です。
    /// </summary>
    public double Latitude { get; init; }

    /// <summary>
    /// 表示座標の経度です。集約の場合は構成アイテムの重心です。
    /// </summary>
    public double Longitude { get; init; }

    /// <summary>
    /// 含まれるアイテム数です。
    /// </summary>
    public int Count { get; init; }

    /// <summary>
    /// 最も多いカテゴリです。マーカー色の決定に使用します。
    /// </summary>
    public string DominantCategory { get; init; } = "";

    /// <summary>
    /// 単体ピンの場合の元データです。IsCluster が false のとき有効です。
    /// </summary>
    public BearSighting? SingleItem { get; init; }

    /// <summary>
    /// 集約に含まれる全アイテムです。ズームイン時の範囲計算に使用します。
    /// </summary>
    public List<BearSighting> Items { get; init; } = [];
}
