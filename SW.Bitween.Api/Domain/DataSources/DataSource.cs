using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;

namespace SW.Bitween.Domain.DataSources;

/// <summary>
/// How to reach an external system, separate from what Bitween does with it.
///
/// A <see cref="Gateway.BusGateway"/> with no <c>DataSourceId</c> still means the INTERNAL bus, so
/// every gateway that exists today keeps working untouched. Setting one moves that gateway onto an
/// external broker, served by a resident serverless adapter.
///
/// The split matters: this holds the connection - endpoint, credentials, health - and the gateway
/// holds the meaning - which Document, which routes, which filters. One data source can serve many
/// gateways, exactly as one RabbitMQ connection serves many queues.
/// </summary>
public class DataSource : BaseEntity, IAudited
{
    public string Name { get; set; }

    /// <summary>
    /// The adapter that knows this protocol, e.g. <c>bitween.bus.rabbitmq</c> or
    /// <c>bitween.bus.sqs</c>. Resolved and installed from cloud storage like any other adapter.
    /// </summary>
    public string AdapterId { get; set; }

    /// <summary>Free text for the UI; the adapter is the authority on what it actually speaks.</summary>
    public DataSourceKind Kind { get; set; } = DataSourceKind.Broker;

    /// <summary>
    /// Connection settings handed to the adapter as startup values. Deliberately untyped: a
    /// provider must not be constrained to the subset of a broker's model that Bitween happens to
    /// have modelled. Secrets are protected at rest - see <see cref="SecretProperties"/>.
    /// </summary>
    public Dictionary<string, string> Properties { get; set; } = new();

    /// <summary>
    /// Names within <see cref="Properties"/> whose values are encrypted at rest and never returned
    /// by the API in clear.
    /// </summary>
    public List<string> SecretProperties { get; set; } = new();

    /// <summary>
    /// How many nodes may run this source's adapter at once. Left <see cref="DataSourcePlacement.Auto"/>
    /// it follows from <see cref="Kind"/>, which is right almost always - set it only to override.
    /// </summary>
    public DataSourcePlacement Placement { get; set; } = DataSourcePlacement.Auto;

    /// <summary>Stops the adapter without deleting the configuration, mirroring BusGateway.Inactive.</summary>
    public bool Inactive { get; set; }

    /// <summary>
    /// How long a message's dedupe key is remembered. It has to exceed the widest redelivery
    /// window this broker can actually produce - its message TTL, a dead-letter replay, someone
    /// re-driving a queue by hand - because a key forgotten too early lets a redelivery through
    /// as a fresh message. That number is a property of the customer's broker, not of Bitween,
    /// which is why it lives here rather than in configuration.
    ///
    /// Zero turns deduplication off for this data source.
    /// </summary>
    public int DeduplicationWindowDays { get; set; } = 30;

    /// <summary>
    /// A soft ceiling in megabytes. Crossing it is a signal, not a kill: the host reports it and
    /// the adapter is recycled between messages, so nothing in flight is lost. Zero leaves the
    /// host's own default in place.
    /// </summary>
    public int SoftMemoryLimitMb { get; set; }

    /// <summary>
    /// A hard ceiling in megabytes, enforced by the runtime rather than by the supervisor's
    /// goodwill - it becomes the adapter process's GC heap hard limit, so an allocation past it
    /// fails inside the adapter instead of taking the node down with it. Zero leaves the host's
    /// own default in place.
    ///
    /// This matters because an adapter is a separate process holding a broker connection: without
    /// a ceiling, one customer's runaway payload is bounded by nothing but the host, and every
    /// other integration on the node goes down with it.
    /// </summary>
    public int HardMemoryLimitMb { get; set; }

    /// <summary>
    /// Sustained CPU ceiling for the adapter process, as a percentage of the WHOLE node - the same
    /// figure the heartbeat reports. Worth being exact about, because the intuitive reading is
    /// wrong in an expensive direction: one core pegged flat out on a sixteen-core node reads about
    /// 6%, so a ceiling set at "50%, surely that's half a core" would in fact allow eight.
    ///
    /// Deliberately sustained rather than instantaneous - an adapter draining a backlog is supposed
    /// to work hard, and recycling it for that would be a bug wearing a limit's clothes. Zero
    /// leaves the host default in place.
    /// </summary>
    public double CpuPercentLimit { get; set; }

    /// <summary>
    /// How many consecutive heartbeats above <see cref="CpuPercentLimit"/> before it trips. Zero
    /// uses the host default. Longer means a bigger burst of legitimate work passes underneath it.
    /// </summary>
    public int CpuLimitSamples { get; set; }

    // ---------------------------------------------------------------- health

    /// <summary>Last state the adapter reported on its heartbeat: Connected, Idle, Disconnected...</summary>
    public string LastKnownState { get; set; }

    public DateTime? LastHeartbeatOn { get; set; }
    public string LastException { get; set; }
    public int ConsecutiveFailures { get; set; }

    /// <summary>
    /// Which node currently owns this connection. A broker connection is exclusive, so exactly one
    /// node may hold it; this is what the placement layer writes.
    /// </summary>
    public string OwnedByNode { get; set; }

    public DateTime CreatedOn { get; set; }
    public string CreatedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
    public string ModifiedBy { get; set; }
}

/// <summary>
/// Broker today. The others are declared now because the adapter contract is identical for them -
/// a relational or object-store source differs in whether it pushes or is polled, not in how it is
/// configured, supervised or observed.
/// </summary>
public enum DataSourceKind
{
    Broker = 0,
    Relational = 1,
    Document = 2,
    ObjectStore = 3,
    Http = 4
}

/// <summary>
/// How many nodes may hold this connection.
///
/// The distinction is not cosmetic and the default is not safe in both directions. A broker queue
/// consumed by two nodes is duplicate processing - the failure the whole leased design exists to
/// prevent. A database connection pool held by only one node is the opposite failure: every other
/// node's Xchanges have nowhere to run, and the health page reports "not running here" as though
/// that were normal.
/// </summary>
public enum DataSourcePlacement
{
    /// <summary>Decided from <see cref="DataSourceKind"/>: a broker is exclusive, anything else is per-node.</summary>
    Auto = 0,

    /// <summary>One node at a time, chosen by lease. Brokers, and anything else that pushes.</summary>
    Exclusive = 1,

    /// <summary>Every node runs its own instance. Pools, and anything else the host only ever calls.</summary>
    PerNode = 2
}

public static class DataSourcePlacementExtensions
{
    /// <summary>
    /// What <see cref="DataSourcePlacement.Auto"/> actually means for this source.
    ///
    /// A relational source registered for change notification is the exception that proves the
    /// rule: it stops being something the host merely calls and starts being something that pushes,
    /// so it needs the lease back. Set <see cref="DataSource.Placement"/> explicitly for that.
    /// </summary>
    public static DataSourcePlacement Resolve(this DataSource dataSource) =>
        dataSource.Placement != DataSourcePlacement.Auto ? dataSource.Placement
        : dataSource.Kind == DataSourceKind.Broker ? DataSourcePlacement.Exclusive
        : DataSourcePlacement.PerNode;
}
