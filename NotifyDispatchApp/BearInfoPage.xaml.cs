using Microsoft.Maui.Controls.Shapes;
using NotifyDispatchApp.Controls;
using NotifyDispatchApp.Models;
using SensorLocation = Microsoft.Maui.Devices.Sensors.Location;

namespace NotifyDispatchApp;

/// <summary>
/// 熊出没情報をマップ上に表示するページです。
/// </summary>
public partial class BearInfoPage : ContentPage
{
    private readonly BearInfoPageViewModel _viewModel;

    /// <summary>
    /// デフォルトの富山県中心座標です。位置情報が取得できない場合のフォールバックに使用します。
    /// </summary>
    private static readonly SensorLocation ToyamaCenter = new(36.6953, 137.2113);

    /// <summary>
    /// マップ初期表示の半径（キロメートル）です。直径1kmの範囲を表示します。
    /// </summary>
    private const double InitialRadiusKm = 0.5;

    /// <summary>
    /// カテゴリ別マーカースタイルです。ClusterMap の CategoryStylesMap に設定します。
    /// </summary>
    private static readonly Dictionary<string, CategoryStyle> CategoryStylesForMap = new()
    {
        ["sighting"] = new("#44E53935", "#E53935"),
        ["trace"]    = new("#44FB8C00", "#FB8C00"),
        ["damage"]   = new("#448E24AA", "#8E24AA"),
    };

    /// <summary>
    /// チップのON/OFF外観定義（StrokeColor, BackgroundColor, TextColor, Opacity）です。
    /// </summary>
    private static readonly Dictionary<string, ChipStyle> ChipStyles = new()
    {
        ["sighting"] = new("#E53935", "#FFEBEE"),
        ["trace"]    = new("#FB8C00", "#FFF3E0"),
        ["damage"]   = new("#8E24AA", "#F3E5F5"),
    };

    /// <summary>
    /// 期間チップのBorderマッピングです。
    /// </summary>
    private readonly Dictionary<PeriodOption, Border> _periodChipMap = [];

