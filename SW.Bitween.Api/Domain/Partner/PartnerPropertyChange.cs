using System;
using System.Collections.Generic;
using System.Linq;

namespace SW.Bitween.Domain;

/// <summary>
/// A change a partner's people made to their own partner properties in the portal: the new values,
/// by name, and what became of it. Waits for our team when their changes need approval
/// (<see cref="Partner.ChangesNeedApproval"/>), and is used at once otherwise.
/// </summary>
/// <remarks>
/// Never edited once made: a newer ask is a row of its own and takes back the one waiting before it,
/// so approving applies exactly what the reviewer read. The values are encrypted like the partner's
/// own, since a secret one may be among them.
/// </remarks>
public class PartnerPropertyChange
{
    private PartnerPropertyChange()
    {
    }

    public PartnerPropertyChange(int partnerId, IDictionary<string, string> values, PartnerChangeStatus status,
        string requestedBy)
    {
        PartnerId = partnerId;
        Values = (values ?? new Dictionary<string, string>()).ToDictionary();
        Status = status;
        RequestedBy = requestedBy;
        RequestedOn = DateTime.UtcNow;
    }

    public int Id { get; private set; }
    public int PartnerId { get; private set; }

    /// <summary>The properties it changes, by name, to their new values.</summary>
    public IReadOnlyDictionary<string, string> Values { get; private set; } = new Dictionary<string, string>();

    // Decided only by PartnerPropertyChanges.DecideAsync: one statement matching a change still waiting.
    public PartnerChangeStatus Status { get; private set; }

    public string RequestedBy { get; private set; }
    public DateTime RequestedOn { get; private set; }
    public string DecidedBy { get; private set; }
    public DateTime? DecidedOn { get; private set; }

    /// <summary>Why it was rejected.</summary>
    public string Note { get; private set; }
}
