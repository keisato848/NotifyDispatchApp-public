using System.Collections;
using NotifyDispatchApp.Models;

namespace NotifyDispatchApp.Controls;

/// <summary>
/// クラスタリング対応のカスタムマップ View です。
/// Android ではネイティブ GoogleMap ハンドラで描画し、その他プラットフォームではフォールバックします。
/// </summary>
public class ClusterMap : View
{
    /// <summary>
    /// マップに表示するアイテムソースの BindableProperty です。
    /// </summary>
    public static readonly BindableProperty ItemsSourceProperty =
        BindableProperty.Create(
            nameof(ItemsSource),
            typeof(IEnumerable),
            typeof(ClusterMap));

    /// <summary>
    /// マップに表示するアイテムソースです。
    /// </summary>
    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>
    /// 現在地を表示するかどうかの BindableProperty です。
    /// </summary>
    public static readonly BindableProperty IsShowingUserProperty =
        BindableProperty.Create(
            nameof(IsShowingUser),
            typeof(bool),
            typeof(ClusterMap),
            true);

    /// <summary>
    /// 現在地を表示するかどうかです。
    /// </summary>
    public bool IsShowingUser
    {
        get => (bool)GetValue(IsShowingUserProperty);
        set => SetValue(IsShowingUserProperty, value);
    }

    /// <summary>
    /// 地図種別（0=Street, 1=Satellite, 2=Hybrid）の BindableProperty です。
    /// </summary>
    public static readonly BindableProperty MapTypeValueProperty =
        BindableProperty.Create(
            nameof(MapTypeValue),
            typeof(int),
            typeof(ClusterMap),
            0);

    /// <summary>
    /// 地図種別です。0=Street, 1=Satellite, 2=Hybrid を表します。
    /// </summary>
    public int MapTypeValue
    {
        get => (int)GetValue(MapTypeValueProperty);
        set => SetValue(MapTypeValueProperty, value);
    }

    /// <summary>
    /// カテゴリ別スタイル辞書の BindableProperty です。
    /// キーはカテゴリ名、値は (FillColor, StrokeColor) のタプルです。
    /// </summary>
    public static readonly BindableProperty CategoryStylesMapProperty =
        BindableProperty.Create(
            nameof(CategoryStylesMap),
            typeof(Dictionary<string, CategoryStyle>),
            typeof(ClusterMap));

    /// <summary>
    /// カテゴリ別スタイル辞書です。
    /// </summary>
    public Dictionary<string, CategoryStyle>? CategoryStylesMap
    {
        get => (Dictionary<string, CategoryStyle>?)GetValue(CategoryStylesMapProperty);
        set => SetValue(CategoryStylesMapProperty, value);
    }

    /// <summary>
    /// 選択中の目撃情報の BindableProperty です。
    /// </summary>
    public static readonly BindableProperty SelectedItemProperty =
        BindableProperty.Create(
            nameof(SelectedItem),
            typeof(BearSighting),
            typeof(ClusterMap),
            defaultBindingMode: BindingMode.TwoWay);

