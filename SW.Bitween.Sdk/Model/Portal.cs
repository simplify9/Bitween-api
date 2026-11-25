using System;
using System.Collections.Generic;

namespace SW.Bitween.Model;

/// <summary>A subscription as the partner portal shows it: what it is, never how it is set up.</summary>
public class PortalSubscriptionRow
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string Type { get; set; } = null!;
    public string DocumentName { get; set; } = null!;
    public bool Paused { get; set; }
}

public class PortalXchangeSearch
{
    /// <summary>running, success or failed; anything else, or nothing, is every exchange.</summary>
    public string? Status { get; set; }

    public int? SubscriptionId { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }

    /// <summary>Zero-based.</summary>
    public int Page { get; set; }

    public int Size { get; set; } = 25;
}

/// <summary>
/// One page of exchanges, newest first. No total: counting every exchange a partner ever had costs
/// far more than the page does, so the page says only whether there is another.
/// </summary>
public class PortalXchangePage
{
    public List<PortalXchangeRow> Result { get; set; } = [];
    public bool HasMore { get; set; }
}

public class PortalXchangeRow
{
    public string Id { get; set; } = null!;
    public DateTime StartedOn { get; set; }
    public DateTime? FinishedOn { get; set; }

    /// <summary>running, success or failed.</summary>
    public string Status { get; set; } = null!;

    public string DocumentName { get; set; } = null!;
    public int? SubscriptionId { get; set; }
    public string? SubscriptionName { get; set; }

    /// <summary>Why it failed; only with the exchange errors permission.</summary>
    public string? Error { get; set; }

    /// <summary>Its files; only with the exchange files permission, and only the ones it has.</summary>
    public List<PortalXchangeFile>? Files { get; set; }
}

public class PortalXchangeFile
{
    /// <summary>input, output or response.</summary>
    public string Kind { get; set; } = null!;

    public string? Name { get; set; }
    public int Size { get; set; }

    /// <summary>Opens the file without signing in; given only for the partner's own exchanges.</summary>
    public string Url { get; set; } = null!;
}

/// <summary>The last 24 hours of the partner's exchanges, by outcome.</summary>
public class PortalSummary
{
    public string PartnerName { get; set; } = null!;
    public int Running { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
}

/// <summary>A place a partner is connected, and whether they map it their own way there.</summary>
public class PortalMappingPlace
{
    public int SubscriptionId { get; set; }
    public string SubscriptionName { get; set; } = null!;

    /// <summary>How they are connected: their own subscription, an API gateway, a bus gateway route.</summary>
    public string Via { get; set; } = null!;

    public string DocumentName { get; set; } = null!;

    /// <summary>They have their own version of the mapping.</summary>
    public bool Customized { get; set; }

    /// <summary>
    /// Theirs reads an internal value ours no longer sends, so ours is used until they change theirs.
    /// </summary>
    public bool SetAside { get; set; }

    /// <summary>It maps with a mapper they can make their own version of.</summary>
    public bool Customizable { get; set; }

    /// <summary>A change of theirs waits for our team to approve it.</summary>
    public bool Waiting { get; set; }

    public DateTime? ModifiedOn { get; set; }
}

public class PortalMappingRequest
{
    public int SubscriptionId { get; set; }
}

/// <summary>A place's mapping: ours, and theirs when they have one.</summary>
public class PortalMapping
{
    public int SubscriptionId { get; set; }
    public string SubscriptionName { get; set; } = null!;
    public bool Customizable { get; set; }

    /// <summary>
    /// The subscription's own mapping rules, the starting point for theirs; null when it doesn't map
    /// or maps with another mapper. Its samples are left out: they may be another partner's data.
    /// </summary>
    public Dictionary<string, string>? Default { get; set; }

    /// <summary>Their own version, or null when they use ours.</summary>
    public Dictionary<string, string>? Theirs { get; set; }

    /// <summary>See <see cref="PortalMappingPlace.SetAside"/>.</summary>
    public bool SetAside { get; set; }

    /// <summary>Their changes wait for our team to approve them before they are used.</summary>
    public bool NeedsApproval { get; set; }

    /// <summary>Their change waiting for approval, or null.</summary>
    public PortalMappingWaiting? Waiting { get; set; }

    /// <summary>Their latest change, when it was rejected and nothing came after it; otherwise null.</summary>
    public PortalMappingRejected? Rejected { get; set; }
}

public class PortalMappingWaiting
{
    /// <summary>What theirs becomes once approved; null when the change is going back to ours.</summary>
    public Dictionary<string, string>? Theirs { get; set; }

    public DateTime RequestedOn { get; set; }
}

public class PortalMappingRejected
{
    /// <summary>Why, in our team's words; may be empty.</summary>
    public string? Note { get; set; }

    public DateTime DecidedOn { get; set; }
}

/// <summary>What became of a change: used from their next exchange, or waiting for approval.</summary>
public class PortalMappingSaved
{
    public bool Waiting { get; set; }
}

public class PortalSaveMapping
{
    public int SubscriptionId { get; set; }

    /// <summary>The native mapper's settings: MappingRules, SourceSample, TargetSample.</summary>
    public Dictionary<string, string> MapperProperties { get; set; } = new();
}

public class PortalPreviewMapping
{
    public int SubscriptionId { get; set; }
    public string MappingRules { get; set; } = "";
    public string SourceDocument { get; set; } = "";
}

/// <summary>One of their recent exchanges through a place, to use as the mapping's sample.</summary>
public class PortalMappingSample
{
    public string XchangeId { get; set; } = null!;
    public DateTime StartedOn { get; set; }
    public string? InputName { get; set; }
}

/// <summary>A partner using their own mapping of a subscription, as the subscription's page lists them.</summary>
public class SubscriptionPartnerMapping
{
    public int PartnerId { get; set; }
    public string PartnerName { get; set; } = null!;

    /// <summary>Their rules, as the mapping editor writes them.</summary>
    public string MappingRules { get; set; } = "";

    public DateTime ChangedOn { get; set; }

    /// <summary>Who last changed it, by name; null when that account is gone.</summary>
    public string? ChangedBy { get; set; }

    /// <summary>
    /// It reads a global value the subscription's mapping no longer sends, so the subscription's is
    /// used for them until they change theirs.
    /// </summary>
    public bool SetAside { get; set; }

    /// <summary>
    /// They use their own mapping now. False for a partner listed only because a change of theirs
    /// waits for approval, or for the history of the one they had.
    /// </summary>
    public bool InUse { get; set; }

    /// <summary>Their change waiting for approval, or null.</summary>
    public int? WaitingChangeId { get; set; }
}

public class RemovePartnerMappingModel
{
    public int PartnerId { get; set; }
}
