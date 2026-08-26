using System.Threading.Tasks;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Abstractions.Services.UI;
using ClassIsland.Core.Enums.UI;
using ClassIsland.Shared;

namespace ClassIsland.Views;

/// <summary>
/// 浏览器端的启动界面提供方。
/// </summary>
/// <remarks>
/// 桌面端实现 <see cref="SplashWindow"/> 派生自 <c>SplashWindowBase</c>（一个
/// <see cref="Avalonia.Controls.Window"/>），浏览器端无法构造。这里改用同样内容的
/// <see cref="SplashView"/>（一个 <c>ViewBase</c>），显示到单视图宿主里。
/// </remarks>
public class BrowserSplashProvider : ISplashProvider
{
    private SplashView? _view;

    public Task StartSplash()
    {
        // 与 Android 端一致，直接构造（SplashView 未注册到 DI）。
        _view ??= new SplashView();
        _view.Show();
        return Task.CompletedTask;
    }

    public Task EndSplash()
    {
        _view?.Hide();
        _view = null;
        return Task.CompletedTask;
    }
}
