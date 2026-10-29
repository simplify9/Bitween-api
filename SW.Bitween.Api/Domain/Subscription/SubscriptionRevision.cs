using System;

namespace SW.Bitween.Domain;

/// <summary>
/// A subscription's configuration as one save left it: what its history lists, compares and restores.
/// </summary>
/// <remarks>
/// The configuration as the API reads it, so a secret is the <c>__private__</c> sentinel, never its
/// value: restoring a revision keeps the secrets the subscription has now. Running state (failures,
/// pauses, last runs) is left out, since it isn't something a person chose.
/// </remarks>
public class SubscriptionRevision
{
    private SubscriptionRevision()
    {
    }

    public SubscriptionRevision(int subscriptionId, int number, string snapshot, string savedBy)
    {
        SubscriptionId = subscriptionId;
        Number = number;
        Snapshot = snapshot;
        SavedBy = savedBy;
        SavedOn = DateTime.UtcNow;
    }

    public long Id { get; private set; }
    public int SubscriptionId { get; private set; }

    /// <summary>1 for the first save recorded, counting up.</summary>
    public int Number { get; private set; }

    /// <summary>The configuration, as camel-cased JSON in the API's own shape.</summary>
    public string Snapshot { get; private set; }

    /// <summary>The account that saved it, as the audit trail names accounts.</summary>
    public string SavedBy { get; private set; }

    public DateTime SavedOn { get; private set; }
}
