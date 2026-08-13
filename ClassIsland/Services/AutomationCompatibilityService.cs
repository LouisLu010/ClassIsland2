using System;
using System.Collections.Generic;
using System.Linq;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Models.Automation;
using ClassIsland.Shared.Models.Automation;

namespace ClassIsland.Services;

public sealed class AutomationCompatibilityService : IAutomationCompatibilityService
{
    private static readonly IReadOnlyDictionary<string, string> KnownNodeNames =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["classisland.trayMenu"] = "从托盘菜单运行时",
            ["classisland.windows.className"] = "前台窗口类名",
            ["classisland.windows.text"] = "前台窗口标题",
            ["classisland.windows.status"] = "前台窗口状态",
            ["classisland.windows.processName"] = "前台窗口进程",
            ["classisland.os.run"] = "运行",
            ["classisland.app.quit"] = "退出 ClassIsland",
            ["classisland.app.restart"] = "重启 ClassIsland"
        };

    public IReadOnlyList<AutomationCompatibilityIssue> Evaluate(Workflow workflow) =>
        AddMessages(AutomationCompatibilityEvaluator.Evaluate(
            workflow,
            IAutomationService.RegisteredTriggers.Select(x => x.Id).ToHashSet(StringComparer.Ordinal),
            IRulesetService.Rules.Keys.ToHashSet(StringComparer.Ordinal),
            IActionService.ActionInfos.Keys.ToHashSet(StringComparer.Ordinal)));

    public IReadOnlyList<AutomationCompatibilityIssue> Evaluate(ActionSet actionSet) =>
        AddMessages(AutomationCompatibilityEvaluator.EvaluateActionSet(
            actionSet,
            IActionService.ActionInfos.Keys.ToHashSet(StringComparer.Ordinal)));

    private static IReadOnlyList<AutomationCompatibilityIssue> AddMessages(
        IReadOnlyList<AutomationCompatibilityIssue> issues) =>
        issues.Select(issue => issue with { Message = CreateMessage(issue) }).ToArray();

    private static string CreateMessage(AutomationCompatibilityIssue issue)
    {
        var kind = issue.Kind switch
        {
            AutomationCompatibilityNodeKind.Trigger => "触发器",
            AutomationCompatibilityNodeKind.Rule => "规则",
            AutomationCompatibilityNodeKind.Action => "行动",
            _ => throw new ArgumentOutOfRangeException(nameof(issue))
        };
        var name = KnownNodeNames.GetValueOrDefault(issue.Id, issue.Id);
        return $"{kind}“{name}”在当前平台不可用。";
    }
}
