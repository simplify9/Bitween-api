using System;
using System.Collections.Generic;
using System.Linq;
using SW.Bitween.Model;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.RetryPolicies;

/// <summary>
/// Rejects retry groups that could never fire. The evaluator skips such groups silently
/// (matchers must support the result type being evaluated — see
/// <see cref="Matcher.Supports"/>), which reads as "retries just don't work", so the
/// misconfiguration is caught at write time instead.
/// </summary>
public static class RetryGroupValidation
{
    public static void EnsureCanFire(IEnumerable<RetryGroup> groups)
    {
        foreach (var group in groups ?? [])
        {
            if ((group.AppliesTo?.Count ?? 0) == 0)
                throw new SWValidationException("RETRY_GROUP_NO_RESULT_TYPE",
                    $"Group '{group.Name}' applies to no result type, so it would never be evaluated. " +
                    "Select Error, Bad result, or both.");

            if ((group.Matchers?.Count ?? 0) == 0)
                throw new SWValidationException("RETRY_GROUP_NO_MATCHERS",
                    $"Group '{group.Name}' has no matchers, so it would never match a failure. " +
                    "Add at least one matcher.");

            foreach (var resultType in group.AppliesTo)
                if (!group.Matchers.Any(m => m.Supports(resultType)))
                    throw new SWValidationException("RETRY_GROUP_INCOMPATIBLE_MATCHERS",
                        $"Group '{group.Name}' applies to {resultType} but none of its matchers can be " +
                        $"evaluated against {resultType} content. {SupportedMatchersFor(resultType)}");

            // Allow with no budget has nothing to work from — no per-message cap, no total, no delay —
            // so the evaluator can only refuse it. Caught here because a group saved that way silently
            // stops retrying, which reads as the whole feature being broken.
            if (group.Action == RetryAction.Allow && group.Budget == null)
                throw new SWValidationException("RETRY_GROUP_ALLOW_WITHOUT_BUDGET",
                    $"Group '{group.Name}' allows retries but has no budget. Set the attempt caps and " +
                    "delay, or change the action to block.");

            // An overriding level replaces the one above it rather than merging into it, so a group
            // set to Send with no channel would silence the policy's alert instead of redirecting it.
            if (group.AlertMode == RetryAlertMode.Send && group.AlertChannelId == null)
                throw new SWValidationException("RETRY_GROUP_ALERT_NO_CHANNEL",
                    $"Group '{group.Name}' is set to send its own budget alert but has no channel. " +
                    "Choose a notification channel, or set the alert back to inherit.");

            EnsureNoAlertHandler(group.AlertHandlerId);
        }
    }

    /// <summary>
    /// Rejects an alert override that claims to send but names nothing to send with — the same trap
    /// as <see cref="EnsureCanFire"/> guards at group level.
    /// </summary>
    public static void EnsureAlertCanSend(RetryAlertMode mode, int? channelId)
    {
        if (mode == RetryAlertMode.Send && channelId == null)
            throw new SWValidationException("RETRY_ALERT_NO_CHANNEL",
                "This override is set to send its own budget alert but has no channel. " +
                "Choose a notification channel, or set it back to inherit.");
    }

    /// <summary>
    /// Refuses the handler a group used to carry. Alerts go through a channel now, and a handler
    /// accepted here would be stored and never used.
    /// </summary>
    private static void EnsureNoAlertHandler(string handlerId)
    {
        if (!string.IsNullOrWhiteSpace(handlerId))
            throw new SWValidationException("RETRY_ALERT_HANDLER_RETIRED",
                "Alerts are sent through notification channels now. Set alertChannelId instead of " +
                "alertHandlerId.");
    }

    /// <summary>Refuses a channel id that names no channel.</summary>
    public static async Task EnsureAlertChannelsExist(BitweenDbContext dbContext, params int?[] channelIds)
    {
        var wanted = channelIds.Where(id => id != null).Select(id => id!.Value).Distinct().ToList();
        if (wanted.Count == 0) return;

        var found = await dbContext.Set<NotificationChannel>()
            .Where(c => wanted.Contains(c.Id))
            .Select(c => c.Id)
            .ToListAsync();

        var missing = wanted.Except(found).ToList();
        if (missing.Count > 0)
            throw new SWValidationException("NOTIFICATION_CHANNEL_NOT_FOUND",
                $"Notification channel {string.Join(", ", missing)} was not found.");
    }

    /// <summary>Every channel a set of groups sends through.</summary>
    public static int?[] AlertChannelIds(IEnumerable<RetryGroup> groups) =>
        (groups ?? []).Select(g => g.AlertChannelId).ToArray();

    private static string SupportedMatchersFor(XchangeResultType resultType) => resultType switch
    {
        XchangeResultType.Error => "Error supports Contains, Regex and Exception type matchers.",
        XchangeResultType.BadResult => "Bad result supports Contains, Regex and JSON path matchers.",
        _ => "Successful results are never retried."
    };
}
