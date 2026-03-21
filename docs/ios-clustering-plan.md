# iOS マップクラスタリング実装計画

## 1. 背景と課題

### 現状

Android 版では `ClusterMapHandler` (`ViewHandler<ClusterMap, MapView>`) を実装済み。
共有レイヤー（`ClusterMap`, `ClusterItem`, `GeoClusterer`）は完成しており、
iOS 側はハンドラの追加のみで同じ機能を実現できる。

### Android 版で実装済みの機能

| 機能 | 実装状況 |
|---|---|
| グリッドベースクラスタリング | ✅ `GeoClusterer` (共有) |
| ズームレベル連動の再クラスタリング | ✅ `OnCameraIdle` → `ReclusterAndRender` |
| カテゴリ色付きクラスタアイコン | ✅ `ClusterIconGenerator` (Android Canvas API) |
| 単体ピン + InfoWindow | ✅ `DefaultMarker(hue)` |
| 自動スパイダー展開 | ✅ `AutoSpiderfyLatDeg = 0.005` |
| タップ → ズームイン / 詳細表示 | ✅ `OnMarkerClick` / `OnInfoWindowClick` |
| 表示範囲変更通知 | ✅ `NotifyBoundsChangedDebounced` |
| MoveToRegion | ✅ `OnMoveToRegionRequested` |

### 根本方針

Android と同一のアーキテクチャで iOS ネイティブハンドラを実装する。
`GeoClusterer` と `ClusterMap` は共有のまま、iOS 固有の `MKMapView` 操作のみプラットフォーム層に追加する。

---

## 2. iOS / Android API 対応表

| Android API | iOS (MapKit) 対応 | 備考 |
|---|---|---|
| `MapView` | `MKMapView` | ネイティブマップビュー |
| `GoogleMap` | `MKMapView` (直接操作) | iOS は MapView と地図操作が同一オブジェクト |
| `Marker` + `MarkerOptions` | `MKPointAnnotation` + `MKAnnotationView` | データとビューが分離 |
| `BitmapDescriptor` | `UIImage` | マーカーアイコン |
| `Marker.Tag` (Java.Lang.Object) | `MKAnnotation` サブクラスのプロパティ | データ保持方法の違い |
| `CameraIdle` イベント | `RegionChanged` デリゲート | カメラ停止 → 領域変更完了 |
| `Projection.VisibleRegion` | `MKMapView.Region.Span` | 表示範囲取得 |
| `LatLng` / `LatLngBounds` | `CLLocationCoordinate2D` / `MKCoordinateRegion` | 座標型 |
| `CameraUpdateFactory.NewLatLngBounds` | `SetVisibleMapRect` / `SetRegion` | カメラ移動 |
| Canvas API (Bitmap 描画) | Core Graphics (`UIGraphicsImageRenderer`) | アイコン描画 |
| Activity ライフサイクル連携 | 不要 | iOS は UIView 自動管理 |

---

## 3. アーキテクチャ

