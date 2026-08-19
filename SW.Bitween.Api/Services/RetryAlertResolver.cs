using System.Collections.Generic;
using SW.Bitween.Domain;
using SW.Bitween.Model;

namespace SW.Bitween;

/// <summary>Where a resolved alert should be delivered, and which level of the hierarchy decided it.</summary>
public class RetryAlertTarget
{
    public required string HandlerId { get; init; }
    public IReadOnlyDictionary<string, string> HandlerProperties { get; init; }

    /// <summary>Which level won — shown in the UI so a surprising destination can be traced.</summary>
    public required RetryAlertLevel Level { get; init; }
}

/// <summary>
/// Resolves where a group's exhaustion alert goes, walking from the most specific level to the
/// least: the subscription+group override, then the group, then the policy.
/// </summary>
/// <remarks>
/// <para>
/// A level that overrides <strong>replaces</strong> the level above rather than merging into it, so
/// whichever level wins must carry the handler and every property it needs. That keeps what the UI
/// shows for a level identical to what actually gets sent.
/// </para>
/// <para>
/// Resolved at send time rather than stored, so editing a policy's default immediately affects
/// everything still inheriting it.
/// </para>
/// </remarks>
public static class RetryAlertResolver
{
    /// <summary>
    /// Returns the destination for one subscription's alert in one group, or <c>null</c> when no
    /// level configures one or a level explicitly silences it.
    /// </summary>
    /// <param name="subscriptionOverride">The subscription+group override, or <c>null</c> if none exists.</param>
    /// <param name="group">The matched group. <c>null</c> when the group no longer exists in the policy.</param>
    /// <param name="policy">
    /// The named policy, or <c>null</c> when the subscription uses an inline
    /// <see cref="CustomRetryPolicy"/> — those have no policy row, so only the group and the
    /// override levels can configure an alert.
    /// </param>
    public static RetryAlertTarget Resolve(RetryAlertOverride subscriptionOverride, RetryGroup group,
        RetryPolicy policy)
    {
        switch (subscriptionOverride?.AlertMode)
        {
            case RetryAlertMode.Silent:
                return null;
            case RetryAlertMode.Send when !string.IsNullOrWhiteSpace(subscriptionOverride.AlertHandlerId):
                return new RetryAlertTarget
                {
                    HandlerId = subscriptionOverride.AlertHandlerId,
                    HandlerProperties = subscriptionOverride.AlertHandlerProperties,
                    Level = RetryAlertLevel.SubscriptionGroup
                };
        }

        switch (group?.AlertMode)
        {
            case RetryAlertMode.Silent:
                return null;
            case RetryAlertMode.Send when !string.IsNullOrWhiteSpace(group.AlertHandlerId):
                return new RetryAlertTarget
                {
                    HandlerId = group.AlertHandlerId,
                    HandlerProperties = group.AlertHandlerProperties,
                    Level = RetryAlertLevel.Group
                };
        }

        if (!string.IsNullOrWhiteSpace(policy?.AlertHandlerId))
            return new RetryAlertTarget
            {
                HandlerId = policy.AlertHandlerId,
                HandlerProperties = policy.AlertHandlerProperties,
                Level = RetryAlertLevel.Policy
            };

        return null;
    }
}
