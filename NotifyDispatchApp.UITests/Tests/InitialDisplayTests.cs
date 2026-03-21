using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Android;
using Xunit;

namespace NotifyDispatchApp.UITests.Tests;

/// <summary>
/// アプリ初期表示の UI テストスイートです。Appium 経由でエミュレータ上の実画面を検証します。
/// </summary>
/// <remarks>
/// 前提条件:
/// 1. Appium サーバーが http://127.0.0.1:4723 で起動済み
/// 2. Android エミュレータが起動済み
/// 3. APK がビルド済み（AppiumSetup.cs のパスを確認）
/// </remarks>
[Collection("Appium")]
public class InitialDisplayTests : IClassFixture<AppiumSetup>
{
    private readonly AndroidDriver _driver;

    /// <summary>
    /// テストクラスを初期化します。
    /// </summary>
    /// <param name="setup">Appium ドライバーのフィクスチャです。</param>
    public InitialDisplayTests(AppiumSetup setup)
    {
        _driver = setup.Driver;
    }

    /// <summary>
    /// AutomationId または content-desc/resource-id で要素を検索します。
    /// MAUI の AutomationId は Android で content-desc にマッピングされる場合と
    /// resource-id にマッピングされる場合があるため、両方を試みます。
    /// </summary>
    /// <param name="automationId">検索する AutomationId です。</param>
    /// <returns>見つかった要素です。</returns>
    private AppiumElement FindByAutomationId(string automationId)
    {
        try
        {
            return _driver.FindElement(MobileBy.AccessibilityId(automationId));
        }
        catch (NoSuchElementException)
        {
            // content-desc で見つからない場合、XPath の content-desc 属性で検索
            try
            {
                return _driver.FindElement(By.XPath($"//*[@content-desc='{automationId}']"));
            }
            catch (NoSuchElementException)
            {
                // resource-id で検索（MAUI のバージョンによってはこちら）
                return _driver.FindElement(By.XPath($"//*[contains(@resource-id, '{automationId}')]"));
            }
        }
    }

    /// <summary>
    /// アプリが正常に起動し、セッションが確立されることを検証します。
    /// </summary>
    [Fact]
    public void App_Launches_Successfully()
    {
        Assert.NotNull(_driver);
        Assert.NotNull(_driver.SessionId);
    }

    /// <summary>
    /// アプリのタイトル「消防出動情報」が画面に表示されていることを検証します。
    /// </summary>
    [Fact]
    public void AppTitle_IsDisplayed()
    {
        // テキストで直接検索（AutomationId に依存しない）
        var title = _driver.FindElement(By.XPath("//*[@text='消防出動情報']"));
        Assert.NotNull(title);
        Assert.True(title.Displayed);
    }

    /// <summary>
    /// InfoListPage が表示されていることを検証します。
    /// </summary>
    [Fact]
    public void InfoListPage_IsDisplayed()
    {
        var page = FindByAutomationId("InfoListPageView");
        Assert.NotNull(page);
    }

    /// <summary>
    /// 地域セレクターが表示されていることを検証します。
    /// </summary>
    [Fact]
    public void RegionSelector_IsVisible()
    {
        var regionList = FindByAutomationId("RegionSelector");
        Assert.NotNull(regionList);
        Assert.True(regionList.Displayed);
    }

    /// <summary>
    /// 出動情報リストが表示されていることを検証します。
    /// </summary>
    [Fact]
    public void DispatchInfoList_IsVisible()
    {
        var dispatchList = FindByAutomationId("DispatchInfoList");
        Assert.NotNull(dispatchList);
        Assert.True(dispatchList.Displayed);
    }

    /// <summary>
    /// 件数ラベルが表示されていることを検証します。
    /// </summary>
    [Fact]
    public void CountLabel_IsVisible()
    {
        var countLabel = FindByAutomationId("CountLabel");
        Assert.NotNull(countLabel);
        Assert.True(countLabel.Displayed);
    }

    /// <summary>
    /// データ読み込み完了後にローディングインジケーターが非表示になることを検証します。
    /// </summary>
    [Fact]
    public void LoadingIndicator_IsNotVisible_AfterLoad()
    {
        Thread.Sleep(3000);

        try
        {
            var indicator = FindByAutomationId("LoadingIndicator");
            Assert.False(indicator.Displayed);
        }
        catch (NoSuchElementException)
        {
            // 要素が見つからない＝非表示（期待通り）
            Assert.True(true);
        }
    }

    /// <summary>
    /// 画面にモックデータの場所テキストが表示されていることを検証します（DEBUG ビルド時）。
    /// </summary>
    [Fact]
    public void MockData_PlaceText_IsVisible()
    {
        Thread.Sleep(3000);

        // モックデータの場所テキストで検索
        var element = _driver.FindElement(By.XPath("//*[contains(@text, 'TEST')]"));
        Assert.NotNull(element);
        Assert.True(element.Displayed);
    }

    /// <summary>
    /// 件数ラベルに「件数:」テキストが含まれることを検証します。
    /// </summary>
    [Fact]
    public void CountLabel_ShowsCount()
    {
        Thread.Sleep(3000);

        // テキストで直接検索
        var element = _driver.FindElement(By.XPath("//*[contains(@text, '件数:')]"));
        Assert.NotNull(element);
        Assert.Contains("件数:", element.Text);
    }
}
