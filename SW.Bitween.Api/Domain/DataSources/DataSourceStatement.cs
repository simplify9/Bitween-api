using SW.PrimitiveTypes;
using System;

namespace SW.Bitween.Domain.DataSources;

/// <summary>
/// One named piece of SQL a data source is allowed to run.
///
/// This is deliberately an entity rather than a JSON blob on <see cref="DataSource"/>, and the
/// reason is permissions before anything else. Statements have to live on the connection - the
/// alternative, SQL in a subscription's adapter properties, is a live injection surface, because
/// those property values have <c>{{partner.X}}</c> substituted into them before the adapter ever
/// sees them, and partner records are ordinary data. But putting the SQL in a field on the data
/// source meant that adding a statement required the same right as changing the credentials, so
/// anyone configuring their own integration needed power over the connection.
///
/// Separating it fixes three more things that were only going to get worse:
///
/// * <b>Contention.</b> One blob shared by every subscription on that database is two teams editing
///   one field, where a bad edit fails the connection test for everyone.
/// * <b>Namespacing.</b> A unique index on (data source, name) makes a collision an error at the
///   point of saving rather than a silent overwrite.
/// * <b>Dead SQL.</b> A statement nothing references is now a countable fact - see the usage
///   endpoint - instead of a line nobody dares delete.
///
/// What does NOT change is the adapter contract. The supervisor composes these rows into the same
/// <c>Statements</c> JSON the adapter has always received, so an adapter still resolves a name and
/// knows nothing about where the SQL was kept.
/// </summary>
public class DataSourceStatement : BaseEntity, IAudited
{
    public int DataSourceId { get; set; }
    public DataSource DataSource { get; set; }

    /// <summary>
    /// What a subscription names to run it. Unique within the data source, case-insensitively -
    /// the adapter resolves names that way, so allowing <c>getOrder</c> and <c>GetOrder</c> to
    /// coexist would make which one runs a matter of dictionary ordering.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// The SQL, or a procedure name for a statement meant to be called. Never templated by Bitween:
    /// it goes to the driver as written, with values bound as parameters.
    /// </summary>
    public string Sql { get; set; }

    /// <summary>Why it exists, for whoever inherits it. Optional and worth writing.</summary>
    public string Description { get; set; }

    /// <summary>
    /// Which team owns it. The point of an owner is that a shared database stops being a shared
    /// blob: a statement has someone to ask before it is changed or deleted. Null means unowned,
    /// which is what every statement created before anyone cared will be.
    /// </summary>
    public int? WorkGroupId { get; set; }

    /// <summary>
    /// Kept out of the composed statement set without being deleted - the same idea as
    /// <see cref="DataSource.Inactive"/>. Useful for retiring a statement while the subscriptions
    /// that used it are still being migrated: they fail loudly on a missing name rather than
    /// quietly running SQL nobody meant to keep.
    /// </summary>
    public bool Inactive { get; set; }

    /// <summary>
    /// For a statement a receiver polls with: the column carrying the cursor - the incrementing
    /// id, or the modified-at timestamp. Its value in the last row read is what gets saved.
    ///
    /// It lives here rather than on the subscription because it describes the SHAPE of what this
    /// query returns, not a choice the reader makes. <c>ordersOutbox</c> returns a
    /// <c>modified_at</c> whoever reads it, and two subscriptions each nominating their own cursor
    /// column is two chances to nominate the wrong one, with nothing to check them against.
    ///
    /// Null on the ordinary statements, which are never polled.
    /// </summary>
    public string CursorColumn { get; set; }

    /// <summary>
    /// For a polled statement: the column identifying a row, used for mark-processed and for
    /// deduplication. Here for the same reason as <see cref="CursorColumn"/> - it is a fact about
    /// the query's result, not about who reads it.
    /// </summary>
    public string KeyColumn { get; set; }

    public DateTime CreatedOn { get; set; }
    public string CreatedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
    public string ModifiedBy { get; set; }
}
