

namespace NotifyDispatchApp;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();

		// アプリ全体をライトテーマに固定（ダークモードでの色反転を防止）
		UserAppTheme = AppTheme.Light;
	}

	/// <summary>
	/// アプリケーションのウィンドウを生成します。
	/// </summary>
	/// <param name="activationState">アクティベーション状態です。</param>
	/// <returns>生成されたウィンドウです。</returns>
	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new AppShell());
	}
}
