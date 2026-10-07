using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using ClassIsland.Management.Client;

namespace ClassIsland.Management.Android;

[Application]
public sealed class ManagementApplication : AvaloniaAndroidApplication<App>
{
    public ManagementApplication(IntPtr javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
    {
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) => base.CustomizeAppBuilder(builder).LogToTrace();
}
