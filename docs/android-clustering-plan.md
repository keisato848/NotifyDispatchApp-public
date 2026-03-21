# Android マップクラスタリング実装計画

## 1. 背景と課題

### 現状の問題

`BearInfoPage.RefreshMapElements()` は `VisibleSightings` の全件に対して MAUI の `Pin` + `Circle` を生成・追加している。
一括ダウンロード後に数千件のデータが存在すると、マップコントロールが大量のオーバーレイを処理しきれずクラッシュする。

```csharp
// 現在のクラッシュ原因コード (BearInfoPage.xaml.cs RefreshMapElements)
foreach (var sighting in _viewModel.VisibleSightings)
{
    bearMap.MapElements.Add(new Circle { ... });  // 数千個
    bearMap.Pins.Add(new Pin { ... });             // 数千個
}
```

### 暫定対策（実施済み）

- 初期表示を現在地中心・半径 1km に限定（Phase 8）
- VisibleRegion 範囲外のピンはフィルタ除外（ViewModel の bounds フィルタ）

### 根本解決

ネイティブ GoogleMap ハンドラで**グリッドベースのクラスタリング**を実装し、
ズームレベルに応じて近接ピンを集約マーカーとして描画する。

---

## 2. 依存関係調査結果

| パッケージ名 | NuGet 存在 | 備考 |
|---|---|---|
| `Xamarin.Google.Android.Maps.Utils` | ❌ 不在 | Java の `ClusterManager` バインディング |
| `Xamarin.Google.Android.Maps.Utils.V3` | ❌ 不在 | 同上 V3 版 |
| `Xamarin.Google.Maps.Android.Utils` | ❌ 不在 | 名前違いで検索 |
| `Xamarin.GooglePlayServices.Maps` | ✅ 120.0.0.1 | 基本 Maps SDK（MAUI Maps が内部利用） |

**結論**: Google Maps Android Utils（ClusterManager）の .NET バインディングは存在しない。
→ **C# 側でグリッドクラスタリングアルゴリズムを実装**し、ネイティブハンドラでマーカーを直接操作する。

---

## 3. アーキテクチャ

```
┌──────────────────────────────────────────────────────────────┐
│  BearInfoPageViewModel                                       │
│  ・_allSightings → フィルタ → VisibleSightings               │
│  ・VisibleSightingsUpdated イベント                           │
└──────────────┬───────────────────────────────────────────────┘
               │ BindableProperty: ItemsSource
┌──────────────▼───────────────────────────────────────────────┐
│  ClusterMap (共有 View)                                      │
│  ・ItemsSource, IsShowingUser, MapType                       │
│  ・CategoryStyles (Dictionary)                               │
│  ・SelectedItem → InfoWindow タップ時に set                   │
│  ・MapBoundsChanged イベント → ViewModel へ範囲通知            │
└──────────────┬───────────────────────────────────────────────┘
               │ MAUI Handler パターン
┌──────────────▼───────────────────────────────────────────────┐
│  ClusterMapHandler (Android 専用)                             │
│  TPlatformView = Android.Gms.Maps.MapView                    │
│  ・CreatePlatformView → new MapView()                        │
│  ・ConnectHandler → MapView lifecycle + GetMapAsync           │
│  ・PropertyMapper: ItemsSource → ReclusterAndRender()        │
│  ・GoogleMap.CameraIdle → zoom 変化で再クラスタリング          │
│  ・Marker タップ → 単体: 情報表示 / 集約: ズームイン            │
└──────────────┬───────────────────────────────────────────────┘
               │ uses
┌──────────────▼───────────────────────────────────────────────┐
│  GeoClusterer (共有ロジック)                                  │
│  ・入力: List<BearSighting> + zoom level (latitudeDegrees)   │
│  ・グリッド分割 → ClusterItem (単体 or 集約) のリスト          │
└──────────────┬───────────────────────────────────────────────┘
               │ uses (Android only)
┌──────────────▼───────────────────────────────────────────────┐
│  ClusterIconGenerator (Android 専用)                          │
│  ・Android Canvas API でクラスタ用丸アイコン Bitmap 生成       │
│  ・件数テキスト + カテゴリ色 + サイズ比例                      │
└──────────────────────────────────────────────────────────────┘
```

