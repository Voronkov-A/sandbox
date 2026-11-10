using Android.App;
using Android.Content.PM;
using Avalonia;
using Avalonia.Android;
using Picshare.Services;

namespace Picshare.Android;

[Activity(
    Label = "Picshare.Android",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity<App>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        LongRunningOperationHost.Current = new AndroidLongRunningOperationHost(this);
        DiagnosticExportHost.Current = new AndroidDiagnosticShareHost(this);
        return base.CustomizeAppBuilder(builder)
            .WithInterFont();
    }
}
