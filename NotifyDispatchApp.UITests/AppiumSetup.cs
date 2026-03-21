using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Android;

namespace NotifyDispatchApp.UITests;

/// <summary>
/// Appium ドライバーの初期化・終了を管理する共有フィクスチャです。
/// </summary>
public class AppiumSetup : IDisposable
{
    private AndroidDriver? _driver;

    /// <summary>
    /// 初期化済みの AndroidDriver を返します。未初期化の場合は自動的に初期化します。
    /// </summary>
    public AndroidDriver Driver => _driver ??= CreateDriver();

    /// <summary>
    /// AndroidDriver を生成します。
    /// </summary>
    /// <returns>構成済みの AndroidDriver インスタンスです。</returns>
    private static AndroidDriver CreateDriver()
    {
        var options = new AppiumOptions
        {
            PlatformName = "Android",
            AutomationName = "UiAutomator2",
            // ビルド済み APK のパス（環境に合わせて変更してください）
            App = @"C:\temp\bin\Debug\net10.0-android\com.companyname.notifydispatchapp-Signed.apk"
        };
        options.AddAdditionalAppiumOption("noReset", true);
        options.AddAdditionalAppiumOption("newCommandTimeout", 300);

        var driver = new AndroidDriver(new Uri("http://127.0.0.1:4723"), options, TimeSpan.FromMinutes(3));
        driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(10);
        return driver;
    }

    /// <summary>
    /// ドライバーを終了し、リソースを解放します。
    /// </summary>
    public void Dispose()
    {
        _driver?.Quit();
        _driver = null;
        GC.SuppressFinalize(this);
    }
}
