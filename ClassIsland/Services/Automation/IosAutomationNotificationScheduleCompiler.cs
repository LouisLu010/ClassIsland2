using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ClassIsland.Core.Models.Automation;
using ClassIsland.iOS.Services.Notifications;
using ClassIsland.Models.Actions;
using ClassIsland.Models.Automation.Triggers;
using ClassIsland.Platforms.Abstraction.Services;
using TimeCrontab;

namespace ClassIsland.Services.Automation;

internal static class IosAutomationNotificationScheduleCompiler
{
    internal const string IdentifierPrefix = "classisland.automation.";
    internal const string CategoryIdentifier = "classisland.automation";
    private const string CronTriggerId = "classisland.cron";
    private const string NotificationActionId = "classisland.showNotification";
    private static readonly Guid ActionNotificationChannelId =
        Guid.Parse("4B12F124-8585-43C7-AFC5-7BBB7CBE60D6");

    public static IReadOnlyList<IosLessonNotificationRequest> Compile(
        IEnumerable<Workflow> workflows,
        DateTime logicalNow,
        DateTimeOffset systemNow,
        DateTime logicalUntilExclusive,
        int maximumRequestCount,
        bool allowNotificationSound)
    {
        ArgumentNullException.ThrowIfNull(workflows);
        if (logicalUntilExclusive <= logicalNow)
        {
            throw new ArgumentOutOfRangeException(nameof(logicalUntilExclusive));
        }
        if (maximumRequestCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumRequestCount));
        }
        if (maximumRequestCount == 0)
        {
            return [];
        }

        var requests = new List<IosLessonNotificationRequest>();
        foreach (var workflow in workflows)
        {
            if (!TryGetNativeSafeDefinition(
                    workflow,
                    out var cronSettings,
                    out var notificationSettings))
            {
                continue;
            }

            Crontab crontab;
            try
            {
                crontab = Crontab.Parse(cronSettings.CronExpression);
            }
            catch (TimeCrontabException)
            {
                continue;
            }

            var logicalOccurrence = logicalNow;
            var workflowRequestCount = 0;
            while (workflowRequestCount < maximumRequestCount)
            {
                var nextOccurrence = crontab.GetNextOccurrence(logicalOccurrence);
                if (nextOccurrence <= logicalOccurrence ||
                    nextOccurrence >= logicalUntilExclusive)
                {
                    break;
                }

                var fireAt = IosNotificationTimeMapper.ToSystemTime(
                    nextOccurrence,
                    logicalNow,
                    systemNow);
                for (var actionIndex = 0;
                     actionIndex < notificationSettings.Count &&
                     workflowRequestCount < maximumRequestCount;
                     actionIndex++)
                {
                    var settings = notificationSettings[actionIndex];
                    var payload = IosFallbackNotificationPayloadPolicy.Create(
                        "行动提醒",
                        [new IosFallbackNotificationTextEntry(settings.Mask, settings.Content)]);
                    requests.Add(new IosLessonNotificationRequest(
                        $"{IdentifierPrefix}{workflow.ActionSet.Guid:N}." +
                        $"{actionIndex:D2}.{fireAt.ToUnixTimeSeconds()}",
                        fireAt,
                        payload.Title,
                        payload.Body,
                        ActionNotificationChannelId,
                        allowNotificationSound && settings.IsSoundEffectEnabled,
                        CategoryIdentifier: CategoryIdentifier));
                    workflowRequestCount++;
                }

                logicalOccurrence = nextOccurrence;
            }
        }

        return requests
            .OrderBy(x => x.FireAt)
            .ThenBy(x => x.Identifier, StringComparer.Ordinal)
            .Take(maximumRequestCount)
            .ToArray();
    }

    private static bool TryGetNativeSafeDefinition(
        Workflow workflow,
        out CronTriggerSettings cronSettings,
        out IReadOnlyList<NotificationActionSettings> notificationSettings)
    {
        cronSettings = null!;
        notificationSettings = [];
        if (!workflow.ActionSet.IsEnabled ||
            workflow.ActionSet.IsRevertEnabled ||
            workflow.IsConditionEnabled ||
            workflow.Triggers.Count != 1 ||
            workflow.ActionSet.ActionItems.Count == 0)
        {
            return false;
        }

        var trigger = workflow.Triggers[0];
        if (!string.Equals(trigger.Id, CronTriggerId, StringComparison.Ordinal) ||
            !TryReadSettings(trigger.Settings, out cronSettings) ||
            string.IsNullOrWhiteSpace(cronSettings.CronExpression))
        {
            return false;
        }

        var actionSettings = new List<NotificationActionSettings>(
            workflow.ActionSet.ActionItems.Count);
        foreach (var action in workflow.ActionSet.ActionItems)
        {
            if (!string.Equals(action.Id, NotificationActionId, StringComparison.Ordinal) ||
                !TryReadSettings(action.Settings, out NotificationActionSettings? settings) ||
                settings.IsWaitForCompleteEnabled)
            {
                return false;
            }

            actionSettings.Add(settings);
        }

        notificationSettings = actionSettings;
        return true;
    }

    private static bool TryReadSettings<T>(object? value, out T settings)
        where T : class
    {
        try
        {
            settings = value switch
            {
                T typed => typed,
                JsonElement json => json.Deserialize<T>()!,
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
}