---

## 4. ファイル構成

| # | ファイル | 種別 | レイヤー | 概要 |
|---|---------|------|---------|------|
| 1 | `Controls/ClusterMap.cs` | 新規 | 共有 | カスタム View。BindableProperty 定義 |
| 2 | `Controls/ClusterItem.cs` | 新規 | 共有 | クラスタ結果のデータモデル |
| 3 | `Controls/GeoClusterer.cs` | 新規 | 共有 | グリッドベース空間クラスタリングアルゴリズム |
| 4 | `Platforms/Android/Handlers/ClusterMapHandler.cs` | 新規 | Android | Android 用ハンドラ本体 |
| 5 | `Platforms/Android/Handlers/ClusterIconGenerator.cs` | 新規 | Android | クラスタマーカーの Bitmap 生成 |
| 6 | `MauiProgram.cs` | 変更 | 共有 | ハンドラ登録 |
| 7 | `BearInfoPage.xaml` | 変更 | 共有 | `<maps:Map>` → `<controls:ClusterMap>` |
| 8 | `BearInfoPage.xaml.cs` | 変更 | 共有 | RefreshMapElements 削除、イベント接続簡素化 |
| 9 | `NotifyDispatchApp.csproj` | 変更 | 共有 | Windows TFM 削除 |

---

## 5. 各ファイル詳細設計

### 5-1. `Controls/ClusterMap.cs` — 共有カスタム View

```csharp
public class ClusterMap : View
```

**BindableProperty:**

| プロパティ | 型 | 用途 |
|---|---|---|
| `ItemsSource` | `IEnumerable<BearSighting>` | ViewModel の VisibleSightings をバインド |
| `IsShowingUser` | `bool` | 現在地表示 |
| `MapTypeValue` | `int` (0=Street, 1=Satellite, 2=Hybrid) | 地図種別 |
| `CategoryStylesMap` | `Dictionary<string, (Color Fill, Color Stroke)>` | カテゴリ別色定義 |
| `SelectedItem` | `BearSighting?` | InfoWindow タップ時に ViewModel へ通知 |

**イベント:**

| イベント | 引数 | 用途 |
|---|---|---|
| `MapBoundsChanged` | `MapBounds` | カメラ移動後に ViewModel.UpdateMapBounds() 呼び出し |
| `ItemSelected` | `BearSighting` | 個別ピンの InfoWindow タップ |

**メソッド:**

| メソッド | 用途 |
|---|---|
| `MoveToRegion(double lat, double lng, double radiusKm)` | コードビハインドから初期表示位置を指定 |

---

### 5-2. `Controls/ClusterItem.cs` — クラスタ結果モデル

```csharp
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
    /// IsCluster=false の場合の元データです。
    /// </summary>
    public BearSighting? SingleItem { get; init; }

    /// <summary>
    /// 集約に含まれる全アイテムです。ズームイン時の範囲計算に使用します。
    /// </summary>
    public List<BearSighting> Items { get; init; } = [];
}
```

---

### 5-3. `Controls/GeoClusterer.cs` — クラスタリングアルゴリズム

**アルゴリズム: グリッド分割方式**

```
入力: List<BearSighting> items, double latitudeDegrees (表示領域の緯度幅)
出力: List<ClusterItem>

定数: GridDivisor = 6 (表示領域を縦横6分割)

手順:
  1. gridSize = latitudeDegrees / GridDivisor
     ・latitudeDegrees ≈ 0.01 (ズームイン) → gridSize ≈ 0.0017 → ほぼ集約なし
     ・latitudeDegrees ≈ 1.0  (広域)       → gridSize ≈ 0.167  → 大幅に集約

  2. 各 BearSighting を gridKey = (floor(lat/gridSize), floor(lng/gridSize)) に割当

  3. グリッドセルごとにグループ化:
     ・Count == 1 → ClusterItem { IsCluster = false, SingleItem = item }
     ・Count >= 2 → ClusterItem {
         IsCluster = true,
         Latitude = items.Average(Lat),
         Longitude = items.Average(Lng),
         DominantCategory = 最頻カテゴリ,
         Items = 全件
       }
```