```
┌──────────────────────────────────────────────────────────────┐
│  BearInfoPageViewModel (共有・変更なし)                       │
│  ・_allSightings → フィルタ → VisibleSightings               │
└──────────────┬───────────────────────────────────────────────┘
               │ BindableProperty: ItemsSource
┌──────────────▼───────────────────────────────────────────────┐
│  ClusterMap (共有 View・変更なし)                              │
│  ・ItemsSource, IsShowingUser, MapTypeValue                  │
│  ・CategoryStylesMap (Dictionary)                            │
│  ・SelectedItem, MapBoundsChanged, ItemSelected              │
│  ・MoveToRegion(lat, lng, radiusKm)                          │
└──────────────┬───────────────────────────────────────────────┘
               │ MAUI Handler パターン
┌──────────────▼───────────────────────────────────────────────┐
│  ClusterMapHandler (iOS 専用) ★新規                           │
│  TPlatformView = MapKit.MKMapView                            │
│  ・CreatePlatformView → new MKMapView()                      │
│  ・ConnectHandler → Delegate 設定 + MoveToRegion 購読         │
│  ・PropertyMapper: ItemsSource → ReclusterAndRender()        │
│  内部クラス ClusterMapDelegate : MKMapViewDelegate            │
│  ・GetViewForAnnotation → カスタム AnnotationView 返却        │
│  ・RegionChanged → zoom 変化で再クラスタリング                 │
│  ・DidSelectAnnotationView → クラスタ: ズームイン              │
│  ・CalloutAccessoryControlTapped → 詳細表示                   │
└──────────────┬───────────────────────────────────────────────┘
               │ uses
┌──────────────▼───────────────────────────────────────────────┐
│  GeoClusterer (共有ロジック・変更なし)                         │
│  ・入力: List<BearSighting> + latitudeDegrees                │
│  ・グリッド分割 → ClusterItem (単体 or 集約) のリスト          │
└──────────────┬───────────────────────────────────────────────┘
               │ uses (iOS only)
┌──────────────▼───────────────────────────────────────────────┐
│  ClusterIconGenerator (iOS 専用) ★新規                        │
│  ・UIGraphicsImageRenderer で丸アイコン UIImage 生成           │
│  ・件数テキスト + カテゴリ色 + サイズ比例                      │
│  ・キャッシュ: Dictionary<(count, colorHex), UIImage>         │
└──────────────────────────────────────────────────────────────┘
```

---

## 4. ファイル構成

| # | ファイル | 種別 | レイヤー | 概要 |
|---|---------|------|---------|------|
| S1 | `Platforms/iOS/Handlers/ClusterAnnotation.cs` | 新規 | iOS | `ClusterItem` を保持するカスタム Annotation |
| S2 | `Platforms/iOS/Handlers/ClusterIconGenerator.cs` | 新規 | iOS | クラスタマーカー UIImage 生成 |
| S3 | `Platforms/iOS/Handlers/ClusterMapHandler.cs` | 新規 | iOS | iOS ハンドラ本体 + MKMapViewDelegate |
| S4 | `MauiProgram.cs` | 変更 | 共有 | iOS ハンドラ登録追加 |
| — | `Controls/ClusterMap.cs` | 変更なし | 共有 | — |
| — | `Controls/ClusterItem.cs` | 変更なし | 共有 | — |
| — | `Controls/GeoClusterer.cs` | 変更なし | 共有 | — |
| — | `BearInfoPage.xaml` | 変更なし | 共有 | — |
| — | `BearInfoPage.xaml.cs` | 変更なし | 共有 | — |

---

## 5. 各ファイル詳細設計

### 5-1. `Platforms/iOS/Handlers/ClusterAnnotation.cs` — カスタム Annotation

Android の `ClusterItemWrapper : Java.Lang.Object`（Marker.Tag として使用）に相当する。
iOS では Annotation 自体にデータを持たせる。

```csharp
public class ClusterAnnotation : MKPointAnnotation
{
    public ClusterItem Item { get; }

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
```

| プロパティ | クラスタ時 | 単体ピン時 |
|---|---|---|
| `Coordinate` | 重心座標 | 元データの座標 |
| `Title` | `"{count}件"` | `sighting.Location` |
| `Subtitle` | `null` | `"{category} \| {date}"` |

---

### 5-2. `Platforms/iOS/Handlers/ClusterIconGenerator.cs` — アイコン生成

```csharp
public static class ClusterIconGenerator
```

**Android 版との対応:**

| Android | iOS | 備考 |
|---|---|---|
| `Bitmap.CreateBitmap` | `UIGraphicsImageRenderer` | Retina 自動対応 |
| `Canvas` | `CGContext` (renderer ブロック内) | |
| `Paint` (Fill) | `CGContext.SetFillColor` + `FillEllipseInRect` | |
| `Paint` (Stroke) | `CGContext.SetStrokeColor` + `StrokeEllipseInRect` | |
| `Paint` (Text) | `NSString.DrawString` + `NSStringDrawingOptions` | |
| `BitmapDescriptorFactory.FromBitmap` | `UIImage` 直接返却 | |
| `TypedValue.ApplyDimension(Dip)` | pt ベース（Retina 自動対応） | dp の約半分 |
| `Bitmap.Recycle()` | `UIImage.Dispose()` | |

