using System;
using System.Collections.Generic;

namespace SW.Bitween.Model;

/// <summary>
/// How to reach an external system. A BusGateway with no data source still means the internal bus,
/// so setting one on a gateway is the single act that moves it onto a customer's broker.
/// </summary>
public class DataSourceCreate : IName
{
    /// <summary>Required; the server rejects a create without it.</summary>
    public string Name { get; set; } = null!;

    /// <summary>The adapter that speaks this protocol, e.g. <c>bitween.bus.rabbitmq</c>.</summary>
    /// <summary>Required; the server rejects a create without it.</summary>
    public string AdapterId { get; set; } = null!;

    public string Kind { get; set; } = "Broker";

    /// <summary>
    /// How many nodes may run this source's adapter: <c>Auto</c>, <c>Exclusive</c> or <c>PerNode</c>.
    ///
    /// Auto — the default — follows from <see cref="Kind"/>: a broker connection is exclusive
    /// because two nodes consuming one queue is duplicate processing, and everything else is
    /// per-node because a connection pool held by a single node leaves every other node unable to
    /// use it. Override only for the case that crosses over: a relational source registered for
    /// change notification pushes, so it needs Exclusive.
    /// </summary>
    public string Placement { get; set; } = "Auto";

    /// <summary>
    /// Connection settings, handed to the adapter as startup values. Untyped on purpose: a provider
    /// must not be limited to the subset of a broker's model Bitween happens to have modelled.
    /// </summary>
    public Dictionary<string, string> Properties { get; set; } = new();

    /// <summary>
    /// Which names in <see cref="Properties"/> hold credentials. Those come back from the API
    /// masked, and a masked value saved again keeps whatever is already stored.
    /// </summary>
    public List<string> SecretProperties { get; set; } = new();

    public bool Inactive { get; set; }

    /// <summary>
    /// Soft ceiling in MB for the adapter process. Crossing it recycles the adapter between
    /// messages rather than killing it. Zero leaves the host default.
    /// </summary>
    public int SoftMemoryLimitMb { get; set; }

    /// <summary>
    /// Hard ceiling in MB, enforced by the runtime as the adapter's GC heap hard limit, so an
    /// allocation past it fails inside the adapter rather than taking the node with it. Zero
    /// leaves the host default.
    /// </summary>
    public int HardMemoryLimitMb { get; set; }

    /// <summary>
    /// Sustained CPU ceiling as a percentage of the whole node. Zero leaves the host default.
    /// One pegged core on a sixteen-core node is about 6%, not 100% — see the entity's remarks.
    /// </summary>
    public double CpuPercentLimit { get; set; }

    /// <summary>Consecutive heartbeats above the CPU ceiling before it trips. Zero = host default.</summary>
    public int CpuLimitSamples { get; set; }

    /// <summary>
    /// How long a message's dedupe key is remembered. It has to exceed the widest redelivery window
    /// this broker can produce. Zero turns deduplication off.
    /// </summary>
    public int DeduplicationWindowDays { get; set; } = 30;
}

public class DataSourceUpdate : DataSourceCreate
{
}

public class DataSourceRow : DataSourceUpdate
{
    public int Id { get; set; }

    /// <summary>How many bus gateways this data source feeds. Deleting is refused while any do.</summary>
    public int GatewayCount { get; set; }

    /// <summary>How many subscriptions run on this data source's connection.</summary>
    public int SubscriptionCount { get; set; }

    // ------------------------------------------------------------------ health

    /// <summary>Null until the supervisor has reported on it once.</summary>
    public string? LastKnownState { get; set; }
    public DateTime? LastHeartbeatOn { get; set; }
    public string? LastException { get; set; }
    public int ConsecutiveFailures { get; set; }

    /// <summary>Which node holds this connection, and at which fencing term.</summary>
    /// <summary>Null when no node currently holds it.</summary>
    public string? OwnedByNode { get; set; }
}

/// <summary>
/// Nothing to send: the data source already holds everything the test needs. It exists because a
/// keyed command takes a body, and an operator pressing Test is not supplying anything.
/// </summary>
public class DataSourceTestRequest
{
}

/// <summary>
/// What a Test button reports. Staged rather than a single boolean, because "it did not work" is
/// not an answer anyone can act on: the failing stage names what to go and fix.
/// </summary>
public class DataSourceTestResult
{
    public bool Succeeded { get; set; }

    /// <summary>Null when it succeeded.</summary>
    public string? Error { get; set; }

    /// <summary>Stage name to outcome, in the order the adapter attempted them.</summary>
    public List<DataSourceTestStage> Stages { get; set; } = new();

    /// <summary>Whatever the adapter chose to report — endpoint, queue depths, visibility timeout.</summary>
    public Dictionary<string, string> Details { get; set; } = new();
}

public class DataSourceTestStage
{
    public string Name { get; set; } = null!;
    public bool Succeeded { get; set; }

    /// <summary>What the stage found, or why it failed; null when there is nothing to add.</summary>
    public string? Detail { get; set; }
}

/// <summary>
/// What the connection is doing right now, as opposed to what it was configured to do.
///
/// Read live from the resident adapter host rather than from the data source row, because the row
/// only carries what the last reconcile happened to write back — a summary, thirty seconds stale at
/// worst. This is the heartbeat itself: the counters the adapter keeps, the queue depths it can
/// see, and what the host observes about the process without needing the adapter's cooperation.
/// </summary>
public class DataSourceTelemetry
{
    /// <summary>
    /// False when this node is not running the adapter. That is not a fault: a broker connection is
    /// exclusive, so at most one node holds it and every other node answers this honestly rather
    /// than reporting an outage it cannot see.
    /// </summary>
    public bool RunningHere { get; set; }

