using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NotifyDispatchApp.Services;
using Xunit;

namespace NotifyDispatchApp.DeviceTests.Tests;

/// <summary>
/// ViewModel の初期データ読み込みをエミュレータ上で検証するテストクラスです。
/// </summary>
public class ViewModelInitialLoadTests
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
    /// ViewModel の ListDispatchInfo が初期状態で空であることを検証します。
    /// </summary>
    [Fact]
    public void ViewModel_ListDispatchInfo_InitiallyEmpty()
    {
        using var provider = BuildServiceProvider();
        var vm = provider.GetRequiredService<InfoListPageViewModel>();

        Assert.Empty(vm.ListDispatchInfo);
    }

    /// <summary>
    /// ViewModel の IsBusy が初期状態で false であることを検証します。
    /// </summary>
    [Fact]
    public void ViewModel_IsBusy_InitiallyFalse()
    {
        using var provider = BuildServiceProvider();
        var vm = provider.GetRequiredService<InfoListPageViewModel>();

        Assert.False(vm.IsBusy);
    }

    /// <summary>
    /// LoadInfoAsync 実行後に ListDispatchInfo にデータが格納されることを検証します（DEBUG モックデータ）。
    /// </summary>
    [Fact]
    public async Task ViewModel_LoadInfoAsync_PopulatesList()
    {
        using var provider = BuildServiceProvider();
        var vm = provider.GetRequiredService<InfoListPageViewModel>();

        await vm.LoadInfoAsync("富山市");

        // DEBUG ビルドではモックデータが返却される
        Assert.NotEmpty(vm.ListDispatchInfo);
    }

    /// <summary>
    /// LoadInfoAsync 完了後に IsBusy が false に戻ることを検証します。
    /// </summary>
    [Fact]
    public async Task ViewModel_LoadInfoAsync_IsBusyReturnsFalse()
    {
        using var provider = BuildServiceProvider();
        var vm = provider.GetRequiredService<InfoListPageViewModel>();

        await vm.LoadInfoAsync("富山市");

        Assert.False(vm.IsBusy);
    }

    /// <summary>
    /// モックデータに富山市の出動情報が含まれることを検証します。
    /// </summary>
    [Fact]
    public async Task ViewModel_LoadInfoAsync_ContainsToyamaRegion()
    {
        using var provider = BuildServiceProvider();
        var vm = provider.GetRequiredService<InfoListPageViewModel>();

        await vm.LoadInfoAsync("富山市");

        Assert.Contains(vm.ListDispatchInfo, d => d.Region == "富山市");
    }

    /// <summary>
    /// モックデータに出動場所と理由が含まれることを検証します。
    /// </summary>
    [Fact]
    public async Task ViewModel_LoadInfoAsync_ItemsHavePlaceAndReason()
    {
        using var provider = BuildServiceProvider();
        var vm = provider.GetRequiredService<InfoListPageViewModel>();

        await vm.LoadInfoAsync("富山市");

        Assert.All(vm.ListDispatchInfo, info =>
        {
            Assert.False(string.IsNullOrEmpty(info.Place));
            Assert.False(string.IsNullOrEmpty(info.Reason));
        });
    }

    /// <summary>
    /// 2回連続の LoadInfoAsync で前のデータがクリアされることを検証します。
    /// </summary>
    [Fact]
    public async Task ViewModel_LoadInfoAsync_ClearsPreviousData()
    {
        using var provider = BuildServiceProvider();
        var vm = provider.GetRequiredService<InfoListPageViewModel>();

        await vm.LoadInfoAsync("富山市");
        var firstCount = vm.ListDispatchInfo.Count;

        await vm.LoadInfoAsync("富山市");
        var secondCount = vm.ListDispatchInfo.Count;

        Assert.Equal(firstCount, secondCount);
    }
}