**サイズ区分:**

| 件数範囲 | サイズ (pt) | テキスト (pt) | Android 比較 (dp) |
|---|---|---|---|
| 100+ | 64 | 20 | 128 dp |
| 50〜99 | 56 | 18 | 112 dp |
| 10〜49 | 48 | 16 | 96 dp |
| 2〜9 | 40 | 14 | 80 dp |

※ iOS は pt 単位。@2x/@3x は `UIGraphicsImageRenderer` が自動処理。
dp の約半分の値で同等の物理サイズになる。

**キャッシュ戦略:**

- キー: `(count, colorHex)` — Android 版と同一設計
- `DisconnectHandler` で全 UIImage を `Dispose()` しキャッシュクリア

**描画手順:**

```
1. サイズ区分決定: GetSizeSpec(count) → (sizePt, textPt)
2. キャッシュチェック: _cache[(count, colorHex)] → 有ればそのまま返却
3. renderer = new UIGraphicsImageRenderer(new CGSize(sizePt, sizePt))
4. image = renderer.CreateImage(ctx => {
     // 4a. 塗りつぶし円（カテゴリ色 α=0.85）
     ctx.CGContext.SetFillColor(ParseColor(colorHex).ColorWithAlpha(0.85f).CGColor);
     ctx.CGContext.FillEllipseInRect(new CGRect(0, 0, sizePt, sizePt));

     // 4b. 白縁取り（2pt）
     ctx.CGContext.SetStrokeColor(UIColor.White.CGColor);
     ctx.CGContext.SetLineWidth(2.0f);
     ctx.CGContext.StrokeEllipseInRect(new CGRect(1, 1, sizePt - 2, sizePt - 2));

     // 4c. 件数テキスト（白, Bold, 中央揃え）
     var text = new NSString(count.ToString());
     var attrs = new UIStringAttributes {
         Font = UIFont.BoldSystemFontOfSize(textPt),
         ForegroundColor = UIColor.White,
         ParagraphStyle = centered
     };
     var textSize = text.GetSizeUsingAttributes(attrs);
     var textRect = new CGRect(
         (sizePt - textSize.Width) / 2,
         (sizePt - textSize.Height) / 2,
         textSize.Width, textSize.Height);
     text.DrawString(textRect, attrs);
   });
5. _cache[(count, colorHex)] = image; return image
```

**ClearCache:**

```csharp
public static void ClearCache()
{
    foreach (var img in _cache.Values)
        img.Dispose();
    _cache.Clear();
}
```

**ParseColor ヘルパー（`UIColor.FromHex` 相当）:**

```csharp
internal static UIColor ParseColor(string hex)
{
    hex = hex.TrimStart('#');
    var r = int.Parse(hex[0..2], NumberStyles.HexNumber) / 255f;
    var g = int.Parse(hex[2..4], NumberStyles.HexNumber) / 255f;
    var b = int.Parse(hex[4..6], NumberStyles.HexNumber) / 255f;
    return new UIColor(r, g, b, 1f);
}
```

---

### 5-3. `Platforms/iOS/Handlers/ClusterMapHandler.cs` — iOS ハンドラ

```csharp
public class ClusterMapHandler : ViewHandler<ClusterMap, MKMapView>
```

**⚠️ 設計上の重要事項: Delegate パターン**

iOS の `MKMapView` は `Delegate` プロパティにオブジェクトを設定する方式。
**`Delegate` を設定すると C# イベント購読が無効化される**ため、
**全コールバックを `MKMapViewDelegate` サブクラスに統一**する。

```csharp
// ❌ NG: Delegate 設定とイベント購読は共存できない
mapView.Delegate = myDelegate;
mapView.RegionChanged += ...;  // ← 動作しない

// ✅ OK: 全て Delegate サブクラスに統一
mapView.Delegate = myDelegate;
// myDelegate.RegionChanged() 内で処理
```

