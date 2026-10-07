using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Browser;
using ClassIsland.Management.Client;

[assembly: SupportedOSPlatform("browser")]

using var location = JSHost.GlobalThis.GetPropertyAsJSObject("location");
App.DefaultServerUrl = location?.GetPropertyAsString("origin") ?? "https://";
await AppBuilder.Configure<App>().StartBrowserAppAsync("out");
