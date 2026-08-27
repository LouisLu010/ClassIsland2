using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Browser;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Threading;
using ClassIsland.Browser.Controls.UI;
using ClassIsland.Browser.Services.UI;
using ClassIsland.Core;
using ClassIsland.Core.Abstractions.Services.UI;
using ClassIsland.Core.Helpers;
using ClassIsland.Extensions;

[assembly: SupportedOSPlatform("browser")]

namespace ClassIsland.Browser;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        Console.WriteLine($"[BROWSER-BOOT] Main enter, args=[{string.Join(", ", args)}]");
        Probe();

        try
        {
            // 基线模式：只跑 Avalonia + FluentAvalonia，用于把平台问题和 ClassIsland 启动问题分开。
            // 加 --windowtest 则额外验证浏览器端能否创建原生 Window。
            if (args.Contains("--baseline") || args.Contains("--windowtest"))
            {
                Console.WriteLine("[BROWSER-BOOT] baseline mode");
                BaselineApp.RunWindowTest = args.Contains("--windowtest");
                await AppBuilder.Configure<BaselineApp>()
                    .WithInterFont()
                    .LogToHostSink()
                    .StartBrowserAppAsync("out");
                return;
            }

            var builder = BuildAvaloniaApp(args);
            Console.WriteLine("[BROWSER-BOOT] builder ready, starting browser app");
            await builder.StartBrowserAppAsync("out");
            Console.WriteLine("[BROWSER-BOOT] StartBrowserAppAsync returned");
        }
        catch (Exception ex)
        {
            // AggregateException 默认只打印外层消息，这里把每个内层异常都摊平打出来。
            Dump("Main", ex);
            throw;
        }
    }

    private static void Dump(string label, Exception ex, int depth = 0)
    {
        var indent = new string(' ', depth * 2);
        Console.WriteLine($"[BROWSER-FATAL] {indent}{label}: {ex.GetType().FullName}: {ex.Message}");
        Console.WriteLine($"[BROWSER-FATAL] {indent}  at {ex.StackTrace}");

        switch (ex)
        {
            case AggregateException agg:
                foreach (var inner in agg.InnerExceptions)
                {
                    Dump("inner", inner, depth + 1);
                }
                break;
            case { InnerException: { } inner }:
                Dump("inner", inner, depth + 1);
                break;
        }
    }

    /// <summary>
    /// 定位启动崩溃用的临时探针：确认 WASM 解释器下动态代码生成与 <c>[UnsafeAccessor]</c> 的可用性。
    /// </summary>
    private static void Probe()
    {
        Console.WriteLine($"[PROBE] IsDynamicCodeSupported={RuntimeFeature.IsDynamicCodeSupported}, "
                          + $"IsDynamicCodeCompiled={RuntimeFeature.IsDynamicCodeCompiled}, "
                          + $"resourceKeysStripped={IsResourceKeysStripped()}");
    }

    /// <summary>
    /// 检测异常消息是否被裁剪成资源键：若为 true，排查时看到的都是键名而非消息文本。
    /// </summary>
    private static bool IsResourceKeysStripped()
    {
        try
        {
            var empty = new int[1];
            _ = empty[2];
            return false;
        }
        catch (Exception ex)
        {
            return !ex.Message.Contains(' ');
        }
    }

    /// <summary>
    /// 入口项目自用的参数，不转发给 <see cref="ClassIsland.Program.AppEntry"/>。
    /// </summary>
    private static readonly string[] OwnArgs = ["--baseline", "--windowtest"];

    private static AppBuilder BuildAvaloniaApp(string[] args)
    {
        // AppEntry 会挂上 DiagnosticService 的处理器（它会尝试弹崩溃窗口），
        // 先挂自己的处理器保证原始异常一定被打印出来。
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                Dump("domain unhandled", ex);
            }
            else
            {
                Console.WriteLine($"[BROWSER-FATAL] domain unhandled: {e.ExceptionObject}");
            }
        };

        // 未观察到的 Task 异常在浏览器端常常是唯一线索，这里也打出来。
        TaskScheduler.UnobservedTaskException += (_, e) => Dump("unobserved task", e.Exception);

        // 转发查询串里传进来的应用参数（?arg=--skip-oobe 之类），排除入口项目自用的。
        // 不传 --mobile：它在整个仓库只有 App.axaml.cs 里 isDesktop 分支内一个读取点，
        // 浏览器端根本读不到，传了只会造成误解。
        //
        // 浏览器端默认跳过 OOBE：设置与档案不持久化（内存文件系统），每次访问都是全新状态，
        // 再走一遍迎新向导只会干扰主界面的展示。
        var appArgs = args
            .Where(a => a.StartsWith('-') && !OwnArgs.Contains(a))
            .ToList();
        if (!appArgs.Contains("--skip-oobe"))
        {
            appArgs.Add("--skip-oobe");
        }

        Console.WriteLine($"[BROWSER-BOOT] before AppEntry, appArgs=[{string.Join(", ", appArgs)}]");

        var buildApp = ClassIsland.Program.AppEntry(appArgs.ToArray());

        Console.WriteLine("[BROWSER-BOOT] after AppEntry");

        return AppBuilder.Configure<App>(() =>
            {
                Console.WriteLine("[BROWSER-BOOT] creating App instance");
                var app = buildApp();
                Console.WriteLine("[BROWSER-BOOT] App instance created");
                app.OperatingSystem = "browser";
                return app;
            })
            .With(new FontManagerOptions
            {
                DefaultFamilyName = MainWindow.DefaultFontFamilyKey,
                FontFallbacks =
                [
                    new FontFallback
                    {
                        FontFamily = MainWindow.DefaultFontFamily
                    }
                ]
            })
            .AfterSetup(_ => AttachViewHost())
            .LogToHostSink();
    }

    /// <summary>
    /// 创建页面上唯一的视图宿主并启动应用初始化流程，对应 Android 端 MainActivity.OnCreate 的职责。
    /// </summary>
    private static void AttachViewHost()
    {
        Console.WriteLine("[BROWSER-BOOT] AttachViewHost");

        if (Application.Current?.ApplicationLifetime is not ISingleViewApplicationLifetime singleView)
        {
            Console.WriteLine("[BROWSER-BOOT] lifetime is not ISingleViewApplicationLifetime, abort");
            return;
        }

        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            Dump("dispatcher unhandled", e.Exception);
            e.Handled = true;
        };

        var host = new BrowserViewHost();
        BrowserViewHostProvider.Instance.ViewHosts.Add(host);
        IViewHostProvider.Instance = BrowserViewHostProvider.Instance;
        singleView.MainView = host;

        // 课表条要等服务容器建好才能构造，挂到 AppStarted 上。
        AppBase.Current.AppStarted += (_, _) =>
        {
            try
            {
                host.AttachScheduleBar();
                Console.WriteLine("[BROWSER-BOOT] schedule bar attached");
            }
            catch (Exception ex)
            {
                Dump("AttachScheduleBar", ex);
            }
        };

        Console.WriteLine("[BROWSER-BOOT] view host attached, posting Init()");

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                ((App)AppBase.Current).Init();
                Console.WriteLine("[BROWSER-BOOT] Init() returned");
            }
            catch (Exception ex)
            {
                Dump("Init()", ex);
            }
        });
    }
}
