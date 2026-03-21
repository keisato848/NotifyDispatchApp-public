using CoreGraphics;
using CoreLocation;
using MapKit;
using Microsoft.Maui.Handlers;
using NotifyDispatchApp.Controls;
using NotifyDispatchApp.Models;
using UIKit;

namespace NotifyDispatchApp.Platforms.iOS.Handlers;

/// <summary>
/// ClusterMap の iOS ネイティブハンドラです。
/// MKMapView 上でグリッドベースのクラスタリングを行い、集約マーカーと単体ピンを描画します。
/// </summary>
public class ClusterMapHandler : ViewHandler<ClusterMap, MKMapView>
{
    /// <summary>
    /// 前回の再クラスタリング時の緯度幅（度）です。ズーム変化検知に使用します。
    /// </summary>
    private double _previousLatDeg;

    /// <summary>
    /// 範囲変更通知のデバウンス用キャンセルトークンです。
    /// </summary>
    private CancellationTokenSource? _boundsDebounce;

    /// <summary>
    /// MKMapView の Delegate 準備前に設定されたアイテムを保持します。
    /// </summary>
    private List<BearSighting>? _pendingItems;

    /// <summary>
    /// MKMapView 用のデリゲートインスタンスです。
    /// </summary>
    private ClusterMapDelegate? _delegate;

    /// <summary>
    /// 自動スパイダー展開の緯度幅しきい値（度）です。
    /// 表示領域の緯度幅がこの値未満（≒ズーム16以上）になるとクラスタを個別ピンに展開します。
    /// </summary>
    private const double AutoSpiderfyLatDeg = 0.005;

    /// <summary>
    /// カテゴリ別デフォルトマーカー色です。CategoryStylesMap 未設定時のフォールバックに使用します。
    /// </summary>
    private static readonly Dictionary<string, string> DefaultCategoryColors = new()
    {
        ["sighting"] = "#E53935",
        ["trace"] = "#FB8C00",
        ["damage"] = "#8E24AA",
    };

    /// <summary>
    /// ClusterMap のプロパティとハンドラメソッドのマッピングです。
    /// </summary>
    public static readonly IPropertyMapper<ClusterMap, ClusterMapHandler> ClusterMapMapper =
        new PropertyMapper<ClusterMap, ClusterMapHandler>(ViewHandler.ViewMapper)
        {
            [nameof(ClusterMap.ItemsSource)] = MapItems,
            [nameof(ClusterMap.IsShowingUser)] = MapIsShowingUser,
            [nameof(ClusterMap.MapTypeValue)] = MapMapType,
        };

    /// <summary>
    /// ClusterMapHandler の新しいインスタンスを初期化します。
    /// </summary>
    public ClusterMapHandler() : base(ClusterMapMapper)
    {
    }

    /// <summary>
    /// プラットフォーム固有の MKMapView を生成します。
    /// </summary>
    /// <returns>新しい MKMapView インスタンスです。</returns>
    protected override MKMapView CreatePlatformView()
    {
        return new MKMapView(CGRect.Empty);
    }

    /// <summary>
    /// ハンドラ接続時に Delegate 設定と MoveToRegion 購読を行います。
    /// </summary>
    /// <param name="platformView">対象の MKMapView です。</param>
    protected override void ConnectHandler(MKMapView platformView)
    {
        base.ConnectHandler(platformView);

        _delegate = new ClusterMapDelegate(this);
        platformView.Delegate = _delegate;

        if (VirtualView is not null)
        {
            VirtualView.MoveToRegionRequested += OnMoveToRegionRequested;
        }

        if (VirtualView?.IsShowingUser == true)
        {
            platformView.ShowsUserLocation = true;
        }

        platformView.MapType = ConvertMapType(VirtualView?.MapTypeValue ?? 0);

        // Delegate 設定前に設定されたアイテムを処理
        if (_pendingItems is not null)
        {
            ReclusterAndRender(_pendingItems);
            _pendingItems = null;
        }
    }

    /// <summary>
    /// ハンドラ切断時に Delegate 解除・イベント解除・アイコンキャッシュクリアを行います。
    /// </summary>
    /// <param name="platformView">対象の MKMapView です。</param>
    protected override void DisconnectHandler(MKMapView platformView)
    {
        if (VirtualView is not null)
        {
            VirtualView.MoveToRegionRequested -= OnMoveToRegionRequested;
        }

        platformView.Delegate = null;
        _delegate = null;

        _boundsDebounce?.Cancel();
        _boundsDebounce = null;
        _pendingItems = null;

        ClusterIconGenerator.ClearCache();

        base.DisconnectHandler(platformView);
    }

