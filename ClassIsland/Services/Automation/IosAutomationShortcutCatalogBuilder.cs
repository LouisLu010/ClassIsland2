using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ClassIsland.Core.Models.Automation;
using ClassIsland.Models.Automation.Triggers;
using ClassIsland.Shared.Models.Automation;

namespace ClassIsland.Services.Automation;

internal sealed record IosAutomationShortcutCatalogEntry(
    string Id,
    string Name,
    string UriSuffix);

internal static class IosAutomationShortcutCatalogBuilder
{
    private const string UriTriggerId = "classisland.uri";
    private const string FallbackWorkflowName = "未命名自动化";

    public static IosAutomationShortcutCatalogEntry[] Build(
        IEnumerable<Workflow> workflows)
    {
        ArgumentNullException.ThrowIfNull(workflows);

        var entries = new Dictionary<string, IosAutomationShortcutCatalogEntry>(
            StringComparer.Ordinal);
        foreach (var workflow in workflows.Where(IsRunnable))
        {
            var workflowName = string.IsNullOrWhiteSpace(workflow.ActionSet.Name)
                ? FallbackWorkflowName
                : workflow.ActionSet.Name.Trim();
            foreach (var trigger in workflow.Triggers.Where(x =>
                         string.Equals(x.Id, UriTriggerId, StringComparison.Ordinal)))
            {
                if (!TryReadSettings(trigger.Settings, out var settings) ||
                    !TryNormalizeSuffix(settings.UriSuffix, out var suffix))
                {
                    continue;
                }

                var id = $"{workflow.ActionSet.Guid:N}:{suffix}";
                entries.TryAdd(
                    id,
                    new IosAutomationShortcutCatalogEntry(id, workflowName, suffix));
            }
        }

        return entries.Values
            .OrderBy(x => x.Name, StringComparer.Ordinal)
            .ThenBy(x => x.UriSuffix, StringComparer.Ordinal)
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsRunnable(Workflow workflow) =>
        workflow.ActionSet.IsEnabled &&
        workflow.ActionSet.ActionItems.Count > 0 &&
        !workflow.HasPlatformCompatibilityIssues;

    private static bool TryReadSettings(
        object? value,
        out UriTriggerSettings settings)
    {
        try
        {
            settings = value switch
            {
                UriTriggerSettings typed => typed,
                JsonElement json => json.Deserialize<UriTriggerSettings>()!,
                _ => null!
            };
            return settings != null;
        }
        catch (JsonException)
        {
            settings = null!;
            return false;
        }
    }

    private static bool TryNormalizeSuffix(string? value, out string suffix)
    {
        suffix = value?.Trim() ?? string.Empty;
        if (suffix.Length == 0 || suffix.Any(character =>
                !IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '/' and not '~'))
        {
            return false;
        }

        var segments = suffix.Split('/', StringSplitOptions.None);
        return segments.All(segment =>
            segment.Length > 0 && segment is not "." and not "..");
    }

    private static bool IsAsciiLetterOrDigit(char character) =>
        character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9';
}
