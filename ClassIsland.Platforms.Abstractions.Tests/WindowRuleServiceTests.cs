using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Models.Ruleset;
using ClassIsland.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ClassIsland.Platforms.Abstractions.Tests;

public sealed class WindowRuleServiceTests
{
    private static readonly string[] WindowRuleIds =
    [
        "classisland.windows.className",
        "classisland.windows.text",
        "classisland.windows.status",
        "classisland.windows.processName"
    ];

    [Fact]
    public void Constructor_OnAppleMobileWithoutWindowRules_DoesNotRegisterHandlers()
    {
        var rulesetService = new StubRulesetService([]);

        var service = new WindowRuleService(
            NullLogger<WindowRuleService>.Instance,
            rulesetService,
            isAppleMobile: true);

        Assert.False(service.IsForegroundWindowClassIsland());
        Assert.Empty(rulesetService.RegisteredRuleIds);
    }

    [Fact]
    public void Constructor_OnDesktop_RegistersAllWindowRuleHandlers()
    {
        var rulesetService = new StubRulesetService(WindowRuleIds);

        _ = new WindowRuleService(
            NullLogger<WindowRuleService>.Instance,
            rulesetService,
            isAppleMobile: false);

        Assert.Equal(WindowRuleIds, rulesetService.RegisteredRuleIds);
    }

    private sealed class StubRulesetService(IEnumerable<string> availableRuleIds) : IRulesetService
    {
        private readonly HashSet<string> _availableRuleIds = new(availableRuleIds, StringComparer.Ordinal);
        private readonly List<string> _registeredRuleIds = [];

        public IReadOnlyList<string> RegisteredRuleIds => _registeredRuleIds;

        public event EventHandler? ForegroundWindowChanged
        {
            add { }
            remove { }
        }

        public event EventHandler? StatusUpdated
        {
            add { }
            remove { }
        }

        public bool IsRulesetSatisfied(Ruleset ruleset) => throw new NotSupportedException();

        public void RegisterRuleHandler(string id, RuleRegistryInfo.HandleDelegate handler)
        {
            if (!_availableRuleIds.Contains(id))
            {
                throw new KeyNotFoundException($"找不到规则 {id}。");
            }

            _registeredRuleIds.Add(id);
        }

        public void NotifyStatusChanged()
        {
        }
    }
}