    /// <summary>
    /// ItemsSource プロパティ変更時にクラスタリングとマーカー描画を実行します。
    /// </summary>
    /// <param name="handler">ハンドラインスタンスです。</param>
    /// <param name="view">ClusterMap インスタンスです。</param>
    private static void MapItems(ClusterMapHandler handler, ClusterMap view)
    {
        var items = view.ItemsSource?.OfType<BearSighting>().ToList();
        if (handler._delegate is null)
        {
            handler._pendingItems = items;
            return;
        }
        handler.ReclusterAndRender(items);
    }

    /// <summary>
    /// IsShowingUser プロパティ変更時に現在地表示を切り替えます。
    /// </summary>
    /// <param name="handler">ハンドラインスタンスです。</param>
    /// <param name="view">ClusterMap インスタンスです。</param>
    private static void MapIsShowingUser(ClusterMapHandler handler, ClusterMap view)
    {
        if (handler.PlatformView is null) return;
        handler.PlatformView.ShowsUserLocation = view.IsShowingUser;
    }

    /// <summary>
    /// MapTypeValue プロパティ変更時に地図種別を切り替えます。
    /// </summary>
    /// <param name="handler">ハンドラインスタンスです。</param>
    /// <param name="view">ClusterMap インスタンスです。</param>
    private static void MapMapType(ClusterMapHandler handler, ClusterMap view)
    {
        if (handler.PlatformView is null) return;
        handler.PlatformView.MapType = ConvertMapType(view.MapTypeValue);
    }

    /// <summary>
    /// 指定アイテムをクラスタリングし、MKMapView 上に Annotation を描画します。
    /// </summary>
    /// <param name="items">描画対象の目撃情報リストです。</param>
    internal void ReclusterAndRender(List<BearSighting>? items)
    {
        if (PlatformView is null) return;

        // 既存の ClusterAnnotation を除去（MKUserLocation は保持）
        var existing = PlatformView.Annotations
            .Where(a => a is ClusterAnnotation)
            .ToArray();
        if (existing.Length > 0)
        {
            PlatformView.RemoveAnnotations(existing);
        }

        if (items is null or { Count: 0 }) return;

        var latDeg = PlatformView.Region.Span.LatitudeDelta;
        if (latDeg <= 0) latDeg = 1.0;
        _previousLatDeg = latDeg;
        var autoSpiderfy = latDeg < AutoSpiderfyLatDeg;
        var clusters = GeoClusterer.Cluster(items, latDeg);

        var annotations = new List<ClusterAnnotation>();
        foreach (var cluster in clusters)
        {
            if (cluster.IsCluster && autoSpiderfy)
            {
                // 高ズーム → クラスタ内アイテムを円形に展開して個別ピンとして描画
                RenderSpiderfied(cluster, annotations);
            }
            else
            {
                annotations.Add(new ClusterAnnotation(cluster));
            }
        }

        if (annotations.Count > 0)
        {
            PlatformView.AddAnnotations(annotations.ToArray());
        }
    }

    /// <summary>
    /// クラスタ内のアイテムを円形に配置し、個別ピンの Annotation を生成します。
    /// 半径約33mの円上に等間隔で配置します。
    /// </summary>
    /// <param name="cluster">展開するクラスタです。</param>
    /// <param name="annotations">生成した Annotation を追加するリストです。</param>
    private static void RenderSpiderfied(ClusterItem cluster, List<ClusterAnnotation> annotations)
    {
        var centerLat = cluster.Latitude;
        var centerLng = cluster.Longitude;
        var count = cluster.Items.Count;
        var radiusDeg = 0.0003; // ~33m
        var lngScale = Math.Cos(centerLat * Math.PI / 180.0);

        for (var i = 0; i < count; i++)
        {
            var angle = 2.0 * Math.PI * i / count;
            var lat = centerLat + radiusDeg * Math.Cos(angle);
            var lng = centerLng + radiusDeg * Math.Sin(angle) / (lngScale > 0.01 ? lngScale : 0.01);

            var sighting = cluster.Items[i];
            var annotation = new ClusterAnnotation(new ClusterItem
            {
                IsCluster = false,
                Latitude = sighting.Latitude,
                Longitude = sighting.Longitude,
                Count = 1,
                DominantCategory = sighting.Category,
                SingleItem = sighting,
                Items = [sighting],
            })
            {
                // 展開位置にオーバーライド
                Coordinate = new CLLocationCoordinate2D(lat, lng)
            };
            annotations.Add(annotation);
        }
    }