**ライフサイクル:**

| フェーズ | 処理 |
|---|---|
| `CreatePlatformView()` | `new MKMapView(CGRect.Empty)` を返す |
| `ConnectHandler()` | `Delegate` 設定、`MoveToRegionRequested` 購読、`_pendingItems` 処理 |
| `DisconnectHandler()` | `Delegate = null`、`MoveToRegionRequested` 解除、アイコンキャッシュクリア |

※ Android と異なり Activity ライフサイクル連携は不要。`MKMapView` は `UIView` として自動管理される。

**PropertyMapper:**

```csharp
public static readonly IPropertyMapper<ClusterMap, ClusterMapHandler> ClusterMapMapper =
    new PropertyMapper<ClusterMap, ClusterMapHandler>(ViewHandler.ViewMapper)
    {
        [nameof(ClusterMap.ItemsSource)]   = MapItems,
        [nameof(ClusterMap.IsShowingUser)] = MapIsShowingUser,
        [nameof(ClusterMap.MapTypeValue)]  = MapMapType,
    };
```

| ClusterMap プロパティ | ハンドラメソッド | iOS API |
|---|---|---|
| `ItemsSource` | `MapItems` → `ReclusterAndRender()` | `RemoveAnnotations` → `AddAnnotations` |
| `IsShowingUser` | `MapIsShowingUser` | `mapView.ShowsUserLocation` |
| `MapTypeValue` | `MapMapType` | `mapView.MapType = MKMapType.*` |

**`ReclusterAndRender()` — マーカー描画の中核ロジック:**

```
1. items = VirtualView.ItemsSource?.OfType<BearSighting>().ToList()
2. if items == null or empty → RemoveAnnotations(全て) → return

3. latDeg = mapView.Region.Span.LatitudeDelta
4. _previousLatDeg = latDeg
5. autoSpiderfy = (latDeg < AutoSpiderfyLatDeg)  // 0.005

6. clusters = GeoClusterer.Cluster(items, latDeg)

7. // ユーザー位置 annotation 以外を全削除
   var existing = mapView.Annotations
       .Where(a => a is ClusterAnnotation).ToArray();
   mapView.RemoveAnnotations(existing);

8. var newAnnotations = new List<ClusterAnnotation>();
   foreach (var cluster in clusters)
   {
       if (cluster.IsCluster && autoSpiderfy)
       {
           // 高ズーム → 円形展開
           RenderSpiderfied(cluster, newAnnotations);
       }
       else
       {
           newAnnotations.Add(new ClusterAnnotation(cluster));
       }
   }
   mapView.AddAnnotations(newAnnotations.ToArray());
```

**MoveToRegion リクエスト:**

```csharp
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
```

**MapType 変換:**

```csharp
private static MKMapType ConvertMapType(int value) => value switch
{
    1 => MKMapType.Satellite,
    2 => MKMapType.Hybrid,
    _ => MKMapType.Standard,
};
```

**カテゴリ色解決:**

```csharp
// Android: Color.ParseColor(hex) → HSV → Hue → DefaultMarker(hue)
// iOS:     hex → UIColor（直接使用可能、HSV 変換不要）
private string GetColorForCategory(string category)
{
    var layerId = BearInfoPageViewModel.MapCategoryToLayerId(category);
    if (VirtualView?.CategoryStylesMap?.TryGetValue(layerId, out var style) == true)
        return style.StrokeColorHex;
    return DefaultCategoryColors.GetValueOrDefault(layerId, "#E53935");
}
```

---

### 5-3a. 内部クラス `ClusterMapDelegate : MKMapViewDelegate`

**全コールバックをこのクラスに集約する。**

