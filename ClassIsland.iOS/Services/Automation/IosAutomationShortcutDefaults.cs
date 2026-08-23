using Foundation;

namespace ClassIsland.iOS.Services.Automation;

/// <summary>
/// 主应用与 App Intents 所在扩展之间共享的自动化数据。
/// </summary>
internal static class IosAutomationShortcutDefaults
{
    public const string AppGroupIdentifier = "group.cn.classisland.ios.automation";
    public const string CatalogKey = "classisland.shortcuts.automation-catalog";
    public const string PendingAutomationUriKey =
        "classisland.shortcuts.pending-automation-uri";

    public static NSUserDefaults Shared { get; } =
        new(AppGroupIdentifier, NSUserDefaultsType.SuiteName);
}
