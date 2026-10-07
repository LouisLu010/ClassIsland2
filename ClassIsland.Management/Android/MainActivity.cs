using Android.App;
using Android.Content.PM;
using Avalonia;
using Avalonia.Android;
using ClassIsland.Management.Client;

namespace ClassIsland.Management.Android;

[Activity(Label = "ClassIsland 集控", Theme = "@style/ManagementTheme", MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public sealed class MainActivity : AvaloniaMainActivity<App>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) => base.CustomizeAppBuilder(builder).LogToTrace();
}