```csharp
private class ClusterMapDelegate : MKMapViewDelegate
{
    private readonly ClusterMapHandler _handler;

    public ClusterMapDelegate(ClusterMapHandler handler)
        => _handler = handler;

    // ----- Annotation ビュー生成 -----
    public override MKAnnotationView? GetViewForAnnotation(
        MKMapView mapView, IMKAnnotation annotation) { ... }

    // ----- 領域変更 → 再クラスタリング + 範囲通知 -----
    public override void RegionChanged(
        MKMapView mapView, bool animated) { ... }

    // ----- Annotation 選択 → クラスタ: ズームイン -----
    public override void DidSelectAnnotationView(
        MKMapView mapView, MKAnnotationView view) { ... }

    // ----- Callout ボタンタップ → 詳細画面 -----
    public override void CalloutAccessoryControlTapped(
        MKMapView mapView, MKAnnotationView view, UIControl control) { ... }
}
```

**ConnectHandler / DisconnectHandler での使用:**

```csharp
private ClusterMapDelegate? _delegate;

protected override void ConnectHandler(MKMapView platformView)
{
    base.ConnectHandler(platformView);
    _delegate = new ClusterMapDelegate(this);
    platformView.Delegate = _delegate;
    // ...
}

protected override void DisconnectHandler(MKMapView platformView)
{
    platformView.Delegate = null;
    _delegate = null;
    // ...
    base.DisconnectHandler(platformView);
}
```

---

### 5-3b. `GetViewForAnnotation` — マーカー外見の制御

iOS マップの最重要メソッド。Annotation の種類に応じた AnnotationView を返す。

```
入力: IMKAnnotation annotation
出力: MKAnnotationView?

1. annotation が MKUserLocation の場合:
   → return null（デフォルトの青点を使用）

2. annotation が ClusterAnnotation で IsCluster == true の場合:
   a. ReuseIdentifier = "ClusterPin"
   b. view = mapView.DequeueReusableAnnotation("ClusterPin")
      ?? new MKAnnotationView(annotation, "ClusterPin")
   c. var colorHex = _handler.GetColorForCategory(item.DominantCategory)
   d. view.Image = ClusterIconGenerator.Create(item.Count, colorHex)
   e. view.CanShowCallout = false（タップ時はズームイン）
   f. view.Annotation = annotation（再利用時の更新）
   g. return view

3. annotation が ClusterAnnotation で IsCluster == false の場合:
   a. ReuseIdentifier = "SinglePin"
   b. view = mapView.DequeueReusableAnnotation("SinglePin")
      ?? new MKMarkerAnnotationView(annotation, "SinglePin")
   c. var sighting = item.SingleItem
   d. var colorHex = _handler.GetColorForCategory(sighting.Category)
   e. view.MarkerTintColor = ClusterIconGenerator.ParseColor(colorHex)
   f. view.GlyphText = GetGlyphForCategory(sighting.Category)
      ・"目撃" → "👁️", "痕跡" → "🐾", "被害" → "⚠️"
   g. view.CanShowCallout = true
   h. view.RightCalloutAccessoryView = UIButton.FromType(UIButtonType.DetailDisclosure)
   i. view.Annotation = annotation
   j. return view

4. その他 → return null
```

**MKMarkerAnnotationView vs MKAnnotationView:**

| 用途 | クラス | 理由 |
|---|---|---|
| 単体ピン | `MKMarkerAnnotationView` | バルーン型ピン + GlyphText (iOS 11+) |
| クラスタマーカー | `MKAnnotationView` | カスタム `UIImage` を `.Image` に設定 |

---

### 5-3c. `RegionChanged` — ズーム変化検知と再クラスタリング

```csharp
public override void RegionChanged(MKMapView mapView, bool animated)
{
    var latDeg = mapView.Region.Span.LatitudeDelta;

    // ズーム変化がしきい値を超えた場合のみ再クラスタリング
    if (Math.Abs(latDeg - _handler._previousLatDeg) > 0.001)
    {
        _handler._previousLatDeg = latDeg;
        var items = _handler.VirtualView?.ItemsSource?
            .OfType<BearSighting>().ToList();
        if (items is { Count: > 0 })
        {
            _handler.ReclusterAndRender(items);
        }
    }

    // ViewModel へ範囲変更を 250ms デバウンス付きで通知
    _handler.NotifyBoundsChangedDebounced();
}
```

**デバウンス通知（Android 版と同一ロジック）:**

