using System.Text.Json;
using ClassIsland.Core.Models.Automation;
using ClassIsland.Models.Automation.Triggers;
using ClassIsland.Services.Automation;
using ClassIsland.Shared.Models.Automation;
using Xunit;

namespace ClassIsland.Platforms.Abstractions.Tests;

public sealed class IosAutomationShortcutCatalogBuilderTests
{
    [Fact]
    public void Build_CreatesNamedChoicesForRunnableUriWorkflows()
    {
        var later = CreateWorkflow("晚间提醒", "evening");
        var earlier = CreateWorkflow("晨间提醒", "morning/start");

        var entries = IosAutomationShortcutCatalogBuilder.Build([later, earlier]);

        Assert.Collection(
            entries,
            entry =>
            {
                Assert.Equal($"{later.ActionSet.Guid:N}:evening", entry.Id);
                Assert.Equal("晚间提醒", entry.Name);
                Assert.Equal("evening", entry.UriSuffix);
            },
            entry =>
            {
                Assert.Equal($"{earlier.ActionSet.Guid:N}:morning/start", entry.Id);
                Assert.Equal("晨间提醒", entry.Name);
                Assert.Equal("morning/start", entry.UriSuffix);
            });
    }

    [Fact]
    public void Build_IgnoresWorkflowsThatCannotRunFromShortcuts()
    {
        var disabled = CreateWorkflow("已禁用", "disabled");
        disabled.ActionSet.IsEnabled = false;
        var incompatible = CreateWorkflow("不兼容", "incompatible");
        incompatible.PlatformCompatibilityIssues =
        [
            new AutomationCompatibilityIssue(
                AutomationCompatibilityNodeKind.Action,
                "classisland.os.run",
                "当前平台不可用。")
        ];
        var invalidSuffix = CreateWorkflow("后缀无效", "folder//item");
        var withoutUriTrigger = CreateWorkflow("其他触发器", "unused");
        withoutUriTrigger.Triggers[0].Id = "classisland.cron";
        var withoutActions = CreateWorkflow("没有行动", "empty");
        withoutActions.ActionSet.ActionItems.Clear();

        var entries = IosAutomationShortcutCatalogBuilder.Build(
            [disabled, incompatible, invalidSuffix, withoutUriTrigger, withoutActions]);

        Assert.Empty(entries);
    }

    [Fact]
    public void Build_ReadsSerializedTriggerSettingsAndDeduplicatesChoices()
    {
        var workflow = CreateWorkflow("  ", "folder/item");
        workflow.Triggers[0].Settings = JsonSerializer.SerializeToElement(
            new UriTriggerSettings { UriSuffix = " folder/item " });
        workflow.Triggers.Add(new TriggerSettings
        {
            Id = "classisland.uri",
            Settings = new UriTriggerSettings { UriSuffix = "folder/item" }
        });

        var entry = Assert.Single(
            IosAutomationShortcutCatalogBuilder.Build([workflow]));

        Assert.Equal("未命名自动化", entry.Name);
        Assert.Equal("folder/item", entry.UriSuffix);
    }

    private static Workflow CreateWorkflow(string name, string uriSuffix) => new()
    {
        Triggers =
        [
            new TriggerSettings
            {
                Id = "classisland.uri",
                Settings = new UriTriggerSettings { UriSuffix = uriSuffix }
            }
        ],
        ActionSet = new ActionSet
        {
            Name = name,
            IsEnabled = true,
            ActionItems = [new ActionItem { Id = "classisland.showNotification" }]
        }
    };
}
