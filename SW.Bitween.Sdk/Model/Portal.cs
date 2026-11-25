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