```csharp
private async void NotifyBoundsChangedDebounced()
{
    _boundsDebounce?.Cancel();
    _boundsDebounce = new CancellationTokenSource();
    try
    {
        await Task.Delay(250, _boundsDebounce.Token);
    }
    catch (TaskCanceledException) { return; }

    if (PlatformView is null || VirtualView is null) return;

    var region = PlatformView.Region;
    VirtualView.RaiseMapBoundsChanged(new MapBounds
    {
        SouthLat = region.Center.Latitude - region.Span.LatitudeDelta / 2,
        NorthLat = region.Center.Latitude + region.Span.LatitudeDelta / 2,
        WestLng  = region.Center.Longitude - region.Span.LongitudeDelta / 2,
        EastLng  = region.Center.Longitude + region.Span.LongitudeDelta / 2,
    });
}
```

---

### 5-3d. `DidSelectAnnotationView` — クラスタタップ → ズームイン

```csharp
public override void DidSelectAnnotationView(MKMapView mapView, MKAnnotationView view)
{
    if (view.Annotation is not ClusterAnnotation annotation) return;
    mapView.DeselectAnnotation(annotation, animated: false);

    var item = annotation.Item;
    if (!item.IsCluster) return;  // 単体ピンは Callout 表示のみ

    // クラスタ → 構成アイテムの範囲にズームイン
    var coords = item.Items
        .Select(s => new CLLocationCoordinate2D(s.Latitude, s.Longitude))
        .ToArray();

    var rect = MKMapRect.Null;
    foreach (var coord in coords)
    {
        var point = MKMapPoint.FromCoordinate(coord);
        var pointRect = new MKMapRect(point.X, point.Y, 0.1, 0.1);
        rect = MKMapRect.Union(rect, pointRect);
    }

    mapView.SetVisibleMapRect(rect,
        new UIEdgeInsets(50, 50, 50, 50), animated: true);
}
```

---

### 5-3e. `CalloutAccessoryControlTapped` — 詳細画面遷移

```csharp
public override void CalloutAccessoryControlTapped(
    MKMapView mapView, MKAnnotationView view, UIControl control)
{
    if (view.Annotation is not ClusterAnnotation annotation) return;

    if (annotation.Item.SingleItem is { } sighting)
    {
        _handler.VirtualView?.RaiseItemSelected(sighting);
    }
}
```

---

## 6. 自動スパイダー展開（Auto-Spiderfy）

Android 版と**完全に同一のロジック**。`ReclusterAndRender` 内で判定。

```
autoSpiderfy = (mapView.Region.Span.LatitudeDelta < AutoSpiderfyLatDeg)  // 0.005

if (cluster.IsCluster && autoSpiderfy):
    → RenderSpiderfied(cluster, annotations) で円形配置の個別ピンを追加
```

```csharp
private void RenderSpiderfied(ClusterItem cluster, List<ClusterAnnotation> annotations)
{
    var center = new CLLocationCoordinate2D(cluster.Latitude, cluster.Longitude);
    var count = cluster.Items.Count;
    var radiusDeg = 0.0003; // ~33m
    var lngScale = Math.Cos(center.Latitude * Math.PI / 180.0);

    for (var i = 0; i < count; i++)
    {
        var angle = 2.0 * Math.PI * i / count;
        var lat = center.Latitude + radiusDeg * Math.Cos(angle);
        var lng = center.Longitude + radiusDeg * Math.Sin(angle)
            / (lngScale > 0.01 ? lngScale : 0.01);

        var sighting = cluster.Items[i];
        annotations.Add(new ClusterAnnotation(new ClusterItem
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
        });
    }
}
```

---

## 7. 変更ファイル詳細

### 7-1. `MauiProgram.cs` — ハンドラ登録

**using ディレクティブ追加:**

```csharp
#if ANDROID
using NotifyDispatchApp.Platforms.Android.Handlers;
#elif IOS
using NotifyDispatchApp.Platforms.iOS.Handlers;
#endif
```

**ハンドラ登録変更:**

