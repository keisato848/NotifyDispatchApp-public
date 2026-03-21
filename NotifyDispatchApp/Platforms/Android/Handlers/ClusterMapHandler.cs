using Android.App;
using Android.Gms.Maps;
using Android.Gms.Maps.Model;
using Android.OS;
using Microsoft.Maui.Handlers;
using NotifyDispatchApp.Controls;
using NotifyDispatchApp.Models;

namespace NotifyDispatchApp.Platforms.Android.Handlers;

/// <summary>
/// ClusterMap の Android ネイティブハンドラです。
/// GoogleMap 上でグリッドベースのクラスタリングを行い、集約マーカーと単体ピンを描画します。
/// </summary>
public class ClusterMapHandler : ViewHandler<ClusterMap, MapView>
{
    /// <summary>
    /// 現在の GoogleMap インスタンスです。
    /// </summary>
    private GoogleMap? _googleMap;

    /// <summary>
    /// 前回の再クラスタリング時の緯度幅（度）です。ズーム変化検知に使用します。
    /// </summary>
    private double _previousLatDeg;

    /// <summary>
    /// 範囲変更通知のデバウンス用キャンセルトークンです。
    /// </summary>
    private CancellationTokenSource? _boundsDebounce;

    /// <summary>
    /// GoogleMap 準備前に設定されたアイテムを保持します。
    /// </summary>
    private List<BearSighting>? _pendingItems;

    /// <summary>
    /// Activity ライフサイクルコールバックです。
    /// </summary>
    private MapLifecycleCallbacks? _lifecycleCallbacks;

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
    /// プラットフォーム固有の MapView を生成します。
    /// </summary>
    /// <returns>新しい MapView インスタンスです。</returns>
    protected override MapView CreatePlatformView()
    {
        return new MapView(Context);
    }

    /// <summary>
    /// ハンドラ接続時に MapView のライフサイクルを開始し、GoogleMap の取得を要求します。
    /// </summary>
    /// <param name="platformView">対象の MapView です。</param>
    protected override void ConnectHandler(MapView platformView)
    {
        base.ConnectHandler(platformView);

        platformView.OnCreate(null);
        platformView.OnResume();

        platformView.GetMapAsync(new MapReadyCallback(this));

        // Activity ライフサイクルコールバックを登録
        _lifecycleCallbacks = new MapLifecycleCallbacks(platformView);
        if (Platform.CurrentActivity?.Application is global::Android.App.Application app)
        {
            app.RegisterActivityLifecycleCallbacks(_lifecycleCallbacks);
        }

        // MoveToRegion リクエストを購読
        if (VirtualView is not null)
        {
            VirtualView.MoveToRegionRequested += OnMoveToRegionRequested;
        }
    }

    /// <summary>
    /// ハンドラ切断時にリスナー解除・MapView 破棄・Bitmap キャッシュクリアを行います。
    /// </summary>
    /// <param name="platformView">対象の MapView です。</param>
    protected override void DisconnectHandler(MapView platformView)
    {
        if (VirtualView is not null)
        {
            VirtualView.MoveToRegionRequested -= OnMoveToRegionRequested;
        }

        if (_googleMap is not null)
        {
            _googleMap.CameraIdle -= OnCameraIdle;
            _googleMap.MarkerClick -= OnMarkerClick;
            _googleMap.InfoWindowClick -= OnInfoWindowClick;
            _googleMap = null;
        }

        if (_lifecycleCallbacks is not null)
        {
            if (Platform.CurrentActivity?.Application is global::Android.App.Application app)
            {
                app.UnregisterActivityLifecycleCallbacks(_lifecycleCallbacks);
            }
            _lifecycleCallbacks = null;
        }

        _boundsDebounce?.Cancel();
        _boundsDebounce = null;
        _pendingItems = null;

        platformView.OnPause();
        platformView.OnDestroy();

        ClusterIconGenerator.ClearCache();

        base.DisconnectHandler(platformView);
    }

