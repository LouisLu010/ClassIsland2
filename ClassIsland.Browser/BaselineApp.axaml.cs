using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using FluentAvalonia.UI.Controls;

namespace ClassIsland.Browser;

/// <summary>
/// 基线模式使用的最小应用：只有 Avalonia + FluentAvalonia，不接 ClassIsland 的任何代码。
/// 用于区分「平台/主题本身跑不起来」和「ClassIsland 的启动流程跑不起来」。
/// 访问 index.html?arg=--baseline 进入。
/// </summary>
public partial class BaselineApp : Application
{
    /// <summary>
    /// 是否在初始化完成后尝试创建并显示一个原生 <see cref="Window"/>，
    /// 用于验证 <c>BrowserWindowingPlatform.CreateWindow</c> 在浏览器端是否真的可用。
    /// </summary>
    public static bool RunWindowTest { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        Console.WriteLine("[BASELINE] OnFrameworkInitializationCompleted");

        if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            singleView.MainView = BuildView();
            Console.WriteLine("[BASELINE] MainView assigned");
        }

        base.OnFrameworkInitializationCompleted();

        if (RunWindowTest)
        {
            TryShowNativeWindow();
        }
    }

    private static void TryShowNativeWindow()
    {
        try
        {
            Console.WriteLine("[WINDOWTEST] creating Window…");
            var w = new Window
            {
                Title = "window test",
                Width = 420,
                Height = 200,
                Content = new TextBlock { Text = "原生 Window 在浏览器端可用" }
            };
            Console.WriteLine("[WINDOWTEST] Window constructed, calling Show()…");
            w.Show();
            Console.WriteLine($"[WINDOWTEST] OK: Show() 成功，PlatformImpl={w.PlatformImpl?.GetType().FullName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WINDOWTEST] FAILED: {ex.GetType().FullName}: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }

    private static Control BuildView() => new StackPanel
    {
        Margin = new Thickness(24),
        Spacing = 12,
        HorizontalAlignment = HorizontalAlignment.Center,
        Children =
        {
            new TextBlock { Text = "基线模式：Avalonia + FluentAvalonia", FontSize = 20 },
            new TextBlock { Text = "中文渲染测试 —— 若显示为方块说明缺字体。" },
            new FAInfoBar
            {
                Title = "FluentAvalonia",
                Message = "此控件渲染正常即说明主题在 WASM 下可用。",
                IsOpen = true,
                Severity = FAInfoBarSeverity.Success,
                IsClosable = false
            },
            new Button { Content = "按钮" },
            new ProgressBar { IsIndeterminate = true }
        }
    };
}