**パフォーマンス特性:**

- 計算量: O(n) — Dictionary への挿入 + グループ化の 1 パス
- 5,000 件でも < 1ms（ベンチマーク想定）
- メインスレッドで実行可能（Task.Run 不要）

**ユニットテストケース:**

| テスト | 入力 | 期待結果 |
|---|---|---|
| 空リスト | items=[], latDeg=1.0 | 空リスト |
| 全件別セル | 離散的な5件, latDeg=0.001 | 5個の単体 ClusterItem |
| 全件同セル | 同一座標の10件, latDeg=1.0 | 1個のクラスタ (Count=10) |
| 混合 | 2グループ(3件+2件)+孤立1件 | 2クラスタ + 1単体 |
| ズームイン展開 | 近接3件, latDeg=0.001 | 3個の単体（gridSize が十分小さい） |

---

### 5-4. `Platforms/Android/Handlers/ClusterMapHandler.cs`

```csharp
public class ClusterMapHandler : ViewHandler<ClusterMap, MapView>
```

**ライフサイクル:**

| フェーズ | 処理 |
|---|---|
| `CreatePlatformView()` | `new MapView(Context)` を返す |
| `ConnectHandler()` | `MapView.OnCreate(null)` → `GetMapAsync(callback)` で `GoogleMap` 取得 |
| `OnMapReady(GoogleMap)` | UiSettings 設定、CameraIdleListener 登録、初期位置設定、現在地レイヤー有効化 |
| `DisconnectHandler()` | `MapView.OnDestroy()`、リスナー解除、Bitmap キャッシュ破棄 |

**PropertyMapper:**

```csharp
public static readonly IPropertyMapper<ClusterMap, ClusterMapHandler> Mapper =
    new PropertyMapper<ClusterMap, ClusterMapHandler>(ViewHandler.ViewMapper)
    {
        [nameof(ClusterMap.ItemsSource)]    = MapItems,
        [nameof(ClusterMap.IsShowingUser)]  = MapIsShowingUser,
        [nameof(ClusterMap.MapTypeValue)]   = MapMapType,
    };
```

**`MapItems()` — マーカー描画の中核ロジック:**

```
1. items = VirtualView.ItemsSource?.ToList()
2. latDeg = 現在の VisibleRegion から取得
3. clusters = GeoClusterer.Cluster(items, latDeg)
4. _googleMap.Clear()
5. foreach cluster in clusters:
   ・IsCluster == false:
     → MarkerOptions { Position, Title, Snippet, Icon = カテゴリ色デフォルトピン }
   ・IsCluster == true:
     → MarkerOptions { Position, Icon = ClusterIconGenerator.Create(count, colorHex) }
6. marker.Tag = Java.Lang.Object にラップした ClusterItem
```

**Camera Idle リスナー:**

```
GoogleMap.CameraIdle += () =>
{
    var bounds = _googleMap.Projection.VisibleRegion.LatLngBounds;
    var latDeg = bounds.Northeast.Latitude - bounds.Southwest.Latitude;

    // zoom 変化時のみ再クラスタリング (しきい値: 0.001度)
    if (Math.Abs(latDeg - _previousLatDeg) > 0.001)
    {
        _previousLatDeg = latDeg;
        ReclusterAndRender();
    }

    // ViewModel へ範囲変更通知 (デバウンス 250ms)
    NotifyBoundsChanged(bounds);
};
```

**Marker タップハンドラ:**

```
GoogleMap.MarkerClick += (marker) =>
{
    var item = UnwrapTag(marker.Tag);
    if (item.IsCluster)
    {
        // クラスタ → そのクラスタの範囲にズームイン
        var builder = new LatLngBounds.Builder();
        foreach (var s in item.Items)
            builder.Include(new LatLng(s.Latitude, s.Longitude));
        _googleMap.AnimateCamera(
            CameraUpdateFactory.NewLatLngBounds(builder.Build(), paddingPx: 100));
    }
    else
    {
        // 単体 → InfoWindow 表示
        marker.ShowInfoWindow();
    }
};
```

