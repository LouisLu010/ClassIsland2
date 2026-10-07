using Android.App;
using Android.Content.PM;
using Avalonia.Android;

namespace ClassIsland.Management.Android;

[Activity(Label = "ClassIsland 集控", Theme = "@style/ManagementTheme", MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public sealed class MainActivity : AvaloniaMainActivity
{
}