    /// <summary>
    /// GoogleMap の準備完了時に初期設定とイベントリスナー登録を行います。
    /// </summary>
    /// <param name="googleMap">準備完了した GoogleMap インスタンスです。</param>
    private void OnMapReady(GoogleMap googleMap)
    {
        _googleMap = googleMap;

        googleMap.UiSettings.ZoomControlsEnabled = true;
        googleMap.UiSettings.MyLocationButtonEnabled = true;
        googleMap.UiSettings.MapToolbarEnabled = false;

        googleMap.MapType = ConvertMapType(VirtualView?.MapTypeValue ?? 0);

        if (VirtualView?.IsShowingUser == true)
        {
            try { googleMap.MyLocationEnabled = true; }
            catch { /* 権限未付与の場合は無視 */ }
        }

        googleMap.CameraIdle += OnCameraIdle;
        googleMap.MarkerClick += OnMarkerClick;
        googleMap.InfoWindowClick += OnInfoWindowClick;

        // GoogleMap 準備前に設定されたアイテムを処理
        if (_pendingItems is not null)
        {
            ReclusterAndRender(_pendingItems);
            _pendingItems = null;
        }
    }

    /// <summary>
    /// ItemsSource プロパティ変更時にクラスタリングとマーカー描画を実行します。
    /// </summary>
    /// <param name="handler">ハンドラインスタンスです。</param>
    /// <param name="view">ClusterMap インスタンスです。</param>
    private static void MapItems(ClusterMapHandler handler, ClusterMap view)
    {
        var items = view.ItemsSource?.OfType<BearSighting>().ToList();
        if (handler._googleMap is null)
        {
            handler._pendingItems = items;
            return;
        }
        handler.ReclusterAndRender(items);
    }

    /// <summary>
    /// IsShowingUser プロパティ変更時に現在地レイヤーの表示を切り替えます。
    /// </summary>
    /// <param name="handler">ハンドラインスタンスです。</param>
    /// <param name="view">ClusterMap インスタンスです。</param>
    private static void MapIsShowingUser(ClusterMapHandler handler, ClusterMap view)
    {
        if (handler._googleMap is null) return;
        try { handler._googleMap.MyLocationEnabled = view.IsShowingUser; }
        catch { /* 権限未付与の場合は無視 */ }
    }

    /// <summary>
    /// MapTypeValue プロパティ変更時に地図種別を切り替えます。
    /// </summary>
    /// <param name="handler">ハンドラインスタンスです。</param>
    /// <param name="view">ClusterMap インスタンスです。</param>
    private static void MapMapType(ClusterMapHandler handler, ClusterMap view)
    {
        if (handler._googleMap is null) return;
        handler._googleMap.MapType = ConvertMapType(view.MapTypeValue);
    }

    /// <summary>
    /// 指定アイテムをクラスタリングし、GoogleMap 上にマーカーを描画します。
    /// </summary>
    /// <param name="items">描画対象の目撃情報リストです。</param>
    private void ReclusterAndRender(List<BearSighting>? items)
    {
        if (_googleMap is null) return;

        _googleMap.Clear();

        if (items is null or { Count: 0 }) return;

        var latDeg = GetCurrentLatitudeDegrees();
        _previousLatDeg = latDeg;
        var autoSpiderfy = latDeg < AutoSpiderfyLatDeg;
        var clusters = GeoClusterer.Cluster(items, latDeg);
        var metrics = Context?.Resources?.DisplayMetrics;

        foreach (var cluster in clusters)
        {
            var position = new LatLng(cluster.Latitude, cluster.Longitude);
            var colorHex = GetColorForCategory(cluster.DominantCategory);

            if (cluster.IsCluster)
            {
                if (autoSpiderfy)
                {
                    // 高ズーム → クラスタ内アイテムを円形に展開して個別ピンとして描画
                    RenderSpiderfied(cluster);
                }
                else
                {
                    // 通常クラスタマーカー
                    var options = new MarkerOptions()!
                        .SetPosition(position)!
                        .SetTitle($"{cluster.Count}件")!
                        .Anchor(0.5f, 0.5f)!;

                    if (metrics is not null)
                    {
                        var icon = ClusterIconGenerator.Create(cluster.Count, colorHex, metrics);
                        options.SetIcon(icon);
                    }

                    var marker = _googleMap.AddMarker(options);
                    if (marker is not null)
                        marker.Tag = new ClusterItemWrapper(cluster);
                }
            }
            else
            {
                var sighting = cluster.SingleItem!;
                var hue = GetHueFromHex(colorHex);
                var options = new MarkerOptions()!
                    .SetPosition(position)!
                    .SetTitle(sighting.Location)!
                    .SetSnippet($"{sighting.Category} | {sighting.Date}")!
                    .SetIcon(BitmapDescriptorFactory.DefaultMarker(hue))!;

                var marker = _googleMap.AddMarker(options);
                if (marker is not null)
                    marker.Tag = new ClusterItemWrapper(cluster);
            }
        }
    }