**Android ライフサイクル連携:**

MapView は Activity ライフサイクルへの連動が必須:

```
ConnectHandler 内で実装:
  Platform.CurrentActivity に IActivityLifecycleCallbacks を登録
  - OnResume  → _mapView.OnResume()
  - OnPause   → _mapView.OnPause()
  - OnDestroy → _mapView.OnDestroy()

DisconnectHandler 内:
  - コールバック解除
  - _mapView.OnDestroy()
```

---

### 5-5. `Platforms/Android/Handlers/ClusterIconGenerator.cs`

```csharp
public static class ClusterIconGenerator
```

**Bitmap 生成仕様:**

| 件数範囲 | サイズ (dp) | 背景色 | テキスト |
|---|---|---|---|
| 2〜9 | 80 | カテゴリ色 (α=0.85) | 件数 (白, 14sp) |
| 10〜49 | 96 | カテゴリ色 (α=0.85) | 件数 (白, 16sp) |
| 50〜99 | 112 | カテゴリ色 (α=0.85) | 件数 (白, 18sp) |
| 100+ | 128 | カテゴリ色 (α=0.85) | 件数 (白, 20sp) |

**キャッシュ戦略:**

- キー: `(sizeCategory, colorHex)` — 最大 12 パターン (4 サイズ × 3 カテゴリ色)
- `DisconnectHandler` で全 Bitmap を `Recycle()` しキャッシュクリア

**描画手順:**

```
1. sizePx = TypedValue.ApplyDimension(dp → px)
2. bitmap = Bitmap.CreateBitmap(sizePx, sizePx, Config.Argb8888)
3. canvas = new Canvas(bitmap)
4. paint.Color = categoryColor, paint.Alpha = 217 (0.85 * 255)
5. canvas.DrawCircle(center, radius, paint)  // 塗りつぶし円
6. paint.Color = White, paint.Style = Stroke, paint.StrokeWidth = 2dp
7. canvas.DrawCircle(center, radius - 1dp, paint)  // 白縁取り
8. textPaint.TextSize = fontSize, textPaint.Color = White, textPaint.TextAlign = Center
9. canvas.DrawText(count.ToString(), centerX, centerY + offset, textPaint)
10. return BitmapDescriptorFactory.FromBitmap(bitmap)
```

---

## 6. 変更ファイル詳細

### 6-1. `MauiProgram.cs`

`.UseMauiMaps()` の後に追加:

```csharp
builder.ConfigureMauiHandlers(handlers =>
{
#if ANDROID
    handlers.AddHandler<ClusterMap, Platforms.Android.Handlers.ClusterMapHandler>();
#endif
});
```

### 6-2. `BearInfoPage.xaml`

**Before:**
```xml
<maps:Map x:Name="bearMap" IsShowingUser="True" MapType="Street" />
```

**After:**
```xml
<controls:ClusterMap x:Name="bearMap"
    ItemsSource="{Binding VisibleSightings}"
    IsShowingUser="True"
    MapTypeValue="0" />
```

- `xmlns:controls="clr-namespace:NotifyDispatchApp.Controls"` を追加
- `xmlns:maps` は不要になるため削除

### 6-3. `BearInfoPage.xaml.cs`

**削除:**
- `RefreshMapElements()` メソッド全体 (L223-262)
- `CategoryStyles` 辞書 (L39-44) — ハンドラ側へ移行
- `CircleZoomThreshold` 定数 — ハンドラ側で管理
- `OnMapPropertyChanged()` — ClusterMap.MapBoundsChanged に置換
- `VisibleSightingsUpdated` の `RefreshMapElements()` 呼び出し — バインディングで自動

**変更:**
- コンストラクタ: `VisibleSightingsUpdated` は `UpdateChipAppearance()` のみに
- `OnAppearing`: `bearMap.MoveToRegion(lat, lng, radiusKm)` に変更
- `OnDisappearing`: `PropertyChanged` 解除 → `MapBoundsChanged` 解除
- `ShowSightingDetail()`: `ClusterMap.ItemSelected` イベントから呼び出し

