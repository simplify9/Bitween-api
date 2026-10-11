using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using SW.EfCoreExtensions;
using SW.PrimitiveTypes;

namespace SW.Bitween.Domain;

/// <summary>
/// One configuration change, as the change tracker saw it: which entity, which properties, from what
/// to what, by whom.
/// </summary>
/// <remarks>
/// <para>
/// Rows are written by <see cref="BitweenDbContext.SaveChangesAsync"/> for the entities
/// <see cref="AuditPolicy"/> admits, in the same transaction as the change itself - so an entry
/// exists for every audited change that committed, and for no change that didn't.
/// </para>
/// <para>
/// Nothing edits or deletes an entry. That is the point of the table, and it is why the setters are
/// private and there is no update path anywhere in the code.
/// </para>
/// </remarks>
public class AuditEntry : BaseEntity<string>
{
    private AuditEntry()
    {
    }

    public AuditEntry(GenericAuditDiffJson diff)
    {
        Id = Guid.NewGuid().ToString("N");
        CorrelationId = diff.CorrelationId;
        Sequence = diff.Sequence;
        // The library hands back a DateTimeOffset. Every other timestamp in Bitween is a UTC
        // DateTime, and MySQL has no offset type to store one in, so it is flattened here rather
        // than becoming the one column that behaves differently on one of the three providers.
        OccurredOn = diff.Timestamp.UtcDateTime;
        UserId = diff.UserId;
        EntityName = diff.EntityName;
        EntityKey = FlattenKey(diff.PrimaryKey);
        State = diff.State;
        Changes = JsonConvert.SerializeObject(diff.Changes);
    }

    /// <summary>Shared by every entry written by the same save, so one request reads as one change.</summary>
    public string CorrelationId { get; private set; }

    /// <summary>Position within that save, 1-based. Only meaningful alongside <see cref="CorrelationId"/>.</summary>
    public int Sequence { get; private set; }

    public DateTime OccurredOn { get; private set; }

    /// <summary>
    /// The account id behind the change, or null for a save with no signed-in user - a bus consumer
    /// or a scheduled job. Kept as the id rather than a name so that renaming an account doesn't
    /// rewrite history.
    /// </summary>
    public string UserId { get; private set; }

    /// <summary>The entity's display name, e.g. <c>Subscription</c>.</summary>
    public string EntityName { get; private set; }

    /// <summary>
    /// The primary key as a single value, so the history of one row is an indexed lookup rather than
    /// a JSON scan. Composite keys are joined with <c>|</c> in property-name order.
    /// </summary>
    public string EntityKey { get; private set; }

    /// <summary><c>Added</c>, <c>Modified</c> or <c>Deleted</c>.</summary>
    public string State { get; private set; }

    /// <summary>
    /// The per-property before and after values, as a JSON object of
    /// <c>{ "PropertyName": { "Old": …, "New": … } }</c>. Properties excluded by
    /// <see cref="AuditPolicy"/> never reach it.
    /// </summary>
    public string Changes { get; private set; }

    private static string FlattenKey(object primaryKey) =>
        primaryKey is IDictionary<string, object> values
            ? string.Join("|", values.OrderBy(kv => kv.Key).Select(kv => kv.Value?.ToString()))
            : primaryKey?.ToString();
}