    /// <summary>
    /// カメラ停止時にズーム変化を検知して再クラスタリングし、範囲変更を通知します。
    /// </summary>
    private void OnCameraIdle(object? sender, EventArgs e)
    {
        if (_googleMap is null) return;

        var latDeg = GetCurrentLatitudeDegrees();

        // ズーム変化がしきい値を超えた場合のみ再クラスタリング
        if (Math.Abs(latDeg - _previousLatDeg) > 0.001)
        {
            _previousLatDeg = latDeg;
            var items = VirtualView?.ItemsSource?.OfType<BearSighting>().ToList();
            if (items is { Count: > 0 })
            {
                ReclusterAndRender(items);
            }
        }

        // ViewModel へ範囲変更を 250ms デバウンス付きで通知
        NotifyBoundsChangedDebounced();
    }

    /// <summary>
    /// マーカータップ時にクラスタなら構成アイテムの範囲にズームイン、単体なら InfoWindow を表示します。
    /// 高ズーム時はクラスタが自動展開されるため、ズームインにより再描画で個別ピンになります。
    /// </summary>
    private void OnMarkerClick(object? sender, GoogleMap.MarkerClickEventArgs e)
    {
        var marker = e.Marker;
        if (marker?.Tag is not ClusterItemWrapper wrapper)
            return;

        var item = wrapper.Item;
        if (item.IsCluster)
        {
            // クラスタ → 構成アイテムの範囲にズームイン
            var builder = new LatLngBounds.Builder();
            foreach (var s in item.Items)
            {
                builder.Include(new LatLng(s.Latitude, s.Longitude));
            }
            _googleMap?.AnimateCamera(
                CameraUpdateFactory.NewLatLngBounds(builder.Build(), 100));
            e.Handled = true;
        }
        else
        {
            // 単体 → InfoWindow 表示
            marker.ShowInfoWindow();
            e.Handled = true;
        }
    }

    /// <summary>
    /// クラスタ内のアイテムを円形に配置し、個別ピンとして描画します。
    /// 半径約33mの円上に等間隔で配置します。
    /// </summary>
    /// <param name="cluster">展開するクラスタです。</param>
    private void RenderSpiderfied(ClusterItem cluster)
    {
        if (_googleMap is null) return;

        var center = new LatLng(cluster.Latitude, cluster.Longitude);
        var count = cluster.Items.Count;
        var radiusDeg = 0.0003; // ~33m
        var lngScale = Math.Cos(center.Latitude * Math.PI / 180.0);

        for (var i = 0; i < count; i++)
        {
            var angle = 2.0 * Math.PI * i / count;
            var lat = center.Latitude + radiusDeg * Math.Cos(angle);
            var lng = center.Longitude + radiusDeg * Math.Sin(angle) / (lngScale > 0.01 ? lngScale : 0.01);

            var sighting = cluster.Items[i];
            var colorHex = GetColorForCategory(sighting.Category);
            var hue = GetHueFromHex(colorHex);

            var options = new MarkerOptions()!
                .SetPosition(new LatLng(lat, lng))!
                .SetTitle(sighting.Location)!
                .SetSnippet($"{sighting.Category} | {sighting.Date}")!
                .SetIcon(BitmapDescriptorFactory.DefaultMarker(hue))!;

            var marker = _googleMap.AddMarker(options);
            if (marker is not null)
            {
                marker.Tag = new ClusterItemWrapper(new ClusterItem
                {
                    IsCluster = false,
                    Latitude = sighting.Latitude,
                    Longitude = sighting.Longitude,
                    Count = 1,
                    DominantCategory = sighting.Category,
                    SingleItem = sighting,
                    Items = [sighting],
                });
            }
        }
    }

