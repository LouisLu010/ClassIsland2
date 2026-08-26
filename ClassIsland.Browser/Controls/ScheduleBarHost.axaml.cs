using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Controls;
using ClassIsland.Core.Helpers.UI;
using ClassIsland.Services;
using ClassIsland.Shared;
using ClassIsland.ViewModels;
using Microsoft.Extensions.Logging;

namespace ClassIsland.Browser.Controls;

/// <summary>
/// 在浏览器端承载桌面版「大屏课表条」的控件。
/// </summary>
/// <remarks>
/// 浏览器无法构造任何 <see cref="Window"/>，所以 <c>MainWindow</c> 不可用；本控件把
/// <c>MainWindow.axaml</c> 里课表条那条可视链重新宿主到普通控件上，并补上原本由
/// <c>MainWindow</c> 承担的三件启动工作：挂载主题样式、注入主界面字号资源、
/// 打开 <see cref="ComponentPresenter"/> 的内容加载门控。
/// </remarks>
public partial class ScheduleBarHost : UserControl
{
    public MainViewModel ViewModel { get; } = new();

    public IComponentsService ComponentsService { get; } = IAppHost.GetService<IComponentsService>();

    private SettingsService SettingsService { get; } = IAppHost.GetService<SettingsService>();

    private IProfileService ProfileService { get; } = IAppHost.GetService<IProfileService>();

    private IXamlThemeService ThemeService { get; } = IAppHost.GetService<IXamlThemeService>();

    private ILogger<ScheduleBarHost> Logger { get; } = IAppHost.GetService<ILogger<ScheduleBarHost>>();

    public ScheduleBarHost()
    {
        // MainWindow.axaml 的绑定路径都以 DataContext 为自身、ViewModel.* 为二级路径，
        // 这里保持一致，移植过来的 XAML 才能原样工作。
        DataContext = this;
        ViewModel.Settings = SettingsService.Settings;
        ViewModel.Profile = ProfileService.Profile;

        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        // 桌面端这三步分散在 MainWindow.Show() 与 PostInit() 里，浏览器端没有 Show() 可覆写，
        // 因此在这里按同样顺序补齐。
        MountThemes();
        ApplyFontResources();

        // ComponentPresenter 的内容加载被 IsMainWindowLoaded 门控（inherits:true 附加属性），
        // 不置位则每一行都是空白且不会报错。
        ComponentPresenter.SetIsMainWindowLoaded(this, true);

        Logger.LogInformation("课表条宿主已就绪，共 {} 行。",
            ComponentsService.CurrentComponents.Lines.Count);
    }

    /// <summary>
    /// 把主题样式挂到本控件内的 ResourceLoaderBorder 上。
    /// </summary>
    private void MountThemes()
    {
        // IXamlThemeService.MainWindow 的类型已放宽到 Control，正是为了这里。
        ThemeService.MainWindow = this;
        ThemeService.LoadAllThemes();
    }

    /// <summary>
    /// 注入主界面的四个字号资源与自定义前景色，对应 MainWindow.UpdateTheme 的后半段。
    /// 缺少它们时组件里的 {DynamicResource MainWindowBodyFontSize} 会解析失败。
    /// </summary>
    private void ApplyFontResources()
    {
        var settings = SettingsService.Settings;
        ResourceLoaderBorder.Resources[nameof(settings.MainWindowSecondaryFontSize)] =
            settings.MainWindowSecondaryFontSize;
        ResourceLoaderBorder.Resources[nameof(settings.MainWindowBodyFontSize)] =
            settings.MainWindowBodyFontSize;
        ResourceLoaderBorder.Resources[nameof(settings.MainWindowEmphasizedFontSize)] =
            settings.MainWindowEmphasizedFontSize;
        ResourceLoaderBorder.Resources[nameof(settings.MainWindowLargeFontSize)] =
            settings.MainWindowLargeFontSize;
        ControlColorHelper.SetControlForegroundColor(ResourceLoaderBorder,
            settings.CustomForegroundColor, settings.IsCustomForegroundColorEnabled);
    }
}