    /// <summary>
    /// 選択中の目撃情報です。InfoWindow タップ時にハンドラ側から設定されます。
    /// </summary>
    public BearSighting? SelectedItem
    {
        get => (BearSighting?)GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    /// <summary>
    /// マップの表示範囲が変更されたときに発生するイベントです。
    /// </summary>
    public event EventHandler<MapBoundsChangedEventArgs>? MapBoundsChanged;

    /// <summary>
    /// 個別ピンの InfoWindow がタップされたときに発生するイベントです。
    /// </summary>
    public event EventHandler<ClusterMapItemSelectedEventArgs>? ItemSelected;

    /// <summary>
    /// ズームで分離できないクラスタがタップされたときに発生するイベントです。
    /// </summary>
    public event EventHandler<ClusterTappedEventArgs>? ClusterTapped;

    /// <summary>
    /// マップの表示範囲変更をハンドラから通知します。
    /// </summary>
    /// <param name="bounds">新しい表示範囲です。</param>
    public void RaiseMapBoundsChanged(MapBounds bounds)
    {
        MapBoundsChanged?.Invoke(this, new MapBoundsChangedEventArgs(bounds));
    }

    /// <summary>
    /// アイテム選択をハンドラから通知します。
    /// </summary>
    /// <param name="sighting">選択された目撃情報です。</param>
    public void RaiseItemSelected(BearSighting sighting)
    {
        SelectedItem = sighting;
        ItemSelected?.Invoke(this, new ClusterMapItemSelectedEventArgs(sighting));
    }

    /// <summary>
    /// ズームで分離できないクラスタのタップをハンドラから通知します。
    /// </summary>
    /// <param name="items">クラスタに含まれるアイテムリストです。</param>
    public void RaiseClusterTapped(List<BearSighting> items)
    {
        ClusterTapped?.Invoke(this, new ClusterTappedEventArgs(items));
    }

    /// <summary>
    /// 指定座標と半径でマップ表示領域を移動するリクエストです。
    /// ハンドラ側で処理されます。
    /// </summary>
    public event EventHandler<MoveToRegionRequestEventArgs>? MoveToRegionRequested;

    /// <summary>
    /// マップの表示領域を指定座標と半径に移動します。
    /// </summary>
    /// <param name="latitude">中心の緯度です。</param>
    /// <param name="longitude">中心の経度です。</param>
    /// <param name="radiusKm">表示半径（キロメートル）です。</param>
    public void MoveToRegion(double latitude, double longitude, double radiusKm)
    {
        MoveToRegionRequested?.Invoke(this, new MoveToRegionRequestEventArgs(latitude, longitude, radiusKm));
    }
}

/// <summary>
/// カテゴリ別のマーカースタイル定義です。
/// </summary>
/// <param name="FillColorHex">塗りつぶし色の Hex 文字列です。</param>
/// <param name="StrokeColorHex">枠線色の Hex 文字列です。</param>
public record CategoryStyle(string FillColorHex, string StrokeColorHex);

/// <summary>
/// MapBoundsChanged イベントの引数です。
/// </summary>
/// <param name="Bounds">新しい表示範囲です。</param>
public class MapBoundsChangedEventArgs(MapBounds Bounds) : EventArgs
{
    /// <summary>
    /// 新しい表示範囲です。
    /// </summary>
    public MapBounds Bounds { get; } = Bounds;
}

/// <summary>
/// ItemSelected イベントの引数です。
/// </summary>
/// <param name="Sighting">選択された目撃情報です。</param>
public class ClusterMapItemSelectedEventArgs(BearSighting Sighting) : EventArgs
{
    /// <summary>
    /// 選択された目撃情報です。
    /// </summary>
    public BearSighting Sighting { get; } = Sighting;
}

/// <summary>
/// MoveToRegion リクエストの引数です。
/// </summary>
/// <param name="Latitude">中心の緯度です。</param>
/// <param name="Longitude">中心の経度です。</param>
/// <param name="RadiusKm">表示半径（キロメートル）です。</param>
public class MoveToRegionRequestEventArgs(double Latitude, double Longitude, double RadiusKm) : EventArgs
{
    /// <summary>
    /// 中心の緯度です。
    /// </summary>
    public double Latitude { get; } = Latitude;

    /// <summary>
    /// 中心の経度です。
    /// </summary>
    public double Longitude { get; } = Longitude;

    /// <summary>
    /// 表示半径（キロメートル）です。
    /// </summary>
    public double RadiusKm { get; } = RadiusKm;
}

/// <summary>
/// ClusterTapped イベントの引数です。
/// </summary>
/// <param name="Items">クラスタに含まれる目撃情報リストです。</param>
public class ClusterTappedEventArgs(List<BearSighting> Items) : EventArgs
{
    /// <summary>
    /// クラスタに含まれる目撃情報リストです。
    /// </summary>
    public List<BearSighting> Items { get; } = Items;
}
