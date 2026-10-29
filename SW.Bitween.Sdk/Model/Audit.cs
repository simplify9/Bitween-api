using System;
using System.Collections.Generic;

namespace SW.Bitween.Model;

/// <summary>
/// Filters for the audit trail. Every field is optional; with none set the result is the whole
/// trail, newest first.
/// </summary>
public class SearchAuditModel
{
    public int? Limit { get; set; }
    public int? Offset { get; set; }

    /// <summary>Narrows to one kind of entity, e.g. <c>Subscription</c>.</summary>
    public string? EntityName { get; set; }

    /// <summary>With <see cref="EntityName"/>, the history of one row.</summary>
    public string? EntityKey { get; set; }

    /// <summary>The account behind the change.</summary>
    public string? UserId { get; set; }

    /// <summary>Everything one save changed, as a group.</summary>
    public string? CorrelationId { get; set; }

    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}

public class AuditEntryModel
{
    public string Id { get; set; } = null!;
    public string CorrelationId { get; set; } = null!;
    public int Sequence { get; set; }
    public DateTime OccurredOn { get; set; }

    public string? UserId { get; set; }

    /// <summary>
    /// The account's display name at the time it is read, or null for a change made with no signed-in
    /// user - a bus consumer or a scheduled job. Resolved on read rather than stored, so it is
    /// blank rather than wrong once an account is deleted.
    /// </summary>
    public string? UserDisplayName { get; set; }

    public string EntityName { get; set; } = null!;
    public string EntityKey { get; set; } = null!;

    /// <summary><c>Added</c>, <c>Modified</c> or <c>Deleted</c>.</summary>
    public string State { get; set; } = null!;

    /// <summary>Property name to its before/after values.</summary>
    public Dictionary<string, AuditChangeModel> Changes { get; set; } = new();
}

public class AuditChangeModel
{
    public object? Old { get; set; }
    public object? New { get; set; }
}