    /// <summary>Which node holds the connection, from the data source row — filled in even when it is not this one.</summary>
    public string? OwnedByNode { get; set; }

    // ---------------------------------------------------------------- adapter-reported

    public bool Connected { get; set; }
    /// <summary>Null until the adapter has reported a state.</summary>
    public string? State { get; set; }
    public DateTime? LastMessageOn { get; set; }
    public long InFlight { get; set; }

    /// <summary>Null while the connection is healthy.</summary>
    public string? LastError { get; set; }

    /// <summary>
    /// Whatever the adapter chose to report: per-queue depth, messages received and acknowledged,
    /// prefetch, the endpoint it is connected to. Untyped on purpose — Bitween does not model any
    /// broker's telemetry any more than it models its topology.
    /// </summary>
    public Dictionary<string, string> Details { get; set; } = new();

    // ------------------------------------------------------- host-observed (no cooperation needed)

    /// <summary>These keep working when the adapter is wedged, which is exactly when they matter.</summary>
    public int? ProcessId { get; set; }
    public long WorkingSetBytes { get; set; }
    public double CpuPercent { get; set; }
    public int ThreadCount { get; set; }
    public TimeSpan Uptime { get; set; }
    public int RestartCount { get; set; }
    public int MissedHeartbeats { get; set; }

    /// <summary>Restarted too many times too quickly, so the supervisor stopped trying.</summary>
    public bool Quarantined { get; set; }

    public DateTime? LastHeartbeatOn { get; set; }

    /// <summary>What the adapter says it can do — the commands the UI could offer against it.</summary>
    public List<string> Commands { get; set; } = new();
}

/// <summary>Which read-only command to relay to the running adapter. Defaults to Discover.</summary>
public class DataSourceInspectRequest
{
    public string Command { get; set; } = null!;

    /// <summary>
    /// Passed to the command as its argument. Discover reads objectType, schema, nameLike,
    /// includeColumns, includeRowCounts, skip and take from here; Describe and GetStats take
    /// nothing and ignore whatever is sent.
    ///
    /// Strings, and deliberately so: this crosses two serialization boundaries to reach an
    /// adapter that binds it to a typed request, and "200"/"true" coerce cleanly while an
    /// untyped object graph would not survive the trip unchanged. Nothing here can widen what
    /// the command does — the allow-list decides that, and every command on it is read-only.
    /// </summary>
    public Dictionary<string, string>? Arguments { get; set; }
}

public class DataSourceInspectResult
{
    /// <summary>False when this node is not the one holding the connection, or the command failed.</summary>
    public bool Ran { get; set; }

    public string Command { get; set; } = null!;

    /// <summary>
    /// The adapter's answer, as JSON text. Untyped for the same reason its telemetry is: Bitween
    /// does not model any broker's topology, and a provider must be free to describe its own.
    /// </summary>
    public string? Result { get; set; }

    /// <summary>Null when the command succeeded.</summary>
    public string? Error { get; set; }
}

/// <summary>
/// What a bus provider accepts, as the adapter itself declares it.
///
/// This exists so the UI can render a data source form without knowing anything about brokers. The
/// alternative — the one this replaced — is a table of fields, defaults and allowed values kept by
/// hand in the front end, which is a copy of a contract it does not own and cannot be told when it
/// changes.
/// </summary>
public class DataSourceProviderDescriptor
{
    public string AdapterId { get; set; } = null!;

    /// <summary>What to call it in a menu. The adapter id when the adapter did not say.</summary>
    public string Label { get; set; } = null!;

    /// <summary>
    /// Which DataSourceKind this provider produces — Broker, Relational, Document, ObjectStore or
    /// Http. A data source is not only a broker connection: a resident adapter that holds a
    /// database session is one too, and it must never be offered as a bus gateway's source.
    /// </summary>
    public string Kind { get; set; } = "Broker";

    /// <summary>
    /// How many nodes may run this source's adapter: <c>Auto</c>, <c>Exclusive</c> or <c>PerNode</c>.
    ///
    /// Auto — the default — follows from <see cref="Kind"/>: a broker connection is exclusive
    /// because two nodes consuming one queue is duplicate processing, and everything else is
    /// per-node because a connection pool held by a single node leaves every other node unable to
    /// use it. Override only for the case that crosses over: a relational source registered for
    /// change notification pushes, so it needs Exclusive.
    /// </summary>
    public string Placement { get; set; } = "Auto";

    /// <summary>Null when the adapter declares none.</summary>
    public string? Description { get; set; }

    public List<DataSourceProviderSetting> Settings { get; set; } = [];
}

/// <summary>One connection setting an operator can fill in.</summary>
public class DataSourceProviderSetting
{
    public const string StringType = "string";
    public const string NumberType = "number";
    public const string BooleanType = "boolean";

    /// <summary>The property name the adapter binds by — this is the data source property key.</summary>
    public string Name { get; set; } = null!;

    /// <summary>string, number or boolean. Coarse on purpose: it picks an input, nothing more.</summary>
    public string Type { get; set; } = StringType;

    /// <summary>Null when the adapter declares none.</summary>
    public string? Hint { get; set; }

    /// <summary>What a new data source starts with. Null means start it empty.</summary>
    /// <summary>Null when the adapter declares none.</summary>
    public string? Default { get; set; }

    /// <summary>When set, the only legal values — the UI offers these instead of free text.</summary>
    /// <summary>Null when the setting is free text rather than a fixed choice.</summary>
    public string[]? AllowedValues { get; set; }

    /// <summary>Masked in responses and never shown back once stored.</summary>
    public bool Secret { get; set; }

    public bool Required { get; set; }
}
