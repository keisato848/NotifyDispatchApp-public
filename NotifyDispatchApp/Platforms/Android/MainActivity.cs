using Android.App;
using Android.Content.PM;
using Android.OS;

namespace NotifyDispatchApp;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    /// <summary>
    /// Activityの生成時に呼び出されます。
    /// </summary>
    /// <param name="savedInstanceState">保存されたインスタンス状態です。</param>
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // Android Force Dark を無効化し、ダークモードでの色反転を防止
        if (OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            var decorView = Window?.DecorView;
            if (decorView != null)
            {
                decorView.ForceDarkAllowed = false;
            }
        }
    }

    /// <summary>
    /// ステータスバーとナビゲーションバーの色を設定します。
    /// </summary>
    private void SetColors()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(21))
        {
            var color = Android.Graphics.Color.Transparent;
#pragma warning disable CA1422 // Validate platform compatibility
            Window?.SetStatusBarColor(color);
            Window?.SetNavigationBarColor(color);
#pragma warning restore CA1422 // Validate platform compatibility
        }
    }
}