    /// <summary>
    /// MoveToRegion リクエストを処理し、MKMapView の表示領域を移動します。
    /// </summary>
    private void OnMoveToRegionRequested(object? sender, MoveToRegionRequestEventArgs e)
    {
        if (PlatformView is null) return;

        var latDeg = e.RadiusKm / 111.0;
        var lngDeg = e.RadiusKm / (111.0 * Math.Cos(e.Latitude * Math.PI / 180.0));
        var region = new MKCoordinateRegion(
            new CLLocationCoordinate2D(e.Latitude, e.Longitude),
            new MKCoordinateSpan(latDeg * 2, lngDeg * 2));

        PlatformView.SetRegion(region, animated: false);
    }

    /// <summary>
    /// カテゴリ名から対応するマーカー色（Hex）を解決します。
    /// </summary>
    /// <param name="category">カテゴリ名（日本語）です。</param>
    /// <returns>色の Hex 文字列です。</returns>
    internal string GetColorForCategory(string category)
    {
        var layerId = BearInfoPageViewModel.MapCategoryToLayerId(category);
        if (VirtualView?.CategoryStylesMap?.TryGetValue(layerId, out var style) == true)
            return style.StrokeColorHex;
        return DefaultCategoryColors.GetValueOrDefault(layerId, "#E53935");
    }

    /// <summary>
    /// カテゴリ名から対応するグリフテキストを返します。
    /// </summary>
    /// <param name="category">カテゴリ名（日本語）です。</param>
    /// <returns>グリフ絵文字です。</returns>
    internal static string GetGlyphForCategory(string category) => category switch
    {
        "目撃" => "👁️",
        "痕跡" => "🐾",
        "被害" => "⚠️",
        _ => "📍",
    };

