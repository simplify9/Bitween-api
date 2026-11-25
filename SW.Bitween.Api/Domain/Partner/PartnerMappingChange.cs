using System;
using System.Collections.Generic;
using System.Linq;

namespace SW.Bitween.Domain;

public enum PartnerMappingChangeStatus : byte
{
    /// <summary>Asked for by a partner whose changes need approval; nothing is used from it yet.</summary>
    Waiting = 1,

    /// <summary>Used at once: the partner's changes don't need approval, or our team made it.</summary>
    Live = 2,

    Approved = 3,
    Rejected = 4,

    /// <summary>Taken back before anyone decided: by the partner, or by a newer change that went live.</summary>
    Withdrawn = 5,
}

/// <summary>
/// One change to a partner's own mapping of a subscription (<see cref="PartnerMapping"/>): what was
/// asked for, by whom, and what became of it. Every change is one, so the rows of a subscription and
/// partner are the history of their mapping, and the waiting ones are what our team approves.
/// </summary>
/// <remarks>
/// <see cref="MapperProperties"/> holds what the partner's mapping becomes: the rules, as
/// <see cref="PartnerMapping"/> keeps them, or nothing when the change is going back to ours.
/// </remarks>
public class PartnerMappingChange
{
    private PartnerMappingChange()
    {
    }

    public PartnerMappingChange(int subscriptionId, int partnerId, IDictionary<string, string> mapperProperties,
        PartnerMappingChangeStatus status, string requestedBy, string note = null)
    {
        SubscriptionId = subscriptionId;
        PartnerId = partnerId;
        MapperProperties = (mapperProperties ?? new Dictionary<string, string>()).ToDictionary();
        Status = status;
        RequestedBy = requestedBy;
        RequestedOn = DateTime.UtcNow;
        Note = note;
    }

    public int Id { get; private set; }
    public int SubscriptionId { get; private set; }
    public int PartnerId { get; private set; }

    /// <summary>What the partner's mapping becomes; empty when it goes back to ours.</summary>
    public IReadOnlyDictionary<string, string> MapperProperties { get; private set; } = new Dictionary<string, string>();

    public bool BackToOurs => MapperProperties.Count == 0;

    public PartnerMappingChangeStatus Status { get; private set; }

    // Decided only by PartnerMappingChanges.DecideAsync: one statement matching a change still waiting.

    /// <summary>The account that made the change, as the audit trail names accounts.</summary>
    public string RequestedBy { get; private set; }

    public DateTime RequestedOn { get; private set; }
    public string DecidedBy { get; private set; }
    public DateTime? DecidedOn { get; private set; }

    /// <summary>Why it was rejected, or which version it put back.</summary>
    public string Note { get; private set; }
}
