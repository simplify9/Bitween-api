namespace SW.Bitween.Model;

/// <summary>
/// Whether a level of the alert hierarchy defines its own destination for
/// "retry budget exhausted" alerts, or defers to the level above it.
/// </summary>
/// <remarks>
/// The hierarchy is resolved per failing subscription and group, most specific first:
/// the subscription+group override, then the group, then the policy. An overriding level
/// <strong>replaces</strong> the level above rather than merging into it, so the handler and
/// every property it needs must be present on whichever level wins.
/// </remarks>
public enum RetryAlertMode
{
    /// <summary>Defer to the level above. The default, so existing policies keep behaving as before.</summary>
    Inherit,

    /// <summary>Send through this level's own handler, ignoring anything configured above it.</summary>
    Send,

    /// <summary>Send nothing, and stop the walk — an alert configured above is deliberately suppressed here.</summary>
    Silent,
}

/// <summary>
/// Which level of the hierarchy decided where an alert goes. Surfaced in the management UI so a
/// destination that looks wrong can be traced to the level that set it.
/// </summary>
public enum RetryAlertLevel
{
    /// <summary>An override for this one subscription and group.</summary>
    SubscriptionGroup,

    /// <summary>The group's own setting, applying to every subscription using the policy.</summary>
    Group,

    /// <summary>The policy default.</summary>
    Policy,
}
