using CoreLocation;
using MapKit;
using NotifyDispatchApp.Controls;

namespace NotifyDispatchApp.Platforms.iOS.Handlers;

/// <summary>
/// ClusterItem を保持するカスタム MKPointAnnotation です。
/// Android の ClusterItemWrapper (Java.Lang.Object) に相当し、Annotation 自体にデータを持たせます。
/// </summary>
public class ClusterAnnotation : MKPointAnnotation
{
    /// <summary>
    /// この Annotation に紐づくクラスタアイテムです。
    /// </summary>
    public ClusterItem Item { get; }

    /// <summary>
    /// ClusterAnnotation の新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="item">紐づけるクラスタアイテムです。</param>
    public ClusterAnnotation(ClusterItem item)
    {
        Item = item;
        Coordinate = new CLLocationCoordinate2D(item.Latitude, item.Longitude);

        if (item.IsCluster)
        {
            Title = $"{item.Count}件";
            Subtitle = null;
        }
        else if (item.SingleItem is { } sighting)
        {
            Title = sighting.Location;
            Subtitle = $"{sighting.Category} | {sighting.Date}";
        }
    }
}