**結果:** コードビハインドが約 100 行削減される見込み

### 6-4. `NotifyDispatchApp.csproj`

- `TargetFrameworks` から `net10.0-windows10.0.19041.0` を削除
- Windows 固有の `<PropertyGroup Condition="...windows...">` を削除
- `SupportedOSPlatformVersion` の windows 条件を削除

---

## 7. 実装順序

| Step | 作業 | 依存 | 規模 | 所要 |
|---|---|---|---|---|
| S1 | csproj: Windows TFM 削除 | なし | 小 | 5分 |
| S2 | `ClusterItem.cs` 作成 | なし | 小 | 10分 |
| S3 | `GeoClusterer.cs` 作成 + ユニットテスト | S2 | 中 | 30分 |
| S4 | `ClusterMap.cs` 共有 View 作成 | S2 | 中 | 20分 |
| S5 | `ClusterIconGenerator.cs` 作成 | なし | 中 | 20分 |
| S6 | `ClusterMapHandler.cs` 作成 | S3, S4, S5 | **大** | 60分 |
| S7 | `MauiProgram.cs` ハンドラ登録 | S6 | 小 | 5分 |
| S8 | `BearInfoPage.xaml` / `.xaml.cs` 置換 | S4, S7 | 中 | 30分 |
| S9 | ビルド検証 + 実機テスト | S8 | 中 | 30分 |

**合計見積: 約 3.5 時間**

---

## 8. 技術的リスクと対策

| # | リスク | 影響度 | 対策 |
|---|---|---|---|
| 1 | MapView ライフサイクル不整合で ANR/クラッシュ | 高 | `IActivityLifecycleCallbacks` を実装し Resume/Pause/Destroy を確実に中継 |
| 2 | Marker 全 Clear → 再追加でちらつき | 中 | 差分更新: 前回の ClusterItem ID set と比較し変化分のみ操作 |
| 3 | BitmapDescriptor のメモリリーク | 中 | サイズ区分キャッシュ (最大12パターン)、DisconnectHandler で `Bitmap.Recycle()` |
| 4 | iOS 未対応期間の互換性 | 低 | iOS 用フォールバックハンドラ作成（MAUI Map をそのまま使用する stub） |
| 5 | MAUI Maps 内部ハンドラとの競合 | 低 | `ClusterMap` は `Map` を継承せず独立 `View` として定義し干渉を回避 |

---

## 9. iOS 対応方針（後続フェーズ）

Android 完了後、iOS は 2 段階で対応:

1. **暫定フォールバック**: `MKMapView` をラップする stub ハンドラ。クラスタリングなし、件数制限で対処
2. **恒久対応**: iOS 11+ の `MKClusterAnnotation` API を利用（OS ネイティブクラスタリング）

---

## 10. テスト計画

### ユニットテスト (`NotifyDispatchApp.Tests`)

| テスト対象 | テストケース |
|---|---|
| `GeoClusterer.Cluster()` | 空リスト、全件別セル、全件同セル、混合、ズームイン展開 |
| `ClusterItem` | プロパティ値、重心計算、DominantCategory 判定 |

### 手動テスト (Android 実機)

| シナリオ | 確認項目 |
|---|---|
| 一括DL後にマップ表示 | クラッシュしないこと |
| ズームイン | クラスタが個別ピンに分解されること |
| ズームアウト | 個別ピンがクラスタに集約されること |
| クラスタタップ | そのクラスタの範囲にズームインすること |
| 個別ピンタップ | InfoWindow が表示されること |
| InfoWindow タップ | 詳細アラートが表示されること |
| レイヤー切替 | チップ ON/OFF でカテゴリ別にフィルタされること |
| 期間切替 | 期間フィルタが反映されること |
| 画面回転 | クラッシュしないこと、マーカーが維持されること |
| バックグラウンド → 復帰 | MapView ライフサイクルが正常に動作すること |
