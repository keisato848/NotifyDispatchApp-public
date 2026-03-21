using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Maps;
using NotifyDispatchApp.Models;
using NotifyDispatchApp.Services;
using SensorLocation = Microsoft.Maui.Devices.Sensors.Location;

namespace NotifyDispatchApp;

/// <summary>
/// 出動情報の位置を地図上に表示するページです。
/// </summary>
public partial class MapPage : ContentPage
{
    private readonly IDispatchService _dispatchService;
    private readonly AppSettings _settings;
    private DispatchInfo? _info;

    /// <summary>
    /// MapPage の新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="dispatchService">出動情報サービスです。</param>
    /// <param name="settings">アプリケーション設定です。</param>
    public MapPage(IDispatchService dispatchService, AppSettings settings)
    {
        InitializeComponent();
        _dispatchService = dispatchService;
        _settings = settings;
    }

    /// <summary>
    /// 表示する出動情報を設定し、地図の読み込みを開始します。
    /// </summary>
    /// <param name="info">表示する出動情報です。</param>
    public void SetInfo(DispatchInfo info)
    {
        _info = info;
        UpdateInfoCard();
        _ = LoadLocationAsync();
    }

    /// <summary>
    /// 情報カードの表示を更新します。
    /// </summary>
    private void UpdateInfoCard()
    {
        if (_info == null) return;

        lblPlace.Text = _info.Place ?? "";
        lblReason.Text = _info.Reason ?? "";
        lblDateTime.Text = $"🕐 {_info.StrDateTime}";
        lblRegion.Text = $"📍 {_info.Region}";
        statusBar.Color = _info.IsCompleted
            ? Color.FromArgb("#2ECC71")
            : Color.FromArgb("#FF6B6B");
        infoCard.IsVisible = true;
    }

    /// <summary>
    /// Geo API から位置情報を取得し、地図を移動します。
    /// </summary>
    private async Task LoadLocationAsync()
    {
        if (_info == null) return;

        try
        {
            // Place から市名を抽出、失敗時は Region をフォールバック
            var cityName = ExtractCityName(_info.Place) ?? ExtractCityName(_info.Region);
            if (string.IsNullOrEmpty(cityName))
            {
                ShowError("市名を特定できませんでした。");
                return;
            }

            var geoResponse = await _dispatchService.GetTownInfoAsync(_settings.DefaultPrefecture, cityName);

            if (geoResponse?.ApiResponse?.Location is { } locations && locations.Length > 0)
            {
                // Place に含まれる町域名と一致する位置を探す
                var matched = locations.FirstOrDefault(loc =>
                    !string.IsNullOrEmpty(_info.Place) &&
                    !string.IsNullOrEmpty(loc.Town) &&
                    _info.Place.Contains(loc.Town));

                var target = matched ?? locations[0];

                var loc2 = new SensorLocation(target.Y, target.X);
                var mapSpan = new MapSpan(loc2, 0.01, 0.01);

                map.MoveToRegion(mapSpan);
                map.Pins.Clear();
                map.Pins.Add(new Pin
                {
                    Label = _info.Place ?? "現場",
                    Address = _info.Reason ?? "",
                    Location = loc2,
                    Type = PinType.Place
                });
            }
            else
            {
                ShowDefaultLocation();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"MapPage Error: {ex}");
            ShowDefaultLocation();
        }
    }

    /// <summary>
    /// Place 文字列から市名を抽出します。
    /// </summary>
    /// <param name="place">場所文字列です。</param>
    /// <returns>抽出された市名です。</returns>
    private static string? ExtractCityName(string? place)
    {
        if (string.IsNullOrEmpty(place)) return null;

        // "富山市桜町1-1" → "富山市", "立山町芦峅寺" → "立山町"
        var suffixes = new[] { "市", "町", "村" };
        foreach (var suffix in suffixes)
        {
            var idx = place.IndexOf(suffix, StringComparison.Ordinal);
            if (idx > 0) return place[..(idx + suffix.Length)];
        }
        return null;
    }

    /// <summary>
    /// デフォルト位置（富山市中心部）を表示します。
    /// </summary>
    private void ShowDefaultLocation()
    {
        var defaultLoc = new SensorLocation(36.6952907, 137.2113383);
        map.MoveToRegion(new MapSpan(defaultLoc, 0.05, 0.05));
    }

    /// <summary>
    /// エラーオーバーレイを表示します。
    /// </summary>
    /// <param name="message">エラーメッセージです。</param>
    private void ShowError(string message)
    {
        errorLabel.Text = message;
        errorOverlay.IsVisible = true;
    }

    /// <summary>
    /// 戻るボタンのクリックイベントハンドラーです。
    /// </summary>
    private async void OnBackClicked(object? sender, EventArgs e)
    {
        await Navigation.PopAsync();
    }
}