現在:
```csharp
builder.ConfigureMauiHandlers(handlers =>
{
#if ANDROID
    handlers.AddHandler<ClusterMap, ClusterMapHandler>();
#endif
});
```

変更後:
```csharp
builder.ConfigureMauiHandlers(handlers =>
{
#if ANDROID
    handlers.AddHandler<ClusterMap, ClusterMapHandler>();
#elif IOS
    handlers.AddHandler<ClusterMap, ClusterMapHandler>();
#endif
});
```

---

## 8. iOS 固有の注意事項

### 8-1. MKMapView Delegate パターンの制約

iOS の `MKMapView` は `Delegate` プロパティに **1 つの** デリゲートオブジェクトを設定する方式。

**重要**: .NET iOS バインディングでは `Delegate` オブジェクトを設定すると、
同じ MKMapView に対する **C# イベント購読 (`+=`) が無効化される**。
これは Xamarin/MAUI の既知の制約。

→ 本実装では **`MKMapViewDelegate` サブクラスに全コールバックを統一**する。

### 8-2. Annotation の再利用

`DequeueReusableAnnotation` を使用して `MKAnnotationView` を再利用する。
iOS の `MKMapView` は UITableView と同様のセル再利用パターン。

```csharp
var view = mapView.DequeueReusableAnnotation("ClusterPin")
    ?? new MKAnnotationView(annotation, "ClusterPin");
view.Annotation = annotation;  // 再利用時はデータを更新
```

### 8-3. 位置情報権限（確認済み ✅）

`Info.plist` に以下のキーが**設定済み**。追加作業不要。

```xml
<key>NSLocationAlwaysAndWhenInUseUsageDescription</key>
<string>位置情報の常時使用を許可する</string>
<key>NSLocationWhenInUseUsageDescription</key>
<string>アプリの使用中は位置情報の使用を許可する</string>
```

### 8-4. csproj の TargetFrameworks

現在の設定:
```xml
<TargetFrameworks>net10.0-android</TargetFrameworks>
<TargetFrameworks Condition="$([MSBuild]::IsOSPlatform('osx'))">
    $(TargetFrameworks);net10.0-ios;net10.0-maccatalyst
</TargetFrameworks>
```

- iOS TFM は **macOS 上でのビルド時のみ**有効
- Windows 上での開発中は Android のみビルドされる
- `#if IOS` プリプロセッサで iOS 専用コードを分離し、**Android ビルドに影響しない**

### 8-5. MKMapView のネイティブクラスタリング機能

iOS 11+ には `MKMapView` 自体にクラスタリング API
(`MKAnnotationView.ClusteringIdentifier`) がある。

**本実装ではこれを使用しない。** 理由:

1. Android と同一の `GeoClusterer` を使い挙動を統一するため
2. ネイティブクラスタリングはアイコンや展開動作のカスタマイズが限定的
3. 自動スパイダー展開は独自実装が必要

→ `ClusterAnnotation` には `ClusteringIdentifier` を設定しない。

### 8-6. パフォーマンス考慮

| 項目 | 対策 |
|---|---|
| 一括描画 | `RemoveAnnotations` + `AddAnnotations` で一括操作（iOS は内部で最適化） |
| ビュー再利用 | `DequeueReusableAnnotation` でメモリ削減 |
| アイコンキャッシュ | `Dictionary<(count, colorHex), UIImage>` で同一 count+color を再利用 |
| デバウンス | `RegionChanged` は高頻度で呼ばれるため 250ms デバウンス |
| ユーザー位置 | `RemoveAnnotations` で `MKUserLocation` を**除外**して青点を維持 |

---

## 9. 実装順序

| Step | 内容 | 依存 | 検証方法 |
|---|---|---|---|
| S1 | `ClusterAnnotation.cs` 作成 | なし | Android ビルド成功（`#if IOS` で分離） |
| S2 | `ClusterIconGenerator.cs` 作成 | なし | Android ビルド成功 |
| S3 | `ClusterMapHandler.cs` 作成 | S1, S2 | Android ビルド成功 |
| S4 | `MauiProgram.cs` にハンドラ登録 | S3 | Android ビルド成功 + 既存テスト全パス |
| S5 | macOS 上で iOS ビルド検証 | S4 | `dotnet build -f net10.0-ios` 成功 |
| S6 | iOS シミュレータ / 実機テスト | S5 | 手動テスト項目の全通過 |

