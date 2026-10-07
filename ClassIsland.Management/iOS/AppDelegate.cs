using Avalonia;
using Avalonia.iOS;
using ClassIsland.Management.Client;
using Foundation;
using UIKit;

namespace ClassIsland.Management.iOS;

[Register("AppDelegate")]
public sealed class AppDelegate : AvaloniaAppDelegate<App>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) => base.CustomizeAppBuilder(builder).LogToTrace();
}

public static class Program
{
    public static void Main(string[] args) => UIApplication.Main(args, null, typeof(AppDelegate));
}
