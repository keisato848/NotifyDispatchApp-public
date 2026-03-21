using DeviceRunners.VisualRunners;

namespace NotifyDispatchApp.DeviceTests;

/// <summary>
/// DeviceTestsアプリのエントリーポイント。xUnit ビジュアルテストランナーを構成します。
/// </summary>
public static class MauiProgram
{
    /// <summary>
    /// テストランナーを含む MauiApp を生成します。
    /// </summary>
    /// <returns>構成済みの MauiApp インスタンスです。</returns>
    public static MauiApp CreateMauiApp()
    {
        return MauiApp.CreateBuilder()
            .UseVisualTestRunner(conf => conf
                .AddTestAssembly(typeof(MauiProgram).Assembly))
            .Build();
    }
}