    /// <summary>
    /// 250ms デバウンス付きで表示範囲変更を VirtualView に通知します。
    /// </summary>
    internal async void NotifyBoundsChangedDebounced()
    {
        _boundsDebounce?.Cancel();
        _boundsDebounce = new CancellationTokenSource();
        try
        {
            await Task.Delay(250, _boundsDebounce.Token);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        if (PlatformView is null || VirtualView is null) return;

        var region = PlatformView.Region;
        VirtualView.RaiseMapBoundsChanged(new MapBounds
        {
            SouthLat = region.Center.Latitude - region.Span.LatitudeDelta / 2,
            NorthLat = region.Center.Latitude + region.Span.LatitudeDelta / 2,
            WestLng = region.Center.Longitude - region.Span.LongitudeDelta / 2,
            EastLng = region.Center.Longitude + region.Span.LongitudeDelta / 2,
        });
    }

    /// <summary>
    /// MapTypeValue の整数値を MKMapType に変換します。
    /// </summary>
    /// <param name="value">0=Street, 1=Satellite, 2=Hybrid です。</param>
    /// <returns>MKMapType 列挙値です。</returns>
    private static MKMapType ConvertMapType(int value) => value switch
    {
        1 => MKMapType.Satellite,
        2 => MKMapType.Hybrid,
        _ => MKMapType.Standard,
    };

    /// <summary>
    /// MKMapView の全コールバックを処理するデリゲートクラスです。
    /// iOS では Delegate プロパティ設定時に C# イベント購読が無効化されるため、
    /// 全コールバックをこのクラスに集約します。
    /// </summary>
    private class ClusterMapDelegate : MKMapViewDelegate
    {
        private readonly ClusterMapHandler _handler;

        /// <summary>
        /// ClusterMapDelegate の新しいインスタンスを初期化します。
        /// </summary>
        /// <param name="handler">親ハンドラです。</param>
        public ClusterMapDelegate(ClusterMapHandler handler) => _handler = handler;

        /// <summary>
        /// Annotation のビューを返します。クラスタと単体ピンで異なるビューを生成します。
        /// </summary>
        /// <param name="mapView">MKMapView インスタンスです。</param>
        /// <param name="annotation">対象の Annotation です。</param>
        /// <returns>カスタム MKAnnotationView、またはデフォルト表示の場合 null です。</returns>
        public override MKAnnotationView? GetViewForAnnotation(MKMapView mapView, IMKAnnotation annotation)
        {
            // ユーザー位置はデフォルトの青点を使用
            if (annotation is MKUserLocation)
                return null;

            if (annotation is not ClusterAnnotation clusterAnnotation)
                return null;

            var item = clusterAnnotation.Item;

            if (item.IsCluster)
            {
                // クラスタマーカー: カスタム UIImage アイコン
                const string reuseId = "ClusterPin";
                var view = mapView.DequeueReusableAnnotation(reuseId);
                if (view is null)
                {
                    view = new MKAnnotationView(annotation, reuseId);
                }
                else
                {
                    view.Annotation = annotation;
                }

                var colorHex = _handler.GetColorForCategory(item.DominantCategory);
                view.Image = ClusterIconGenerator.Create(item.Count, colorHex);
                view.CanShowCallout = false;
                return view;
            }
            else
            {
                // 単体ピン: MKMarkerAnnotationView（バルーン型）
                const string reuseId = "SinglePin";
                var markerView = mapView.DequeueReusableAnnotation(reuseId) as MKMarkerAnnotationView;
                if (markerView is null)
                {
                    markerView = new MKMarkerAnnotationView(annotation, reuseId);
                }
                else
                {
                    markerView.Annotation = annotation;
                }

                var sighting = item.SingleItem;
                if (sighting is not null)
                {
                    var colorHex = _handler.GetColorForCategory(sighting.Category);
                    markerView.MarkerTintColor = ClusterIconGenerator.ParseColor(colorHex);
                    markerView.GlyphText = GetGlyphForCategory(sighting.Category);
                }

                markerView.CanShowCallout = true;
                markerView.RightCalloutAccessoryView = UIButton.FromType(UIButtonType.DetailDisclosure);
                return markerView;
            }
        }

        /// <summary>
        /// 表示領域変更完了時にズーム変化を検知して再クラスタリングし、範囲変更を通知します。
        /// </summary>
        /// <param name="mapView">MKMapView インスタンスです。</param>
        /// <param name="animated">アニメーション中かどうかです。</param>
        public override void RegionChanged(MKMapView mapView, bool animated)
        {
            var latDeg = mapView.Region.Span.LatitudeDelta;

            // ズーム変化がしきい値を超えた場合のみ再クラスタリング
            if (Math.Abs(latDeg - _handler._previousLatDeg) > 0.001)
            {
                _handler._previousLatDeg = latDeg;
                var items = _handler.VirtualView?.ItemsSource?.OfType<BearSighting>().ToList();
                if (items is { Count: > 0 })
                {
                    _handler.ReclusterAndRender(items);
                }
            }

            // ViewModel へ範囲変更を 250ms デバウンス付きで通知
            _handler.NotifyBoundsChangedDebounced();
        }

        /// <summary>
        /// Annotation 選択時にクラスタなら構成アイテムの範囲にズームインします。
        /// 単体ピンの場合は Callout が自動表示されるため追加処理は不要です。
        /// </summary>
        /// <param name="mapView">MKMapView インスタンスです。</param>
        /// <param name="view">選択された MKAnnotationView です。</param>
        public override void DidSelectAnnotationView(MKMapView mapView, MKAnnotationView view)
        {
            if (view.Annotation is not ClusterAnnotation annotation) return;

            var item = annotation.Item;
            if (!item.IsCluster) return; // 単体ピンは Callout 表示のみ

            mapView.DeselectAnnotation(annotation, animated: false);

            // クラスタ → 構成アイテムの範囲にズームイン
            var rect = MKMapRect.Null;
            foreach (var s in item.Items)
            {
                var point = MKMapPoint.FromCoordinate(
                    new CLLocationCoordinate2D(s.Latitude, s.Longitude));
                var pointRect = new MKMapRect(point.X, point.Y, 0.1, 0.1);
                rect = MKMapRect.Union(rect, pointRect);
            }

            mapView.SetVisibleMapRect(rect,
                new UIEdgeInsets(50, 50, 50, 50), animated: true);
        }

        /// <summary>
        /// Callout のアクセサリボタンタップ時に VirtualView へアイテム選択を通知します。
        /// </summary>
        /// <param name="mapView">MKMapView インスタンスです。</param>
        /// <param name="view">対象の MKAnnotationView です。</param>
        /// <param name="control">タップされたコントロールです。</param>
        public override void CalloutAccessoryControlTapped(MKMapView mapView, MKAnnotationView view, UIControl control)
        {
            if (view.Annotation is not ClusterAnnotation annotation) return;

            if (annotation.Item.SingleItem is { } sighting)
            {
                _handler.VirtualView?.RaiseItemSelected(sighting);
            }
        }
    }
}
