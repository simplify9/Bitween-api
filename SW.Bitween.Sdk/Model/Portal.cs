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




public class PortalRetry
{
    public string XchangeId { get; set; } = null!;
}

/// <summary>The exchange a retry made.</summary>
public class PortalRetried
{
    public string XchangeId { get; set; } = null!;
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
/// <summary>A subscription one partner maps their own way, as their partner page lists it.</summary>
public class PartnerOwnMapping
{
    public int SubscriptionId { get; set; }
    public string SubscriptionName { get; set; } = null!;
    /// <summary>Null when only a change of theirs waits, with no mapping of their own in use yet.</summary>
    public DateTime? ChangedOn { get; set; }
    public string? ChangedBy { get; set; }
    public bool InUse { get; set; }
    public bool Waiting { get; set; }
}

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

/// <summary>One of a partner's people, as their admin sees them in the portal.</summary>
public class PortalPerson
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string Email { get; set; } = null!;

    /// <summary>Their one role; null for an account still holding several from before one role per account.</summary>
    public AccountRoleSummary? Role { get; set; }

    public bool Disabled { get; set; }

    /// <summary>Invited and hasn't chosen a password yet.</summary>
    public bool Invited { get; set; }

    /// <summary>Has chosen a password; false while invited, or with no invite that still counts.</summary>
    public bool HasPassword { get; set; }

    public DateTime CreatedOn { get; set; }

    /// <summary>When their latest invite stops working; null when they aren't waiting on one.</summary>
    public DateTime? InviteExpiresOn { get; set; }

    public DateTime? LastSignInOn { get; set; }

    /// <summary>The admin looking at the list.</summary>
    public bool IsYou { get; set; }

    /// <summary>
    /// The admin looking may change them: not themselves, and not another of their admins, which only
    /// our team may.
    /// </summary>
    public bool CanChange { get; set; }
}

/// <summary>A role a partner's admin may give: a partner role, within what they hold, that can't manage people.</summary>
public class PortalRoleChoice
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public bool BuiltIn { get; set; }
}

/// <summary>One of the roles a partner's admin sees: ours for partners, built-in ones, and their own.</summary>
public class PortalRoleRequest
{
    public int Id { get; set; }
}

/// <summary>A role a partner's admin makes or changes for their own company's people.</summary>
public class PortalRoleSave
{
    /// <summary>Set to change one of their own roles; left out to make one.</summary>
    public int? Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public List<string> Permissions { get; set; } = [];
}

public class PortalAddPerson
{
    public string Name { get; set; } = null!;
    public string Email { get; set; } = null!;
    public int RoleId { get; set; }
}

/// <summary>A person added and invited: the invite's outcome, with the link when it wasn't emailed.</summary>
public class PortalPersonAdded : InviteResultModel
{
    public int Id { get; set; }
}

public class PortalPersonRequest
{
    public int AccountId { get; set; }
}

public class PortalPersonRole
{
    public int AccountId { get; set; }
    public int RoleId { get; set; }
}

public class PortalPersonDisabled
{
    public int AccountId { get; set; }
    public bool Disabled { get; set; }
}

/// <summary>One of their partner properties they may see.</summary>
public class PortalProperty
{
    public string Key { get; set; } = null!;

    /// <summary>Its value; null for a secret one, which they may replace but never read.</summary>
    public string? Value { get; set; }

    public bool Secret { get; set; }

    /// <summary>They may change it.</summary>
    public bool Editable { get; set; }
}

/// <summary>Their partner properties, and a change of theirs waiting or rejected.</summary>
public class PortalProperties
{
    public List<PortalProperty> Properties { get; set; } = [];

    /// <summary>Their changes wait for our team's approval before they're used.</summary>
    public bool NeedsApproval { get; set; }

    /// <summary>Their change waiting for approval: the names it changes, and when they asked.</summary>
    public PortalPropertiesWaiting? Waiting { get; set; }

    /// <summary>Their latest change, when it was rejected and nothing came after it.</summary>
    public PortalMappingRejected? Rejected { get; set; }
}

public class PortalPropertiesWaiting
{
    /// <summary>The new values by name; a secret one as null.</summary>
    public Dictionary<string, string?> Values { get; set; } = new();

    public DateTime RequestedOn { get; set; }
}

public class PortalSaveProperties
{
    /// <summary>New values by name, only properties they may change. A secret one left empty is kept.</summary>
    public Dictionary<string, string> Values { get; set; } = new();
}

/// <summary>No body: what is taken back is the caller's own waiting change.</summary>
public class PortalWithdrawProperties
{
}

/// <summary>An information type of what runs for them, for their filters: the names of what it promotes, not where they're read from.</summary>
public class PortalInformationType
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Code { get; set; }
    public List<string> PromotedPropertyKeys { get; set; } = [];
}