    /// <summary>
    /// InfoWindow タップ時に VirtualView へアイテム選択を通知します。
    /// </summary>
    private void OnInfoWindowClick(object? sender, GoogleMap.InfoWindowClickEventArgs e)
    {
        if (e.Marker?.Tag is not ClusterItemWrapper wrapper) return;
        if (wrapper.Item.SingleItem is { } sighting)
        {
            VirtualView?.RaiseItemSelected(sighting);
        }
    }

    /// <summary>
    /// MoveToRegion リクエストを処理し、GoogleMap のカメラを移動します。
    /// </summary>
    private void OnMoveToRegionRequested(object? sender, MoveToRegionRequestEventArgs e)
    {
        if (_googleMap is null) return;

        // 半径 km → 緯度・経度の度数に変換
        var latDeg = e.RadiusKm / 111.0;
        var lngDeg = e.RadiusKm / (111.0 * Math.Cos(e.Latitude * Math.PI / 180.0));

        var bounds = new LatLngBounds(
            new LatLng(e.Latitude - latDeg, e.Longitude - lngDeg),
            new LatLng(e.Latitude + latDeg, e.Longitude + lngDeg));

        _googleMap.MoveCamera(CameraUpdateFactory.NewLatLngBounds(bounds, 0));
    }

    /// <summary>
    /// 現在の表示領域の緯度幅（度）を取得します。
    /// </summary>
    /// <returns>緯度幅です。取得失敗時は 1.0 を返します。</returns>
    private double GetCurrentLatitudeDegrees()
    {
        if (_googleMap is null) return 1.0;
        try
        {
            var bounds = _googleMap.Projection.VisibleRegion.LatLngBounds;
            return bounds.Northeast.Latitude - bounds.Southwest.Latitude;
        }
        catch
        {
            return 1.0;
        }
    }

    /// <summary>
    /// カテゴリ名から対応するマーカー色（Hex）を解決します。
    /// </summary>
    /// <param name="category">カテゴリ名（日本語）です。</param>
    /// <returns>色の Hex 文字列です。</returns>
    private string GetColorForCategory(string category)
    {
        var layerId = BearInfoPageViewModel.MapCategoryToLayerId(category);
        if (VirtualView?.CategoryStylesMap?.TryGetValue(layerId, out var style) == true)
            return style.StrokeColorHex;
        return DefaultCategoryColors.GetValueOrDefault(layerId, "#E53935");
    }

    /// <summary>
    /// 250ms デバウンス付きで表示範囲変更を VirtualView に通知します。
    /// </summary>
    private async void NotifyBoundsChangedDebounced()
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

        if (_googleMap is null || VirtualView is null) return;

