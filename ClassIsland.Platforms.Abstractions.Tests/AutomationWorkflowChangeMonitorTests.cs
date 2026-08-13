using ClassIsland.Core.Models.Automation;
using ClassIsland.Shared.Models.Automation;
using Xunit;

namespace ClassIsland.Platforms.Abstractions.Tests;

public sealed class AutomationWorkflowChangeMonitorTests
{
    [Fact]
    public void Monitor_TracksNestedSettingsAndCollectionsUntilDisposed()
    {
        var initialAction = new ActionItem { Id = "classisland.showNotification" };
        var workflow = new Workflow
        {
            ActionSet = new() { ActionItems = [initialAction] }
        };
        var changed = 0;
        using var monitor = new AutomationWorkflowChangeMonitor(workflow);
        monitor.Changed += (_, _) => changed++;

        initialAction.Id = "classisland.os.run";
        var addedAction = new ActionItem { Id = "classisland.showNotification" };
        workflow.ActionSet.ActionItems.Add(addedAction);
        addedAction.Id = "plugin.missing";

        Assert.Equal(3, changed);

        monitor.Dispose();
        addedAction.Id = "classisland.showNotification";
        Assert.Equal(3, changed);
    }

    [Fact]
    public void Monitor_RebuildsSubscriptionsWhenNestedCollectionIsReplaced()
    {
        var workflow = new Workflow();
        var changed = 0;
        using var monitor = new AutomationWorkflowChangeMonitor(workflow);
        monitor.Changed += (_, _) => changed++;

        workflow.Triggers = [new TriggerSettings { Id = "classisland.cron" }];
        var changesAfterReplacement = changed;
        workflow.Triggers[0].Id = "classisland.trayMenu";

        Assert.True(changesAfterReplacement > 0);
        Assert.True(changed > changesAfterReplacement);
    }

    [Fact]
    public void Monitor_IgnoresDerivedCompatibilityState()
    {
        var workflow = new Workflow();
        var changed = 0;
        using var monitor = new AutomationWorkflowChangeMonitor(workflow);
        monitor.Changed += (_, _) => changed++;

        workflow.PlatformCompatibilityIssues =
        [
            new(
                AutomationCompatibilityNodeKind.Action,
                "classisland.os.run",
                "当前平台不可用")
        ];

        Assert.Equal(0, changed);
    }
}
