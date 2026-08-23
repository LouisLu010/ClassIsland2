using ClassIsland.Core.Models.Automation;
using ClassIsland.Models.Actions;
using ClassIsland.Models.Automation.Triggers;
using ClassIsland.Services.Automation;
using ClassIsland.Shared.Models.Automation;
using Xunit;

namespace ClassIsland.Platforms.Abstractions.Tests;

public sealed class IosAutomationNotificationScheduleCompilerTests
{
    private static readonly DateTime LogicalNow = new(2026, 8, 13, 7, 0, 0);
    private static readonly DateTimeOffset SystemNow =
        new(2026, 8, 13, 7, 0, 0, TimeSpan.FromHours(8));
    private static readonly TimeZoneInfo ChinaStandardTime =
        TimeZoneInfo.CreateCustomTimeZone(
            "Test/China",
            TimeSpan.FromHours(8),
            "Test China Time",
            "Test China Time");

    [Fact]
    public void Compile_CreatesStableNativeRequestsForStrictCronNotificationWorkflow()
    {
        var workflow = CreateWorkflow("0 8 * * *");

        var requests = IosAutomationNotificationScheduleCompiler.Compile(
            [workflow],
            LogicalNow,
            SystemNow,
            LogicalNow.AddDays(2),
            10,
            allowNotificationSound: true,
            timeZone: ChinaStandardTime);

        Assert.Collection(
            requests,
            request =>
            {
                Assert.StartsWith(
                    $"classisland.automation.{workflow.ActionSet.Guid:N}.",
                    request.Identifier);
                Assert.Equal(
                    new DateTimeOffset(2026, 8, 13, 8, 0, 0, TimeSpan.FromHours(8)),
                    request.FireAt);
                Assert.Equal("课间提醒", request.Title);
                Assert.Equal("记得喝水", request.Body);
                Assert.True(request.PlaySound);
            },
            request => Assert.Equal(
                new DateTimeOffset(2026, 8, 14, 8, 0, 0, TimeSpan.FromHours(8)),
                request.FireAt));
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void Compile_RejectsWorkflowThatIsNotStrictlyNativeSafe(
        bool hasRules,
        bool hasExtraTrigger,
        bool hasUnsupportedAction)
    {
        var workflow = CreateWorkflow("0 8 * * *");
        workflow.IsConditionEnabled = hasRules;
        if (hasExtraTrigger)
        {
            workflow.Triggers.Add(new TriggerSettings { Id = "classisland.uri" });
        }
        if (hasUnsupportedAction)
        {
            workflow.ActionSet.ActionItems.Add(new ActionItem { Id = "classisland.sleep" });
        }

        var requests = IosAutomationNotificationScheduleCompiler.Compile(
            [workflow],
            LogicalNow,
            SystemNow,
            LogicalNow.AddDays(2),
            10,
            allowNotificationSound: true,
            timeZone: ChinaStandardTime);

        Assert.Empty(requests);
    }

    [Fact]
    public void Compile_OrdersGloballyAndHonorsCapacity()
    {
        var later = CreateWorkflow("0 9 * * *");
        var earlier = CreateWorkflow("0 8 * * *");

        var requests = IosAutomationNotificationScheduleCompiler.Compile(
            [later, earlier],
            LogicalNow,
            SystemNow,
            LogicalNow.AddDays(3),
            2,
            allowNotificationSound: false,
            timeZone: ChinaStandardTime);

        Assert.Equal(2, requests.Count);
        Assert.True(requests[0].FireAt < requests[1].FireAt);
        Assert.All(requests, request => Assert.False(request.PlaySound));
    }

    [Fact]
    public void Compile_IgnoresInvalidCronExpression()
    {
        var workflow = CreateWorkflow("not a cron expression");

        var requests = IosAutomationNotificationScheduleCompiler.Compile(
            [workflow],
            LogicalNow,
            SystemNow,
            LogicalNow.AddDays(2),
            10,
            allowNotificationSound: true,
            timeZone: ChinaStandardTime);

        Assert.Empty(requests);
    }

    private static Workflow CreateWorkflow(string cronExpression) => new()
    {
        Triggers =
        [
            new TriggerSettings
            {
                Id = "classisland.cron",
                Settings = new CronTriggerSettings { CronExpression = cronExpression }
            }
        ],
        ActionSet = new ActionSet
        {
            IsEnabled = true,
            ActionItems =
            [
                new ActionItem
                {
                    Id = "classisland.showNotification",
                    Settings = new NotificationActionSettings
                    {
                        Mask = "课间提醒",
                        Content = "记得喝水",
                        IsSoundEffectEnabled = true
                    }
                }
            ]
        }
    };
}
