using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Models.Automation;
using ClassIsland.Services;
using ClassIsland.Shared.Models.Automation;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ClassIsland.Platforms.Abstractions.Tests;

public sealed class ActionServiceCompatibilityTests
{
    [Fact]
    public async Task InvokeActionSetAsync_BlocksEntireSetWhenAnyActionIsUnsupported()
    {
        var actionSet = new ActionSet
        {
            ActionItems =
            [
                new ActionItem { Id = "classisland.showNotification" },
                new ActionItem { Id = "classisland.os.run" }
            ]
        };
        var statusChangeCount = 0;
        actionSet.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ActionSet.Status))
            {
                statusChangeCount++;
            }
        };
        var service = new ActionService(
            NullLogger<ActionService>.Instance,
            new StubCompatibilityService());

        await service.InvokeActionSetAsync(actionSet);

        Assert.Equal(0, statusChangeCount);
        Assert.False(actionSet.IsWorking);
    }

    private sealed class StubCompatibilityService : IAutomationCompatibilityService
    {
        public IReadOnlyList<AutomationCompatibilityIssue> Evaluate(Workflow workflow) => [];

        public IReadOnlyList<AutomationCompatibilityIssue> Evaluate(ActionSet actionSet) =>
        [new(AutomationCompatibilityNodeKind.Action, "classisland.os.run", "不支持")];
    }
}
