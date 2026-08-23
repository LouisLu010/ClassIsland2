using ClassIsland.Core.Models.Automation;
using ClassIsland.Shared.Models.Automation;

namespace ClassIsland.Core.Abstractions.Services;

public interface IAutomationCompatibilityService
{
    IReadOnlyList<AutomationCompatibilityIssue> Evaluate(Workflow workflow);

    IReadOnlyList<AutomationCompatibilityIssue> Evaluate(ActionSet actionSet);
}