---

## 10. テスト計画

### 10-1. 単体テスト（既存テスト流用）

| テスト | 状態 | 備考 |
|---|---|---|
| `GeoClustererTests` (13件) | ✅ そのまま有効 | 共有ロジック、変更なし |
| `BearSightingTests` (各種) | ✅ そのまま有効 | サービス層、変更なし |

新規テスト追加は不要。iOS 固有コードは `MKMapView` に依存するため単体テスト不可。

### 10-2. 手動テスト（iOS シミュレータ / 実機）

| # | テスト項目 | 確認内容 | Android 対応箇所 |
|---|---|---|---|
| 1 | 起動 → マップ表示 | `MKMapView` が描画されること | `CreatePlatformView` |
| 2 | クラスタマーカー表示 | 件数付き丸アイコンが表示されること | `GetViewForAnnotation` |
| 3 | 単体ピン表示 | カテゴリ色付きバルーンピンが表示されること | `GetViewForAnnotation` |
| 4 | クラスタタップ → ズームイン | 構成アイテムの範囲にアニメーション移動すること | `DidSelectAnnotationView` |
| 5 | 自動スパイダー展開 | 高ズーム時にクラスタが個別ピンに展開されること | `ReclusterAndRender` |
| 6 | 単体ピン Callout 表示 | タイトル・サブタイトルが正しいこと | `GetViewForAnnotation` |
| 7 | Callout 詳細ボタン | `ItemSelected` イベントが発火すること | `CalloutAccessoryControlTapped` |
| 8 | レイヤーフィルタ | チップタップで表示 / 非表示が切り替わること | `MapItems` |
| 9 | 期間フィルタ | 期間チップで表示件数が変化すること | `MapItems` |
| 10 | 現在地表示 | 青点が表示されること（権限付与時） | `MapIsShowingUser` |
| 11 | 地図種別切替 | Standard / Satellite / Hybrid が切り替わること | `MapMapType` |
| 12 | ユーザー位置保持 | 再クラスタリング後も青点が消えないこと | `RemoveAnnotations` フィルタ |

---

## 11. リスク・課題

| # | リスク | 影響 | 対策 |
|---|---|---|---|
| 1 | macOS 環境がないと iOS ビルド・テスト不可 | 開発効率低下 | `#if IOS` で分離し Android ビルドに影響しない設計 |
| 2 | `MKMapView` の annotation 更新時のちらつき | UX 品質 | V1: 全クリア→全追加。V2: diff 方式で差分更新を検討 |
| 3 | `Delegate` 設定でイベント購読が無効化される | バグ原因 | 全コールバックを `MKMapViewDelegate` サブクラスに統一 |
| 4 | `UIColor.FromHex` が標準 API にない | ビルドエラー | `ParseColor` ヘルパーメソッドで Hex → UIColor 変換を自前実装 |
| 5 | iOS シミュレータでの位置情報モック | テスト品質 | Xcode のシミュレータ位置設定で対応 |
| 6 | `MKMapView` ネイティブクラスタリングとの競合 | 予期しない動作 | `ClusteringIdentifier` は設定しない |

---

## 12. 見積もり

| Step | 工数 | 備考 |
|---|---|---|
| S1: ClusterAnnotation | 0.5h | 単純なデータホルダー |
| S2: ClusterIconGenerator | 1.5h | Core Graphics API の調査含む |
| S3: ClusterMapHandler | 3.0h | 最大ファイル。Android 版を参考に移植 |
| S4: MauiProgram.cs | 0.25h | 2行追加のみ |
| S5: macOS ビルド検証 | 0.5h | 環境依存 |
| S6: iOS テスト | 1.0h | シミュレータ手動テスト |
| **合計** | **6.75h** | — |
