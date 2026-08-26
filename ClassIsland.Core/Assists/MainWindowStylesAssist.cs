using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ClassIsland.Core.Assists;

/// <summary>
/// 主界面样式助手类，用于传递当前上下文的主界面样式信息。
/// </summary>
public class MainWindowStylesAssist
{
    public static readonly AttachedProperty<bool> IsIslandSeperatedProperty =
        AvaloniaProperty.RegisterAttached<MainWindowStylesAssist, Control, bool>("IsIslandSeperated", inherits:true);

    public static void SetIsIslandSeperated(Control obj, bool value) => obj.SetValue(IsIslandSeperatedProperty, value);
    public static bool GetIsIslandSeperated(Control obj) => obj.GetValue(IsIslandSeperatedProperty);

    public static readonly AttachedProperty<double> CornerRadiusProperty =
        AvaloniaProperty.RegisterAttached<MainWindowStylesAssist, Control, double>("CornerRadius", inherits:true);

    public static void SetCornerRadius(Control obj, double value) => obj.SetValue(CornerRadiusProperty, value);
    public static double GetCornerRadius(Control obj) => obj.GetValue(CornerRadiusProperty);

    public static readonly AttachedProperty<double> IslandSpacingProperty =
        AvaloniaProperty.RegisterAttached<MainWindowStylesAssist, Control, double>("IslandSpacing", inherits:true);

    public static void SetIslandSpacing(Control obj, double value) => obj.SetValue(IslandSpacingProperty, value);
    public static double GetIslandSpacing(Control obj) => obj.GetValue(IslandSpacingProperty);

    /// <summary>
    /// 主界面停靠方位。
    /// </summary>
    /// <remarks>
    /// 主题样式原先通过 <c>FindAncestor AncestorType=MainWindow</c> 直接读取此值，
    /// 那样在没有主窗口祖先的宿主（浏览器端的课表条）里绑定会静默失效。
    /// 改为继承式附加属性后，任何宿主都能提供。
    /// </remarks>
    public static readonly AttachedProperty<int> WindowDockingLocationProperty =
        AvaloniaProperty.RegisterAttached<MainWindowStylesAssist, Control, int>("WindowDockingLocation", inherits:true);

    public static void SetWindowDockingLocation(Control obj, int value) => obj.SetValue(WindowDockingLocationProperty, value);
    public static int GetWindowDockingLocation(Control obj) => obj.GetValue(WindowDockingLocationProperty);
    

    public static readonly AttachedProperty<bool> IsProgressAccuracyReducedProperty =
        AvaloniaProperty.RegisterAttached<MainWindowStylesAssist, Control, bool>("IsProgressAccuracyReduced", inherits:true);

    public static void SetIsProgressAccuracyReduced(Control obj, bool value) => obj.SetValue(IsProgressAccuracyReducedProperty, value);
    public static bool GetIsProgressAccuracyReduced(Control obj) => obj.GetValue(IsProgressAccuracyReducedProperty);

    public static readonly AttachedProperty<Color> BackgroundCorlorProperty =
        AvaloniaProperty.RegisterAttached<MainWindowStylesAssist, Control, Color>("BackgroundColor", inherits:true);

    public static void SetBackgroundCorlor(Control obj, Color value) => obj.SetValue(BackgroundCorlorProperty, value);
    public static Color GetBackgroundColor(Control obj) => obj.GetValue(BackgroundCorlorProperty);

    public static readonly AttachedProperty<double> BackgroundOpacityProperty =
        AvaloniaProperty.RegisterAttached<MainWindowStylesAssist, Control, double>("BackgroundOpacity", 0.5, inherits: true);

    public static void SetBackgroundOpacity(Control obj, double value) => obj.SetValue(BackgroundOpacityProperty, value);
    public static double GetBackgroundOpacity(Control obj) => obj.GetValue(BackgroundOpacityProperty);

    public static readonly AttachedProperty<bool> IsCustomBackgroundColorEnabledProperty =
        AvaloniaProperty.RegisterAttached<MainWindowStylesAssist, Control, bool>("IsCustomBackgroundColorEnabled", inherits: true);

    public static void SetIsCustomBackgroundColorEnabled(Control obj, bool value) => obj.SetValue(IsCustomBackgroundColorEnabledProperty, value);
    public static bool GetIsCustomBackgroundColorEnabled(Control obj) => obj.GetValue(IsCustomBackgroundColorEnabledProperty);

    public static readonly AttachedProperty<bool> MainWindowInEditModeProperty =
        AvaloniaProperty.RegisterAttached<MainWindowStylesAssist, Control, bool>("MainWindowInEditMode", inherits: true);

    public static void SetMainWindowInEditMode(Control obj, bool value) => obj.SetValue(MainWindowInEditModeProperty, value);
    public static bool GetMainWindowInEditMode(Control obj) => obj.GetValue(MainWindowInEditModeProperty);
}