        try
        {
            var latLngBounds = _googleMap.Projection.VisibleRegion.LatLngBounds;
            VirtualView.RaiseMapBoundsChanged(new MapBounds
            {
                SouthLat = latLngBounds.Southwest.Latitude,
                NorthLat = latLngBounds.Northeast.Latitude,
                WestLng = latLngBounds.Southwest.Longitude,
                EastLng = latLngBounds.Northeast.Longitude,
            });
        }
        catch { /* Projection 取得失敗時は無視 */ }
    }

    /// <summary>
    /// MapTypeValue の整数値を GoogleMap の MapType 定数に変換します。
    /// </summary>
    /// <param name="value">0=Street, 1=Satellite, 2=Hybrid です。</param>
    /// <returns>GoogleMap.MapType* 定数です。</returns>
    private static int ConvertMapType(int value) => value switch
    {
        1 => GoogleMap.MapTypeSatellite,
        2 => GoogleMap.MapTypeHybrid,
        _ => GoogleMap.MapTypeNormal,
    };

    /// <summary>
    /// 色の Hex 文字列から HSV の Hue 値を算出します。DefaultMarker の色指定に使用します。
    /// </summary>
    /// <param name="hex">色の Hex 文字列（例: "#E53935"）です。</param>
    /// <returns>Hue 値（0-360）です。</returns>
    private static float GetHueFromHex(string hex)
    {
        var color = global::Android.Graphics.Color.ParseColor(hex);
        var hsv = new float[3];
        global::Android.Graphics.Color.ColorToHSV(color, hsv);
        return hsv[0];
    }

    /// <summary>
    /// ClusterItem を GoogleMap Marker.Tag として保持するための Java ラッパーです。
    /// </summary>
    private class ClusterItemWrapper : Java.Lang.Object
    {
        /// <summary>
        /// ラップされた ClusterItem です。
        /// </summary>
        public ClusterItem Item { get; }

        /// <summary>
        /// ClusterItemWrapper の新しいインスタンスを初期化します。
        /// </summary>
        /// <param name="item">ラップする ClusterItem です。</param>
        public ClusterItemWrapper(ClusterItem item) => Item = item;
    }

    /// <summary>
    /// IOnMapReadyCallback の実装です。GoogleMap 準備完了をハンドラに中継します。
    /// </summary>
    private class MapReadyCallback : Java.Lang.Object, IOnMapReadyCallback
    {
        private readonly ClusterMapHandler _handler;

        /// <summary>
        /// MapReadyCallback の新しいインスタンスを初期化します。
        /// </summary>
        /// <param name="handler">通知先のハンドラです。</param>
        public MapReadyCallback(ClusterMapHandler handler) => _handler = handler;

        /// <summary>
        /// GoogleMap の準備完了時に呼び出されます。
        /// </summary>
        /// <param name="googleMap">準備完了した GoogleMap です。</param>
        public void OnMapReady(GoogleMap googleMap) => _handler.OnMapReady(googleMap);
    }

    /// <summary>
    /// MapView の Android ライフサイクル連携用コールバックです。
    /// Activity の Resume/Pause に応じて MapView のライフサイクルメソッドを呼び出します。
    /// </summary>
    private class MapLifecycleCallbacks : Java.Lang.Object, global::Android.App.Application.IActivityLifecycleCallbacks
    {
        private readonly MapView _mapView;

        /// <summary>
        /// MapLifecycleCallbacks の新しいインスタンスを初期化します。
        /// </summary>
        /// <param name="mapView">ライフサイクルを連携する MapView です。</param>
        public MapLifecycleCallbacks(MapView mapView) => _mapView = mapView;

        /// <summary>
        /// Activity 再開時に MapView.OnResume() を呼び出します。
        /// </summary>
        public void OnActivityResumed(Activity activity) => _mapView.OnResume();

        /// <summary>
        /// Activity 一時停止時に MapView.OnPause() を呼び出します。
        /// </summary>
        public void OnActivityPaused(Activity activity) => _mapView.OnPause();

        /// <summary>
        /// Activity 破棄時の処理です。MapView 破棄はハンドラ側で行うため空実装です。
        /// </summary>
        public void OnActivityDestroyed(Activity activity) { }

        /// <summary>
        /// Activity 生成時の処理です。MapView 生成はハンドラ側で行うため空実装です。
        /// </summary>
        public void OnActivityCreated(Activity activity, Bundle? savedInstanceState) { }

        /// <summary>
        /// Activity 開始時の処理です。空実装です。
        /// </summary>
        public void OnActivityStarted(Activity activity) { }

        /// <summary>
        /// Activity 停止時の処理です。空実装です。
        /// </summary>
        public void OnActivityStopped(Activity activity) { }

        /// <summary>
        /// Activity 状態保存時の処理です。空実装です。
        /// </summary>
        public void OnActivitySaveInstanceState(Activity activity, Bundle outState) { }
    }
}
