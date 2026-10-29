using System;
using System.Collections.Generic;

namespace SW.Bitween.Model;

/// <summary>
/// One named piece of SQL a data source may run. A subscription names it; the SQL itself never
/// travels with a message, and never sits in a subscription's adapter properties where partner
/// values would be templated into it.
/// </summary>
public class DataSourceStatementCreate : IName
{
    /// <summary>
    /// Which connection it belongs to. In the body rather than the route because the resource is
    /// addressed as /datasourcestatements - a keyed create would collide with the update handler,
    /// which is what POST /datasourcestatements/{id} already means.
    /// </summary>
    public int DataSourceId { get; set; }

    /// <summary>Required; the server rejects a create without it.</summary>
    public string Name { get; set; } = null!;

    /// <summary>The SQL, or a procedure name for a statement meant to be called.</summary>
    public string Sql { get; set; } = null!;

    /// <summary>Why it exists, for whoever inherits it.</summary>
    public string? Description { get; set; }

    /// <summary>Which team to ask before changing it. Null means unowned.</summary>
    public int? WorkGroupId { get; set; }

    /// <summary>
    /// Kept out of the composed statement set without being deleted. A subscription naming an
    /// inactive statement fails loudly, which is the point: retiring is meant to be noticed.
    /// </summary>
    public bool Inactive { get; set; }

    /// <summary>
    /// Only for a statement a receiver polls with: the column carrying the cursor - the
    /// incrementing id or the modified-at timestamp. It describes what this query returns, so it
    /// belongs to the statement rather than to each subscription reading it.
    /// </summary>
    public string? CursorColumn { get; set; }

    /// <summary>
    /// Only for a polled statement: the column identifying a row, for mark-processed and for
    /// deduplication.
    /// </summary>
    public string? KeyColumn { get; set; }
}

public class DataSourceStatementUpdate : DataSourceStatementCreate
{
}

public class DataSourceStatementRow : DataSourceStatementUpdate
{
    public int Id { get; set; }
    public int DataSourceId { get; set; }

    /// <summary>Null when unowned.</summary>
    public string? WorkGroupName { get; set; }

    /// <summary>
    /// How many subscriptions name this statement. Zero is the interesting value - it is the only
    /// reliable way to tell dead SQL from SQL that is merely quiet, and deleting is refused above
    /// zero.
    /// </summary>
    public int UsageCount { get; set; }

    public DateTime CreatedOn { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
    public string? ModifiedBy { get; set; }
}

/// <summary>
/// Nothing to send: the statement id in the route is the whole question. It exists because a keyed
/// command takes a body, and asking what uses a statement supplies nothing.
/// </summary>
public class DataSourceStatementUsageRequest
{
}

/// <summary>Which subscriptions name a statement, and in which adapter slot.</summary>
public class DataSourceStatementUsage
{
    public int StatementId { get; set; }
    public string Name { get; set; } = null!;
    public List<DataSourceStatementUsageEntry> UsedBy { get; set; } = new();
}

public class DataSourceStatementUsageEntry
{
    public int SubscriptionId { get; set; }
    public string SubscriptionName { get; set; } = null!;

    /// <summary>Handler, Mapper or Receiver - which slot's properties name it.</summary>
    public string Role { get; set; } = null!;

    /// <summary>What that slot will do with it: query, execute or call.</summary>
    public string Operation { get; set; } = null!;

    public bool Inactive { get; set; }
}