    /// <summary>
    /// BearInfoPage の新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="viewModel">ビューモデルです。</param>
    public BearInfoPage(BearInfoPageViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;

        _viewModel.VisibleSightingsUpdated += () =>
        {
            UpdateChipAppearance();
        };

        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BearInfoPageViewModel.SelectedPeriod))
                UpdatePeriodChipAppearance();
        };

        _viewModel.Layers.CollectionChanged += (_, _) => UpdateChipAppearance();

        BuildPeriodChips();
    }

    /// <summary>
    /// ページ表示時に現在地を取得してマップを初期表示し、データを読み込みます。
    /// </summary>
    protected override async void OnAppearing()
    {
        base.OnAppearing();

        bearMap.CategoryStylesMap = CategoryStylesForMap;

        var center = await GetCurrentLocationAsync() ?? ToyamaCenter;
        bearMap.MoveToRegion(center.Latitude, center.Longitude, InitialRadiusKm);
        bearMap.MapBoundsChanged += OnMapBoundsChanged;
        bearMap.ItemSelected += OnItemSelected;

        // 設定画面での一括DLを即反映するため、毎回読み込む
        await _viewModel.LoadDataAsync();
    }

    /// <summary>
    /// 端末の現在地を取得します。取得できない場合は null を返します。
    /// </summary>
    /// <returns>現在地の Location です。取得失敗時は null です。</returns>
    private static async Task<SensorLocation?> GetCurrentLocationAsync()
    {
        try
        {
            var request = new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(5));
            var location = await Geolocation.Default.GetLocationAsync(request);
            return location is not null
                ? new SensorLocation(location.Latitude, location.Longitude)
                : null;
        }
        catch
        {
            // 権限拒否・位置情報無効などの場合はフォールバック
            return null;
        }
    }

    /// <summary>
    /// ページ非表示時にイベントを解除します。
    /// </summary>
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        bearMap.MapBoundsChanged -= OnMapBoundsChanged;
        bearMap.ItemSelected -= OnItemSelected;
    }

    /// <summary>
    /// ClusterMap の表示範囲変更を検知して ViewModel の範囲フィルタを更新します。
    /// </summary>
    private void OnMapBoundsChanged(object? sender, MapBoundsChangedEventArgs e)
    {
        _viewModel.UpdateMapBounds(e.Bounds);
    }

    /// <summary>
    /// ClusterMap のアイテム選択イベントから目撃情報の詳細を表示します。
    /// </summary>
    private void OnItemSelected(object? sender, ClusterMapItemSelectedEventArgs e)
    {
        ShowSightingDetail(e.Sighting);
    }

    /// <summary>
    /// 期間選択チップを動的に生成します。
    /// </summary>
    private void BuildPeriodChips()
    {
        foreach (var option in BearInfoPageViewModel.PeriodOptions)
        {
            var label = new Label
            {
                Text = option.Label,
                FontSize = 11,
                FontAttributes = FontAttributes.Bold,
                VerticalOptions = LayoutOptions.Center,
            };

            var chip = new Border
            {
                StrokeShape = new RoundRectangle { CornerRadius = 16 },
                Padding = new Thickness(12, 6),
                Content = label,
            };

            var tap = new TapGestureRecognizer();
            tap.Command = _viewModel.SelectPeriodCommand;
            tap.CommandParameter = option;
            chip.GestureRecognizers.Add(tap);

            _periodChipMap[option] = chip;
            periodChipsContainer.Add(chip);
        }

        UpdatePeriodChipAppearance();
    }

    /// <summary>
    /// 期間チップの選択状態を外観に反映します。
    /// </summary>
    private void UpdatePeriodChipAppearance()
    {
        foreach (var (option, chip) in _periodChipMap)
        {
            var isSelected = option == _viewModel.SelectedPeriod;
            if (isSelected)
            {
                chip.BackgroundColor = Color.FromArgb("#1B2838");
                chip.Stroke = Color.FromArgb("#1B2838");
                chip.StrokeThickness = 1.5;
                ((Label)chip.Content!).TextColor = Colors.White;
            }
            else
            {
                chip.BackgroundColor = Color.FromArgb("#F0F2F5");
                chip.Stroke = Color.FromArgb("#CBD2D9");
                chip.StrokeThickness = 1;
                ((Label)chip.Content!).TextColor = Color.FromArgb("#616E7C");
            }
        }
    }

    /// <summary>
    /// 目撃情報の詳細をアラートで表示します。
    /// </summary>
    /// <param name="sighting">表示する目撃情報です。</param>
    private async void ShowSightingDetail(BearSighting sighting)
    {
        var recent = sighting.IsRecent ? " 🔴 直近" : "";
        await DisplayAlert(
            $"{sighting.Category}{recent}",
            $"📍 {sighting.Location}\n" +
            $"🕐 {sighting.Date}\n" +
            $"📝 {sighting.Description}",
            "閉じる");
    }

    /// <summary>
    /// チップのON/OFF外観定義です。
    /// </summary>
    /// <param name="AccentHex">アクセントカラー（Hex）です。</param>
    /// <param name="BgHex">ON時の背景色（Hex）です。</param>
    private record ChipStyle(string AccentHex, string BgHex);

    /// <summary>
    /// レイヤーの表示状態に応じてチップの外観を更新します。
    /// </summary>
    private void UpdateChipAppearance()
    {
        var chipMap = new Dictionary<string, Border>
        {
            ["sighting"] = chipSighting,
            ["trace"] = chipTrace,
            ["damage"] = chipDamage,
        };

        foreach (var layer in _viewModel.Layers)
        {
            if (!chipMap.TryGetValue(layer.Id, out var chip)) continue;
            if (!ChipStyles.TryGetValue(layer.Id, out var style)) continue;

            if (layer.IsVisible)
            {
                // ON: 鮮明な色 + 太枠線
                chip.Opacity = 1.0;
                chip.StrokeThickness = 2.5;
                chip.Stroke = Color.FromArgb(style.AccentHex);
                chip.BackgroundColor = Color.FromArgb(style.BgHex);
            }
            else
            {
                // OFF: 半透明 + 細枠線 + グレー背景
                chip.Opacity = 0.4;
                chip.StrokeThickness = 1;
                chip.Stroke = Colors.Gray;
                chip.BackgroundColor = Color.FromArgb("#F0F0F0");
            }
        }

        UpdatePeriodChipAppearance();
    }
}
