using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NotifyDispatchApp.Services;
using Xunit;

namespace NotifyDispatchApp.DeviceTests.Tests;

/// <summary>
/// アプリ起動時の DI コンテナ構成をエミュレータ上で検証するテストクラスです。
/// </summary>
public class AppStartupTests
{
    /// <summary>
    /// テスト用の ServiceProvider を構築します。
    /// </summary>
    /// <returns>構成済みの ServiceProvider です。</returns>
    private static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddHttpClient();
        services.AddLogging();
       services.AddSingleton(new AppSettings());
        services.AddSingleton<IConnectivityService, MauiConnectivityService>();
        services.AddSingleton<IDispatchService, DispatchService>();
        services.AddSingleton<InfoListPageViewModel>();
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// IDispatchService が DI コンテナから正しく解決されることを検証します。
    /// </summary>
    [Fact]
    public void DI_Resolves_IDispatchService()
    {
        using var provider = BuildServiceProvider();
        var service = provider.GetService<IDispatchService>();

        Assert.NotNull(service);
        Assert.IsType<DispatchService>(service);
    }

    /// <summary>
    /// IConnectivityService が DI コンテナから正しく解決されることを検証します。
    /// </summary>
    [Fact]
    public void DI_Resolves_IConnectivityService()
    {
        using var provider = BuildServiceProvider();
        var service = provider.GetService<IConnectivityService>();

        Assert.NotNull(service);
        Assert.IsType<MauiConnectivityService>(service);
    }

    /// <summary>
    /// InfoListPageViewModel が DI コンテナから正しく解決されることを検証します。
    /// </summary>
    [Fact]
    public void DI_Resolves_InfoListPageViewModel()
    {
        using var provider = BuildServiceProvider();
        var vm = provider.GetService<InfoListPageViewModel>();

        Assert.NotNull(vm);
    }

    /// <summary>
    /// ConnectivityService が例外を発生させずにブール値を返すことを検証します。
    /// </summary>
    [Fact]
    public void ConnectivityService_Returns_Boolean()
    {
        using var provider = BuildServiceProvider();
        var service = provider.GetRequiredService<IConnectivityService>();

        var result = service.HasInternetAccess();

        Assert.IsType<bool>(result);
    }

    /// <summary>
    /// DispatchService の IsConnected が ConnectivityService と連動することを検証します。
    /// </summary>
    [Fact]
    public void DispatchService_IsConnected_DelegatesToConnectivityService()
    {
        using var provider = BuildServiceProvider();
        var dispatchService = provider.GetRequiredService<IDispatchService>();
        var connectivityService = provider.GetRequiredService<IConnectivityService>();

        Assert.Equal(connectivityService.HasInternetAccess(), dispatchService.IsConnected());
    }
}
