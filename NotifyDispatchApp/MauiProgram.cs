using Microsoft.Extensions.Configuration;
using Shiny;
using NotifyDispatchApp.Controls;
using NotifyDispatchApp.Services;
#if ANDROID
using NotifyDispatchApp.Platforms.Android.Handlers;
#elif IOS
using NotifyDispatchApp.Platforms.iOS.Handlers;
#endif

namespace NotifyDispatchApp;

/// <summary>
/// MAUI アプリケーションのエントリーポイントです。
/// </summary>
public static class MauiProgram
{
    /// <summary>
    /// MauiApp を構成して生成します。
    /// </summary>
    /// <returns>構成済みの MauiApp インスタンスです。</returns>
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseShiny()
            ;

        builder
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            })
            .UseMauiMaps();

        builder.ConfigureMauiHandlers(handlers =>
        {
#if ANDROID
            handlers.AddHandler<ClusterMap, ClusterMapHandler>();
#elif IOS
            handlers.AddHandler<ClusterMap, ClusterMapHandler>();
#endif
        });

#if IOS
          builder.Configuration.AddJsonPlatformBundle(optional: false);
#endif

        // --- appsettings.json を明示的に読み込み ---
        try
        {
            using var appSettingsStream = FileSystem.OpenAppPackageFileAsync("appsettings.json")
                .GetAwaiter().GetResult();
            using var appReader = new System.IO.StreamReader(appSettingsStream);
            var appJson = System.Text.Json.JsonDocument.Parse(appReader.ReadToEnd());
            foreach (var section in appJson.RootElement.EnumerateObject())
            {
                if (section.Value.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    foreach (var prop in section.Value.EnumerateObject())
                    {
                        var value = prop.Value.ValueKind == System.Text.Json.JsonValueKind.String
                            ? prop.Value.GetString()
                            : prop.Value.ToString();
                        builder.Configuration[$"{section.Name}:{prop.Name}"] = value;
                    }
                }
            }
        }
        catch
        {
            // appsettings.json が見つからない場合はデフォルト値を使用
        }

        // --- appsettings.secrets.json で上書き（存在する場合のみ） ---
        try
        {
            using var secretsStream = FileSystem.OpenAppPackageFileAsync("appsettings.secrets.json")
                .GetAwaiter().GetResult();
            using var reader = new System.IO.StreamReader(secretsStream);
            var secretsJson = System.Text.Json.JsonDocument.Parse(reader.ReadToEnd());
            foreach (var section in secretsJson.RootElement.EnumerateObject())
            {
                if (section.Value.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    foreach (var prop in section.Value.EnumerateObject())
                    {
                        var value = prop.Value.ValueKind == System.Text.Json.JsonValueKind.String
                            ? prop.Value.GetString()
                            : prop.Value.ToString();
                        builder.Configuration[$"{section.Name}:{prop.Name}"] = value;
                    }
                }
            }
        }
        catch
        {
            // secrets ファイルが存在しない場合は無視
        }

        // --- App Settings ---
        var apiSection = builder.Configuration.GetSection("Api");
        var settingsSection = builder.Configuration.GetSection("Settings");
        var appSettings = new AppSettings
        {
            BaseUrl = apiSection["BaseUrl"] ?? "",
            FunctionKey = apiSection["FunctionKey"] ?? "",
            DispatchUrl = apiSection["DispatchUrl"] ?? "",
            DispatchApiKey = apiSection["DispatchApiKey"] ?? "",
            DefaultRegion = settingsSection["DefaultRegion"] ?? "富山市",
            DefaultPrefecture = settingsSection["DefaultPrefecture"] ?? "富山県",
            FetchCount = int.TryParse(settingsSection["FetchCount"], out var fc) ? fc : 50,
            AutoRefreshSeconds = int.TryParse(settingsSection["AutoRefreshSeconds"], out var ar) ? ar : 30,
            DispatchDisplayCount = Preferences.Default.Get("DispatchDisplayCount", int.TryParse(settingsSection["FetchCount"], out var dfc) ? dfc : 50),
            BearDisplayCount = Preferences.Default.Get("BearDisplayCount", int.TryParse(settingsSection["FetchCount"], out var bfc) ? bfc : 50),
        };
        builder.Services.AddSingleton(appSettings);

        System.Diagnostics.Debug.WriteLine($"[AppSettings] BaseUrl={appSettings.BaseUrl}");
        System.Diagnostics.Debug.WriteLine($"[AppSettings] FunctionKey={(string.IsNullOrEmpty(appSettings.FunctionKey) ? "(empty)" : appSettings.FunctionKey[..8] + "...")}");
        System.Diagnostics.Debug.WriteLine($"[AppSettings] FetchCount={appSettings.FetchCount}");

        // --- HttpClient with timeout ---
        // 各リクエストの実際のタイムアウトは CancellationTokenSource で制御する。
        // HttpClient.Timeout は一括DL時の延長タイムアウト(30秒)より大きい値にする。
        builder.Services.AddHttpClient("DispatchApi", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(35);
        });
        builder.Services.AddHttpClient("BearApi", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(35);
        });
        builder.Services.AddHttpClient("GeoApi", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        // --- Services ---
        builder.Services.AddSingleton<IConnectivityService, MauiConnectivityService>();
        builder.Services.AddSingleton<IDispatchService, DispatchService>();
        builder.Services.AddSingleton<IBearSightingService, BearSightingService>();
        builder.Services.AddSingleton<ILocalCacheService, LocalCacheService>();

        // --- Pages & ViewModels ---
        builder.Services.AddSingleton<InfoListPage>();
        builder.Services.AddSingleton<InfoListPageViewModel>();
        builder.Services.AddTransient<MapPage>();
        builder.Services.AddTransient<BearInfoPage>();
        builder.Services.AddTransient<BearInfoPageViewModel>();
        builder.Services.AddTransient<SafetyInfoPage>();
        builder.Services.AddTransient<SettingsPage>();

#if IOS
            builder.Services.AddPush<MyPushDelegate>();

            var cfg = builder.Configuration.GetSection("AzureNotificationHubs");
            builder.Services.AddPushAzureNotificationHubs<MyPushDelegate>(
                cfg["ListenerConnectionString"]!,
                cfg["HubName"]!
            );
#endif
        return builder.Build();
    }
}
