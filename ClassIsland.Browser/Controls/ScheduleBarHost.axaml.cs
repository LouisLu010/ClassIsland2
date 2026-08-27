using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Controls;
using ClassIsland.Core.Helpers.UI;
using ClassIsland.Models.ComponentSettings;
using ClassIsland.Services;
using ClassIsland.Shared;
using ClassIsland.Shared.Models.Profile;
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

        // 浏览器端档案为空（内存文件系统，刷新即空）时注入一份完整示例课表，
        // 以便课表条能展示完整一周效果。仅在 Web 端生效，桌面端不受影响。
        SeedDemoProfile();
        ConfigureScheduleComponent();

        Logger.LogInformation("课表条宿主已就绪，共 {} 行。",
            ComponentsService.CurrentComponents.Lines.Count);
    }

    /// <summary>
    /// 让课表条始终显示今天的课程：关闭「放学后切到明天预览」。
    /// </summary>
    /// <remarks>
    /// <see cref="LessonControlSettings.TomorrowScheduleShowMode"/> 默认是 1（放学后显示明天），
    /// 会在晚间隐藏今天的课程、只显示一个「明天」胶囊。为满足课表条的展示需求，
    /// 浏览器端把它设为 0（不切换），使 <c>ShowCurrentLessonOnlyOnClass=false</c> 与
    /// <c>HideFinishedClass=false</c> 生效，全天课程列表始终可见。
    /// </remarks>
    private void ConfigureScheduleComponent()
    {
        foreach (var line in ComponentsService.CurrentComponents.Lines)
        {
            foreach (var child in line.Children ?? [])
            {
                if (child.Settings is LessonControlSettings settings)
                {
                    // 放学后显示明天课程：白天上课时显示当前课程，放学后才切到明天预览。
                    settings.TomorrowScheduleShowMode = 1;
                    settings.ShowCurrentLessonOnlyOnClass = false;
                    settings.HideFinishedClass = false;
                    // 晚上放学后的明天预览不受影响；但白天已结束的课不要淡化到看不见。
                    settings.FadeCompletedClasses = false;
                }
            }
        }
    }

    /// <summary>
    /// 档案为空时注入示例课表：5 门科目、4 个时段、每天一个按「每周」规则生效的计划。
    /// 用于验证课表条端到端渲染，并作为 Web 展示的默认内容。
    /// </summary>
    private void SeedDemoProfile()
    {
        try
        {
            var profile = ProfileService.Profile;
            if (profile.ClassPlans.Count > 0)
            {
                return; // 已有档案则不覆盖（用户可能已配置）
            }

            var subjects = new Dictionary<string, string>
            {
                ["语文"] = "语", ["数学"] = "数", ["英语"] = "英", ["物理"] = "物", ["化学"] = "化"
            };
            var subjectIds = subjects.ToDictionary(x => x.Key, _ =>
            {
                var id = Guid.NewGuid();
                return id;
            });
            foreach (var (name, _) in subjects)
            {
                profile.Subjects[subjectIds[name]] = new Subject { Name = name, Initial = subjects[name] };
            }

            var layoutId = Guid.NewGuid();
            var layout = new TimeLayout { Name = "默认时间表" };
            // 时段覆盖全天，傍晚加一节「晚自习」，保证任意测试时刻都有「当前课程」，
            // 这样课表条岛（minimized，只显示当前课）能始终显示课程而不只是日期。
            var times = new[]
            {
                (8, 0, 8, 45), (8, 55, 9, 40), (9, 55, 10, 40),
                (10, 50, 11, 35), (14, 0, 14, 45), (14, 55, 15, 40),
                (18, 30, 21, 30) // 晚自习，覆盖晚间
            };
            for (var i = 0; i < times.Length; i++)
            {
                layout.Layouts.Add(new TimeLayoutItem
                {
                    TimeType = 0, // 0=上课，显示课程名；1=课间
                    StartTime = new TimeSpan(times[i].Item1, times[i].Item2, 0),
                    EndTime = new TimeSpan(times[i].Item3, times[i].Item4, 0)
                });
            }
            profile.TimeLayouts[layoutId] = layout;

            var dayNames = new[]
            {
                DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
            };
            var subjectNames = subjects.Keys.ToArray();
            for (var d = 0; d < 7; d++)
            {
                var plan = new ClassPlan
                {
                    Name = $"周{ToWeek(d)}",
                    TimeLayoutId = layoutId,
                    IsEnabled = true,
                    IsActivated = true,
                    AssociatedGroup = ClassPlanGroup.GlobalGroupGuid
                };
                plan.TimeRule.Type = TimeRule.TimeRuleType.Weekly;
                plan.TimeRule.WeekDay = (int)dayNames[d];
                for (var p = 0; p < times.Length; p++)
                {
                    // 每天课程轮换，保证各天内容不同
                    var subjectName = subjectNames[(d * 2 + p) % subjectNames.Length];
                    plan.Classes.Add(new ClassInfo
                    {
                        Index = p,
                        SubjectId = subjectIds[subjectName],
                        IsEnabled = true
                    });
                }
                profile.ClassPlans[Guid.NewGuid()] = plan;
            }

            Logger.LogInformation("[DEMO] 已注入完整一周示例课表。");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[DEMO] 注入示例课表失败");
        }
    }

    private static string ToWeek(int index) => index switch
    {
        0 => "一", 1 => "二", 2 => "三", 3 => "四", 4 => "五", 5 => "六", _ => "日"
    };

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
