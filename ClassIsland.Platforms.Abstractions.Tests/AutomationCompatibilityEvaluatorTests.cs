using ClassIsland.Core.Models.Automation;
using ClassIsland.Shared.Models.Automation;
using Xunit;

namespace ClassIsland.Platforms.Abstractions.Tests;

public sealed class AutomationCompatibilityEvaluatorTests
{
    private static readonly HashSet<string> SupportedTriggers =
        ["classisland.cron", "classisland.uri"];

    private static readonly HashSet<string> SupportedRules =
        ["classisland.lessons.currentSubject"];

    private static readonly HashSet<string> SupportedActions =
        ["classisland.showNotification"];

    [Fact]
    public void Evaluate_ReportsEveryUnsupportedNodeInEnabledWorkflow()
    {
        var workflow = new Workflow
        {
            IsConditionEnabled = true,
            Triggers =
            [
                new TriggerSettings { Id = "classisland.trayMenu" },
                new TriggerSettings { Id = "classisland.cron" }
            ],
            Ruleset = new()
            {
                Groups =
                [
                    new()
                    {
                        Rules =
                        [
                            new() { Id = "classisland.windows.text" },
                            new() { Id = "classisland.lessons.currentSubject" }
                        ]
                    }
                ]
            },
            ActionSet = new()
            {
                ActionItems =
                [
                    new ActionItem { Id = "classisland.os.run" },
                    new ActionItem { Id = "classisland.showNotification" }
                ]
            }
        };

        var issues = AutomationCompatibilityEvaluator.Evaluate(
            workflow,
            SupportedTriggers,
            SupportedRules,
            SupportedActions);

        Assert.Collection(
            issues,
            issue =>
            {
                Assert.Equal(AutomationCompatibilityNodeKind.Trigger, issue.Kind);
                Assert.Equal("classisland.trayMenu", issue.Id);
            },
            issue =>
            {
                Assert.Equal(AutomationCompatibilityNodeKind.Rule, issue.Kind);
                Assert.Equal("classisland.windows.text", issue.Id);
            },
            issue =>
            {
                Assert.Equal(AutomationCompatibilityNodeKind.Action, issue.Kind);
                Assert.Equal("classisland.os.run", issue.Id);
            });
    }

    [Fact]
    public void Evaluate_IgnoresRulesWhenConditionsAreDisabled()
    {
        var workflow = new Workflow
        {
            IsConditionEnabled = false,
            Ruleset = new()
            {
                Groups =
                [
                    new()
                    {
                        Rules = [new() { Id = "classisland.windows.text" }]
                    }
                ]
            }
        };

        var issues = AutomationCompatibilityEvaluator.Evaluate(
            workflow,
            SupportedTriggers,
            SupportedRules,
            SupportedActions);

        Assert.Empty(issues);
    }

    [Fact]
    public void EvaluateActionSet_ReportsUnknownActionWithoutExecutingPartialSet()
    {
        var actionSet = new ActionSet
        {
            ActionItems =
            [
                new ActionItem { Id = "classisland.showNotification" },
                new ActionItem { Id = "plugin.missing" }
            ]
        };

        var issues = AutomationCompatibilityEvaluator.EvaluateActionSet(
            actionSet,
            SupportedActions);

        var issue = Assert.Single(issues);
        Assert.Equal(AutomationCompatibilityNodeKind.Action, issue.Kind);
        Assert.Equal("plugin.missing", issue.Id);
    }
}
