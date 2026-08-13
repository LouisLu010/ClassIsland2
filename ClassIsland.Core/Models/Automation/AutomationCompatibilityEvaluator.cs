using ClassIsland.Shared.Models.Automation;

namespace ClassIsland.Core.Models.Automation;

public enum AutomationCompatibilityNodeKind
{
    Trigger,
    Rule,
    Action
}

public sealed record AutomationCompatibilityIssue(
    AutomationCompatibilityNodeKind Kind,
    string Id,
    string Message = "");
public static class AutomationCompatibilityEvaluator
{
    public static IReadOnlyList<AutomationCompatibilityIssue> Evaluate(
        Workflow workflow,
        IReadOnlySet<string> supportedTriggerIds,
        IReadOnlySet<string> supportedRuleIds,
        IReadOnlySet<string> supportedActionIds)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(supportedTriggerIds);
        ArgumentNullException.ThrowIfNull(supportedRuleIds);
        ArgumentNullException.ThrowIfNull(supportedActionIds);

        var issues = workflow.Triggers
            .Where(trigger => IsUnsupported(trigger.Id, supportedTriggerIds))
            .Select(trigger => new AutomationCompatibilityIssue(
                AutomationCompatibilityNodeKind.Trigger,
                trigger.Id))
            .ToList();

        if (workflow.IsConditionEnabled)
        {
            issues.AddRange(workflow.Ruleset.Groups
                .Where(group => group.IsEnabled)
                .SelectMany(group => group.Rules)
                .Where(rule => IsUnsupported(rule.Id, supportedRuleIds))
                .Select(rule => new AutomationCompatibilityIssue(
                    AutomationCompatibilityNodeKind.Rule,
                    rule.Id)));
        }

        issues.AddRange(EvaluateActionSet(workflow.ActionSet, supportedActionIds));
        return issues;
    }

    public static IReadOnlyList<AutomationCompatibilityIssue> EvaluateActionSet(
        ActionSet actionSet,
        IReadOnlySet<string> supportedActionIds)
    {
        ArgumentNullException.ThrowIfNull(actionSet);
        ArgumentNullException.ThrowIfNull(supportedActionIds);

        return actionSet.ActionItems
            .Where(action => IsUnsupported(action.Id, supportedActionIds))
            .Select(action => new AutomationCompatibilityIssue(
                AutomationCompatibilityNodeKind.Action,
                action.Id))
            .ToArray();
    }

    private static bool IsUnsupported(string id, IReadOnlySet<string> supportedIds) =>
        !string.IsNullOrWhiteSpace(id) && !supportedIds.Contains(id);
}
