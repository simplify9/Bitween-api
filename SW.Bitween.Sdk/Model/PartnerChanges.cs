using System;
using System.Collections.Generic;

namespace SW.Bitween.Model;

/// <summary>A change a partner made to their own mapping of a subscription, or one our team made to it.</summary>
public class PartnerChangeRow
{
    public int Id { get; set; }

    /// <summary>mapping, a change to their own mapping of a subscription; or properties, to their own properties.</summary>
    public string Kind { get; set; } = "mapping";

    /// <summary>The subscription a mapping change is for; null for a change to their properties.</summary>
    public int? SubscriptionId { get; set; }

    public string? SubscriptionName { get; set; }
    public int PartnerId { get; set; }
    public string PartnerName { get; set; } = null!;

    /// <summary>waiting, live, approved, rejected or withdrawn.</summary>
    public string Status { get; set; } = null!;

    /// <summary>The change drops their own mapping, so they get the subscription's again.</summary>
    public bool BackToOurs { get; set; }

    public DateTime RequestedOn { get; set; }

    /// <summary>By name; null when that account is gone.</summary>
    public string? RequestedBy { get; set; }

    public DateTime? DecidedOn { get; set; }
    public string? DecidedBy { get; set; }

    /// <summary>Why it was rejected, or which version it put back.</summary>
    public string? Note { get; set; }
}

/// <summary>One change, with the rules it asks for beside the ones used for that partner now.</summary>
public class PartnerChangeDetail : PartnerChangeRow
{
    /// <summary>The rules the change asks for; empty when it goes back to ours.</summary>
    public string MappingRules { get; set; } = "";

    /// <summary>The rules used for the partner now: their own, or the subscription's.</summary>
    public string CurrentRules { get; set; } = "";

    /// <summary>What is used for them now is the subscription's own mapping.</summary>
    public bool CurrentIsOurs { get; set; }

    /// <summary>Why it can't be approved as it stands, or null when it can.</summary>
    public string? Problem { get; set; }
}

/// <summary>A change in a partner's mapping history, with its rules.</summary>
public class PartnerChangeVersion : PartnerChangeRow
{
    public string MappingRules { get; set; } = "";

    /// <summary>It was used for them once, and has rules to put back.</summary>
    public bool CanPutBack { get; set; }
}

/// <summary>A change to a partner's properties, beside what they are now.</summary>
public class PartnerPropertyChangeDetail : PartnerChangeRow
{
    public List<PartnerPropertyChangeItem> Items { get; set; } = [];

    /// <summary>Why it can't be approved as it stands, or null when it can.</summary>
    public string? Problem { get; set; }
}

public class PartnerPropertyChangeItem
{
    public string Key { get; set; } = null!;

    /// <summary>Its value now and what the change makes it; null for a secret one, never shown.</summary>
    public string? Current { get; set; }
    public string? Proposed { get; set; }

    public bool Secret { get; set; }
}

public class PartnerChangeSearch
{
    /// <summary>True for the changes waiting for approval, oldest first; false for the latest decided.</summary>
    public bool Waiting { get; set; } = true;
}

public class PartnerChangeHistoryRequest
{
    public int SubscriptionId { get; set; }
    public int PartnerId { get; set; }
}

public class PartnerChangeDecision
{
    /// <summary>Why, when rejecting; the partner sees it.</summary>
    public string? Note { get; set; }
}

public class PartnerApprovalUpdate
{
    public bool ChangesNeedApproval { get; set; }
}
