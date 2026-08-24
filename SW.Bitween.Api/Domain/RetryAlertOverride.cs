using System;
using System.Collections.Generic;
using SW.Bitween.Model;

namespace SW.Bitween.Domain;

/// <summary>
/// The most specific level of the retry-alert hierarchy: where one subscription's failures in one
/// retry group should be alerted, overriding whatever the group or the policy says.
/// </summary>
/// <remarks>
/// Deliberately its own table rather than columns on <see cref="RetryGroupUsage"/>. Usage rows are
/// deleted by <c>RetryPolicies/resetusage</c>, so config stored there would be silently discarded
/// every time someone cleared a spent budget.
/// </remarks>
public class RetryAlertOverride
{
    /// <summary>The subscription this override applies to.</summary>
    public int SubscriptionId { get; set; }

    /// <summary><c>RetryGroup.Id</c>, which survives policy edits, so the override does too.</summary>
    public Guid GroupId { get; set; }

    /// <summary>
    /// Whether this level sends, stays silent, or defers upward. A row whose mode is
    /// <see cref="RetryAlertMode.Inherit"/> is equivalent to having no row at all.
    /// </summary>
    public RetryAlertMode AlertMode { get; set; }

    /// <summary>Adapter that delivers the alert. Required when <see cref="AlertMode"/> is Send.</summary>
    public string AlertHandlerId { get; set; }

    /// <summary>That adapter's own settings — api key, recipients, subject.</summary>
    public IReadOnlyDictionary<string, string> AlertHandlerProperties { get; set; }
}
