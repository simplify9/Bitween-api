# External Brokers: Data Sources, Provider Plugins, and Cluster Control

Status: **design proposal** (not implemented)
Baseline: **`origin/v2`** @ `fa2dcb3` - 46 commits ahead of `releases/r8.0`, and the branch this
feature lands on. Anything below that cites `releases/r8.0` line numbers is marked as such.

> **v2 changes several premises.** It embeds the UI in the API (`SW.Bitween.Web/ClientApp`,
> a *new* stack - TanStack Query + Tailwind 4 + Headless UI + react-router 8, no Redux and no
> RTK Query, so `Bitween-UI`'s conventions in `CLAUDE.md` do not apply here), adds an `Ops`
> health API, an RBAC permission system, and - most relevant - a **runtime settings subsystem
> with a descriptor catalog and a secret protector** that this design should reuse rather than
> reinvent. It also renames domain concepts in the UI: **Subscription → Integration**,
> **Document → Information type**, **Xchange → Exchange**. Backend names are unchanged.

## 1. Where we are today

Bus-triggered ingestion today has exactly one transport: the *internal* SW.Bus RabbitMQ
connection that Bitween shares with the microservices it is deployed next to.

| Piece | File | Role |
|---|---|---|
| `Document.BusEnabled` / `BusMessageTypeName` | `SW.Bitween.Api/Domain/Document/Document.cs` | marks a document as bus-ingestible and names the message type |
| `BusService : IConsume` | `SW.Bitween.Api/Services/BusService.cs` | dynamic multi-message consumer; maps message type → documentId, then `XchangeService.SubmitFilterXchange` |
| `BusGateway` / `BusGatewayRoute` | `SW.Bitween.Api/Domain/Gateway/` | routing table over a bus-enabled document: (filter, subscription, partner) triples |
| `FilterService.Filter` | `SW.Bitween.Api/Services/FilterService.cs` | evaluates promoted properties → `GatewayHits` |
| runtime topology refresh | `IBroadcast.RefreshConsumers()` → `ConsumersService.RefreshConsumers()` | re-runs consumer discovery and attaches/updates queues without restart |

Two observations that shape the design:

1. **`BusGateway` has no connection concept at all** - it is pure routing. Adding a
   `DataSource` reference is therefore additive, and `null` can keep meaning "internal bus".
2. **The runtime-mutability machinery already exists and works**: `IBroadcast` +
   `IListen<T>` over the per-node exchange, plus `IInfolinkCache` + `RevokeCacheMessage`.
   We should reuse it rather than invent a second control plane.

Relevant SW.Bus facts (verified in `SW-Bus/`):

- `IBroadcast.Broadcast<T>` publishes to `busOptions.NodeExchange` (direct) with a
  **single shared routing key** `NodeRoutingKey`; each node declares its own
  `NodeQueueName = "{NodeExchange}:{NodeId}"` as `exclusive: true, autoDelete: true`
  and binds it to that key. So a broadcast fans out to *every* live node, and there is
  currently **no per-node addressing**. Per-node targeting must be done by filtering
  inside the listener (`if (msg.TargetNodeId != null && msg.TargetNodeId != myNodeId) return;`).
- `IListen<T>` handlers are discovered once at startup (`ConsumerDiscovery.LoadListeners`),
  run with prefetch 1 on the node channel, and have retry/dead-letter (`ListenRetryCount`).
- The exclusive-queue behaviour the leader election idea relies on is already proven in
  this codebase - the node queue *is* an exclusive queue tied to one connection.

## 2. Verdict on the idea

The shape you proposed is right. Four things I'd change or make explicit:

1. **Do not build one universal broker model.** A "super-entity" that has fields for
   exchanges *and* topics *and* consumer groups *and* partitions *and* visibility timeouts
   collapses under its own weight by the third provider. Instead: a **narrow core contract**
   (subscribe / ack / publish over a normalized envelope), a **declared capability set**,
   and a **provider-owned config schema** stored as JSON. Bitween core never learns what an
   exchange is; providers do.
2. **Leader election is a property of the bus, so put the abstraction in the bus.**
   SW.Bus is RabbitMQ-only and heavily used internally; there is one real implementation
   (exclusive queue) and no appetite for a second right now. But the *seam* is worth having
   from day one so a future non-Rabbit bus can supply its own primitive without touching
   Bitween. Concretely: contract in `SimplyWorks.Bus.RabbitMqExtensions`, implementation in
   `SimplyWorks.Bus`, and Bitween depends only on the contract (§6.1). Exclusive-queue
   election gives *liveness*, not consensus - under a partition two nodes can briefly both
   believe they lead - so the **fencing token stays in Bitween's database**, and leader-only
   writes are guarded by it. Same trick `RunFlagUpdater.MarkAsRunning` already uses.
3. **Per-node on/off should be declarative placement, not imperative toggles.** Model
   *desired* placement on the gateway (`All` / `Leader` / `Tagged` / `Explicit`), and store
   manual per-node toggles as **overrides** on top of it. Otherwise a node restart silently
   loses the operator's intent, or worse, silently regains a subscription they turned off.
4. **Ack ownership: persist then ack, immediately.** Never hold a broker message open while
   the mapper/handler pipeline runs. Ingest = write the `Xchange` + commit, then ack. All
   redelivery is then owned by the existing `DelayedRetry` / `RetryPolicy` subsystem, which
   is uniform across providers - instead of nine different broker redelivery semantics.

## 3. Domain model

Three new entities (+ two columns on existing ones).

### 3.1 `DataSource` - a connection to a broker

```csharp
public class DataSource : BaseEntity, IAudited
{
    public string Name { get; set; }
    public string ProviderKey { get; set; }         // "rabbitmq" | "kafka" | "sqs" | "eventhub" | "mqtt" | "pulsar"
    public JsonDocument Settings { get; set; }      // provider-defined; secret fields via SettingsProtector (§11.1)
    public bool Inactive { get; set; }
    public string ConfigHash { get; set; }          // set on save; drives reconciliation diffing
    // health / observability
    public DateTime? LastConnectedOn { get; set; }
    public string LastException { get; set; }
    public int ConsecutiveFailures { get; set; }
}
```

`Settings` is validated against the provider's descriptor (§4.2) at write time, so a bad
Kafka `bootstrap.servers` is rejected by the API, not discovered at 3am by the supervisor.

> Naming: `DataSource` is fine and matches how you framed it, but note it is bidirectional
> (ingress *and* egress). `DataSource` + a child `DataSourceEndpoint` reads better than
> overloading one row with both directions.

### 3.2 `DataSourceEndpoint` - one subscribe-able / publish-able thing on a data source

```csharp
public class DataSourceEndpoint : BaseEntity, IAudited
{
    public int DataSourceId { get; set; }
    public string Name { get; set; }
    public EndpointDirection Direction { get; set; }  // Inbound | Outbound | Both
    public JsonDocument Binding { get; set; }         // provider-defined: queue+exchange+routingKey,
                                                      // or topic+consumerGroup, or queueUrl, or
                                                      // hub+consumerGroup+checkpointStore, or topicFilter+qos
    public JsonDocument Topology { get; set; }        // optional declarative "make this exist" plan
    public bool AutoProvision { get; set; }           // run EnsureTopology on start
}
```

Splitting endpoint from data source is what makes "connect to a client's RabbitMQ and create
five queues on it" a first-class operation rather than five copies of a connection string.

### 3.3 `ClusterNode` - who is alive, who leads, what each node runs

```csharp
public class ClusterNode                 // PK = NodeId (BusOptions.NodeId, stable per process)
{
    public string NodeId { get; set; }
    public string MachineName { get; set; }
    public string Version { get; set; }
    public string[] Tags { get; set; }                 // from config: Bitween:NodeTags
    public DateTime StartedOn { get; set; }
    public DateTime LastHeartbeatOn { get; set; }
    public bool GatewaysEnabled { get; set; }          // node-wide kill switch
    public JsonDocument Overrides { get; set; }        // { "gateway:12": false, "dataSource:3": false }
}

public class ClusterLeader               // single row, id = 1
{
    public string NodeId { get; set; }
    public long Term { get; set; }                     // fencing token, ++ on each acquisition
    public DateTime AcquiredOn { get; set; }
    public DateTime RenewedOn { get; set; }
}
```

### 3.4 Changes to existing entities

- `BusGateway`: `+ int? DataSourceId`, `+ int? EndpointId`, `+ GatewayPlacement Placement`
  (`All | Leader | Tagged | Explicit`), `+ string[] PlacementTags / string[] PlacementNodeIds`,
  `+ bool Inactive`. **`DataSourceId == null` keeps today's exact behaviour** (internal bus
  via `BusService`) - zero migration risk for existing installs, including Traxis-style ones.
- `Subscription`: optional `+ int? ResponseEndpointId` - generalizes today's
  `ResponseMessageTypeName` bus publish so a subscription's output can land on an external
  Kafka topic / SQS queue (§6.3).

### 3.5 Ingress equivalence, and what it buys

An external broker, an API gateway call, and a scheduled receiver are **the same kind of thing**:
a producer that persists an `Xchange` and lets the internal bus hand it off. Nothing about the
mapper, handler, or filter is coupled to how the message arrived. Two consequences worth stating
because they shrink the design:

1. **The external broker's load balancing is irrelevant to Bitween's processing scale-out.** Once
   ingest commits, work distribution across nodes is done by the *internal* RabbitMQ work-group
   queues. Kafka consumer groups, Rabbit competing consumers, and SQS long polling only decide
   **who reads from the external broker** - not who processes. So §6's placement and leader
   election govern **connection ownership only**. That is a much smaller claim than it first
   appears, and it means a single-node-owned Kafka subscription still processes across the whole
   cluster.
2. **The internal RabbitMQ stays a hard dependency**, even for a Kafka-only or SQS-only client.
   Worth being explicit with ops: adding external brokers does not let anyone drop SW.Bus.

### 3.6 Filtering: it already exists, and there is a second kind worth adding

**Bitween-side filtering is unchanged and already provided.** `FilterService` extracts the
Document's promoted properties (JSON/XML paths) and evaluates `BusGatewayRoute.MatchExpression`
per route; a null expression matches everything. External ingress inherits this for free - that is
the whole reason to route through `BusGateway` rather than invent a parallel concept. Nothing to
build.

But note *where* it happens: **after** the payload has been uploaded to cloud storage and the
`Xchange` committed. For a client topic where only 5% of messages are interesting, that is 95% of
the blob PUTs, rows, and queue traffic spent to discover the message was irrelevant. So there is a
second, cheaper filter worth having:

| | Broker-side selection | Bitween-side filtering |
|---|---|---|
| Lives in | `DataSourceEndpoint.Binding` | `Document.PromotedProperties` + `BusGatewayRoute.MatchExpression` |
| Runs | before Bitween sees the message | after persist, at hop 1 |
| Cost of a non-match | zero | one blob PUT + one row + one queue hop |
| Expressiveness | whatever the broker offers | full match expressions over promoted properties |
| RabbitMQ | binding routing keys, headers exchange with `x-match` | - |
| MQTT | topic filters with `+`/`#` | - |
| Kafka | **nothing** - no server-side filtering exists | - |

So broker-side selection is a **capability** (`SupportsServerSideSelection`), not a guarantee. Where
it exists, prefer it and let Bitween-side filtering handle what the broker can't express. Where it
doesn't (Kafka), the volume lands on Bitween and `Document.DisregardsUnfilteredMessages` becomes the
tool that stops unmatched messages from accumulating.

### 3.7 What lives in `DataSource` vs `DataSourceEndpoint` vs `BusGateway`

The boundary is **transport vs business meaning**, and it should stay that clean:

| Concern | Lives in | Examples |
|---|---|---|
| How to reach the system | **`DataSource`** | hosts, vhost/cluster, credentials, TLS, auth mechanism, client id, management URL, pass-through config |
| Health and connection state | **`DataSource`** | `LastConnectedOn`, `LastException`, `ConsecutiveFailures`, `ConfigHash` |
| Which object on that system, and how to read/write it | **`DataSourceEndpoint`** | queue/topic/table/collection name, prefetch, consumer group, declare mode, topology plan, broker-side selection |
| Where the cursor is | **`DataSourceEndpointCursor`** (§3.8) | Kafka offset, Mongo resume token, RDBMS high-water mark |
| **What the message means** | **`BusGateway`** | `DocumentId` - the information type |
| **Who processes it, with which partner, under which filter** | **`BusGatewayRoute`** | `SubscriptionId`, `PartnerId`, `MatchExpression` |
| Placement and enablement | **`BusGateway`** | `Placement`, `WorkGroupId` (§5.1a), `Inactive` |

The join is `BusGateway.EndpointId` + the existing `BusGateway.DocumentId`. `DataSource` and
`DataSourceEndpoint` carry **no business semantics whatsoever** - that is what makes them reusable
for egress, for enrichment, and for the non-messaging kinds below.

**One gap this exposes: mixed-type topics.** A queue usually carries one message type, so
"endpoint → one document" is fine. A Kafka topic often carries several. So `BusGateway` needs a
**document resolution strategy** rather than only a fixed `DocumentId`:

```
DocumentResolution = Fixed(documentId)
                   | Header(headerName → document code)     // Rabbit headers, Kafka headers
                   | RoutingKey(pattern → document code)     // Rabbit
                   | PayloadPath(jsonPath/xpath → document code)
```

`Fixed` covers phase 2; the rest are additive and cheap *if the column exists from the start*.

### 3.8 Keeping `DataSource` reusable beyond messaging

This matters now, because the abstractions ship as a NuGet package and renaming a published
contract later is a breaking change. Three decisions to take up front - all cost nothing today:

**1. Name and shape it around *data sources*, not messaging.** `IDataSourceProvider` is the wrong
name. Use `IDataSourceProvider` with a declared kind:

```csharp
public enum DataSourceKind { Broker, Relational, Document, ObjectStore, FileTransfer, Http }
```

**2. Split behaviour into capability interfaces instead of one fat contract.** A provider
implements only what its system can do; core checks for the interface rather than assuming:

| Capability | Meaning | Broker | RDBMS | NoSQL |
|---|---|---|---|---|
| `ISubscribeCapable` | push/streaming delivery | ✔ | - | Mongo change streams ✔ |
| `IPollCapable` | pull on a schedule, cursor-driven | - | ✔ | ✔ |
| `IPublishCapable` | write a message/row/document out (egress, §6.3) | ✔ | ✔ (insert) | ✔ |
| `IQueryCapable` | ad-hoc read for **enrichment during mapping** | - | ✔ | ✔ |
| `ITopologyCapable` | create/inspect objects | ✔ | ✔ (DDL, usually off) | ✔ |
| `IBrowseCapable` | discovery (§1.12) - topics, tables, collections | ✔ | ✔ | ✔ |

`IQueryCapable` is the one that is easy to miss and changes the design: a `DataSource` may be
consumed by a **mapper** rather than acting as an ingress at all - "look up this SKU in the
client's SQL Server while mapping". That means data sources must be resolvable from adapter
context alongside `__partner__` and `__globals__` (say `__datasources__`), which is a contract
decision, not an implementation detail.

**3. Add the cursor concept now.** Push sources track position in the broker; pull sources cannot.
RDBMS high-water marks, Mongo resume tokens, SFTP "seen files", and Kafka offsets are all the same
idea, and Bitween already needs somewhere durable and per-node-agnostic to keep it:

```csharp
public class DataSourceEndpointCursor      // one row per endpoint
{
    public int EndpointId { get; set; }
    public string Kind { get; set; }        // "offset" | "timestamp" | "token" | "id"
    public string Value { get; set; }
    public DateTime UpdatedOn { get; set; }
    public long Term { get; set; }          // fencing (§6.1) - only the current owner may advance it
}
```

Retrofitting this after three providers exist is painful; adding the table now is a migration.

**Where this pays off immediately:** Bitween *already* has pull-based ingestion - `ReceivingJob`,
Quartz schedules, and native receivers for S3, FTP, POP3, and Azure Blob. Today each of those
carries its own connection details inside `Subscription.ReceiverProperties`, so an FTP credential
is duplicated per subscription, unencrypted beyond the adapter's own handling, untestable, and
unbrowsable. If `DataSource` is designed transport-agnostically, those receivers can later
**reference a `DataSource`** instead - one encrypted, health-monitored, test-connectable,
discoverable connection reused across subscriptions. That unification, not the brokers, is the
strongest long-term argument for getting this entity right on the first attempt.

Scope note: only the **abstraction** is in scope now. No RDBMS or NoSQL provider is being built;
the point is that adding one later should require no change to `DataSource`, `DataSourceEndpoint`,
the registry, placement, health, or the UI shell.

## 4. Plugin architecture

### 4.1 Packaging

New abstraction-only NuGet package **`SW.Bitween.DataSources.Abstractions`** (mirrors how
`SW.Bus.RabbitMqExtensions` has no `RabbitMQ.Client` dependency). Provider packages depend
only on it:

```
SW.Bitween.DataSources.Abstractions   ← contracts, descriptors, envelope, capabilities
SW.Bitween.DataSources.RabbitMq       ← built-in, in-tree
SW.Bitween.DataSources.Kafka          ← built-in, in-tree
SW.Bitween.Messaging.Sqs / .EventHubs / .Mqtt / .Pulsar   ← separate, opt-in
```

Loading is **startup-only** (as you specified): `services.AddBitweenDataSources()` registers
in-tree providers, then for each directory in `Bitween:MessagingProviderPaths` creates an
`AssemblyLoadContext` per plugin folder with `SW.Bitween.DataSources.Abstractions` (and
`Microsoft.Extensions.*`) resolved from the **host**, everything else from the plugin folder.
This gives dependency isolation - critical, since `Confluent.Kafka`, `AWSSDK`, and
`Azure.Messaging.EventHubs` all drag in conflicting transitive versions. Discovered
`IDataSourceProvider` implementations land in a singleton `IDataSourceProviderRegistry`
keyed by `ProviderKey`.

> **Rejected alternative:** running providers as SW.Serverless adapters (subprocess +
> stdin/stdout, distributed via cloud storage). It is a great fit for mappers/handlers, and a
> bad fit here: providers hold long-lived connections, need sub-millisecond ack round-trips,
> and must surface streaming callbacks - none of which survive a request/response RPC over
> stdio. In-process ALC plugins, loaded at startup, is the right trade.

### 4.2 The contract

The whole point is that **each broker's concepts stay inside its provider**. Core sees:

```csharp
public interface IDataSourceProvider
{
    string Key { get; }
    ProviderDescriptor Describe();                     // capabilities + config schema (drives UI *and* validation)
    Task<IDataSourceConnection> Connect(ResolvedConfig settings, CancellationToken ct);
}

public interface IDataSourceConnection : IAsyncDisposable
{
    Task<HealthResult> CheckHealth(CancellationToken ct);
    Task<IMessageSubscription> Subscribe(ResolvedConfig binding, SubscribeOptions options,
                                         Func<InboundMessage, CancellationToken, Task<AckDecision>> onMessage,
                                         CancellationToken ct);
    Task Publish(ResolvedConfig binding, OutboundMessage message, CancellationToken ct);
    ITopologyManager Topology { get; }                 // null when !Capabilities.CanManageTopology
}

public interface IMessageSubscription : IAsyncDisposable
{
    SubscriptionState State { get; }                   // Starting | Running | Degraded | Stopped
    event EventHandler<SubscriptionFault> Faulted;
}

public interface ITopologyManager
{
    Task<TopologyDiff> Plan(TopologyPlan plan, CancellationToken ct);        // dry run - show the operator
    Task Apply(TopologyPlan plan, CancellationToken ct);                     // idempotent create/bind
    Task<IReadOnlyList<TopologyObject>> Browse(BrowseQuery query, CancellationToken ct); // pickers in the UI
}
```

Normalized envelope - the lowest common denominator that all six brokers actually have:

```csharp
public sealed record InboundMessage(
    ReadOnlyMemory<byte> Body,
    IReadOnlyDictionary<string, string> Headers,   // emulated via message attributes / properties where needed
    string Key,                                   // Kafka key / MQTT topic / Rabbit routing key / SQS group id
    string ProviderMessageId,
    DateTimeOffset? Timestamp,
    IReadOnlyDictionary<string, object> ProviderMetadata); // offset, partition, receiptHandle, deliveryTag…

public enum AckDecision { Ack, Reject, RequeueLater, DeadLetter }
```

`ProviderMetadata` is the escape hatch: providers can surface anything, and it is carried
into the `Xchange` references / adapter context so a mapper can read `partition` or
`receiptHandle` without core knowing they exist.

### 4.3 Capabilities: how the complexity is actually contained

```csharp
public sealed record ProviderCapabilities(
    bool CanManageTopology, bool CanBrowseTopology,
    bool SupportsHeaders, bool SupportsOrderingKey, bool SupportsPartitions,
    bool SupportsNack, bool SupportsRequeueDelay, bool SupportsNativeDeadLetter,
    bool SupportsExclusiveConsumer,           // → can back leader election / single-active-consumer
    bool SupportsCompetingConsumers,          // → Placement=All is safe
    bool SupportsTransactions, bool SupportsBatch,
    int  MaxMessageBytes);
```

Rules that follow mechanically from this, enforced in one place:

- `Placement = All` is only offered when `SupportsCompetingConsumers` (Kafka consumer
  groups: yes; a single MQTT non-shared subscription: no → forced to `Leader`).
- `AckDecision.RequeueLater` falls back to "ack + `DelayedRetry` row" when
  `!SupportsRequeueDelay` - so behaviour is uniform even where the broker can't do it.
- `SupportsNativeDeadLetter == false` → dead letters are recorded as failed `Xchange`s only.
- The UI never renders a control the provider didn't declare - no per-provider UI code.

`ProviderDescriptor` also carries the **config schema** (field key, label, type, required,
`IsSecret`, default, enum options, group, help text, plus optional dependsOn) for both
connection settings and bindings. Both API validation and the UI form are generated from it,
which is what makes adding a provider a zero-UI-change operation.

## 5. Runtime: supervisor and reconciliation

```
MessagingSupervisor (IHostedService, singleton)
  desired = f(DB: active DataSources × active BusGateways × placement × node overrides × leadership)
  actual  = in-memory map { dataSourceId → IDataSourceConnection, gatewayId → IMessageSubscription }
  Reconcile() → diff → open/close connections, start/stop subscriptions   (idempotent, lock-guarded)
```

Reconcile is triggered by:

| Trigger | Mechanism |
|---|---|
| startup | after `SchedulerSeedService`-style hosted-service start |
| config change (CRUD on DataSource / Endpoint / BusGateway / Subscription) | `IBroadcast.Broadcast(new GatewayControlMessage { Action = Reconcile })` from the CQAPI handler - same pattern as `Documents/Update.cs` calling `RefreshConsumers()` |
| operator start/stop on one node | same message with `TargetNodeId` set; listener no-ops if it isn't me |
| leadership change | `ILeaderElector` raises `LeadershipChanged` → local reconcile |
| drift / missed message | periodic Quartz job (`ClusterHeartbeatJob`, default every 15-30s) also calls `Reconcile()` |
| connection fault | `IMessageSubscription.Faulted` → backoff (exponential, capped) then reconcile that entry only |

Diffing granularity uses `ConfigHash`: connection-settings change → rebuild connection and
its subscriptions; binding-only change → restart just that subscription; placement change →
start/stop only.

Ingest path per message (deliberately thin):

```
InboundMessage
  → IngressPipeline: decode (provider-declared content type), size guard, optional dedupe
  → XchangeService.SubmitFilterXchange(document.Id, xchangeFile, references, correlationId)
  → SaveChanges  → return AckDecision.Ack
```

Everything downstream - `FilterService` gateway routes, partner values, mappers/handlers,
`RetryPolicy` - is **completely unchanged**. That is the main reason to route external
brokers through `BusGateway` rather than inventing a parallel concept.

### 5.1 The ingest contract, stated exactly

The provider's entire job is: **persist the message, let the existing event mechanism hand it to a
work group, ack.** No filtering, no mapping, no handler - none of the pipeline runs while the
broker message is open. Verified end-to-end on v2:

| # | Step | Code |
|---|---|---|
| 1 | provider → `SubmitFilterXchange(documentId, file, refs, correlationId)` | `XchangeService.cs:68` |
| 2 | payload uploaded to **cloud storage**, `Xchange` added to the change tracker | `CreateXchange` → `AddFile` → `_cloudFiles.WriteTextAsync` (`:340`) |
| 3 | `SaveChangesAsync()` - the commit | `XchangeService.cs:85` |
| 4 | **after** commit, the domain event is published to the work group's queue | `BitweenDbContext.SaveChangesAsync:399-407` → `publish.Publish(hasWorkGroup.GetBusMessageName(), {Id})` |
| 5 | `XchangeService` (`IConsumeExtended`, one queue per work group, prefetch/priority from `WorkGroup.Options.RabbitMqOptions`) consumes → filter → mapper → handler → result → retry | `Process(XchangeMessage)` |
| 6 | provider acks the broker message | - |

Three qualifications on "the right work group handles it":

**(a) It is two hops, and the first one lands on a queue you currently cannot tune.** The
filter-path `Xchange` is created with `workGroup: null`, and `Xchange.cs:42` resolves that to
`WorkGroup.None` - a static, **transient** instance (`new() { BusMessageName = "Ungrouped" }`,
`Id = 0`, no DB row, `Options == null`). So hop 1 routes to `0Ungrouped`, whose `ConsumerOptions`
resolve to `null` prefetch and priority and therefore fall back to
`BitweenOptions.BusDefaultQueuePrefetch`. Hop 1 runs `FilterService` and creates the
per-subscription Xchanges (`CreateXchangesForHits`); only *those* carry
`subscription.WorkGroup` and land on a tunable queue.

Net effect: **all external ingest funnels through one shared, non-configurable queue** before it
ever reaches a work group - because there is no `WorkGroup` row for `None` to edit in the UI.

Three ways out, in increasing order of value:

1. Monitor `0Ungrouped` deliberately (the WorkGroups search already surfaces live queue stats from
   the management API, so the data path exists).
2. Set the document's `DisregardsUnfilteredMessages`, which makes `SubmitFilterXchange` filter
   **inline** and skip hop 1 entirely - slower ack, no shared queue.
3. **Recommended: add `BusGateway.WorkGroupId`** and pass it through to the filter-path
   `CreateXchange(document, workGroup, …)` overload, which *already accepts a work group* and is
   only ever called with `null`. That makes external ingest a first-class, tunable queue with its
   own prefetch and priority, isolates one client's broker traffic from another's, and is a
   genuinely small change: one column, one argument, no new concepts.

**(b) The event is published *after* commit, so there is a dual-write gap - and acking makes it
consequential.** If the publish fails (broker blip, process killed between commit and publish),
`SaveChangesAsync` throws: the row is committed, nothing will ever process it, and the provider
does **not** ack - so the broker redelivers and creates a *second* `Xchange` for the same message.
Net result: one orphan plus one processed copy. Today the same gap exists for API-created
Xchanges, but an HTTP caller gets a 202 and notices; a broker has nobody to notice. **There is no
orphan sweeper in the codebase** (verified). Two fixes, cheapest first:

- **Sweeper (recommended, ~50 lines):** a Quartz job that finds Xchanges with no `XchangeResult`
  older than N minutes and republishes the trigger event. Reuses `RetryJob`'s shape and fixes
  *every* ingress path, not just brokers.
- **Outbox for the trigger event:** the same machinery as the egress outbox (§6.3) - correct, but
  6b-sized.

This is also the concrete reason the dedupe question (§12.3) matters: with a provider message id
as the dedupe key, redelivery after a failed publish is idempotent instead of duplicating.

**(c) Ack latency includes a blob upload, not just a DB insert** - step 2 is a network round trip to
S3/Azure/Oracle. But see §5.2: measured in production, the whole ingest-plus-plumbing path is
~330 ms p50, and it is **not** the bottleneck. The bottleneck is adapter execution, by two orders
of magnitude.

### 5.2 Measured baseline - Traxis production (Bitween **6.1**), 2026-07-30

Read-only measurements against `traxis_prod` (`infolink` schema, DigitalOcean managed PG 17,
CloudFiles = DO Spaces `nyc3`).

> **This is a 6.1 install, not 8.x - but the pipeline logic is substantially the same.**
> `Xchange` created → payload to cloud storage → domain event published after commit → consumed
> → filter → mapper → handler → `XchangeResult`: that shape, and therefore its per-message cost
> structure, is unchanged between 6.1 and 8.x. What 8.x added is **routing granularity** (work
> groups replacing per-event-type queues), **native adapters**, **auto-retry**, and **gateways** -
> none of which alter the cost of the steps that were measured. So the numbers are broadly
> transferable; the exceptions are specific and worth naming.
>
> Latest applied migration is `20230910151704_SubscriptionCategory` (Sept 2023), and
> `releases/r6.1` is still the `SW.Infolink.*` generation. Verified absent, in both the live schema
> and the r6.1 tree:
> **work groups** (no `work_group` table, no `work_group_id` column, zero WorkGroup source files),
> **native adapters** (zero source files), and the **auto-retry subsystem** (no `delayed_retry`,
> `retry_policy`, or `group_attempt_counts`; only `xchange.retry_for`). There are no
> `bus_gateway`/`api_gateway` tables either.
>
> | Finding | Transfers to 8.x? |
> |---|---|
> | Volume, arrival rate, payload sizes | **Yes** - properties of the client's business traffic, not of Bitween |
> | Handler vs no-handler latency ratio (~100×) | **Yes, qualitatively** - it is an A/B inside one system, so the comparison is sound |
> | The 0.33 s "plumbing" figure | **Yes as a cost estimate** - same blob PUT, same insert/commit, same publish-after-commit, same consume. What differs is only *which* queue absorbs it: 6.1 used the **legacy per-event-type queues** (`InternalXchangeCreatedEvent` etc., the path `ConsumeLegacyEventMessages` still exists to preserve), 8.x uses work-group queues. Routing granularity changed; per-message cost did not |
> | "Zero native adapters" | **Reframed, not invalidated** - native adapters don't exist in 6.1, so this is unavailability rather than a choice. It makes the 32 s structural *there*, and makes the biggest lever something the 8.x upgrade newly unlocks |
> | Two-hop / `0Ungrouped` analysis (§5.1a) | **Not evidenced here at all** - it rests solely on reading the v2 source, which I verified directly |
> | Index sizes | Indicative only - 6.1-era index set on a 3-year-old schema |
>
> Bottom line: treat the volume, payload, and latency-ratio findings as real inputs for 8.x
> planning. Re-measure the absolute service time once an 8.x install with native adapters exists,
> because that is the one constant the upgrade is expected to move - by a lot.

**Volume (a genuinely small workload):**

| Metric | Value |
|---|---|
| Data span | 2025-10-01 → 2026-07-30 (10 months) |
| Total xchanges | 31,078 (≈100/day average) |
| Recent daily range | 45 - 4,128/day; median ≈ 1,800 |
| **Peak minute** | **140 xchanges/min ≈ 2.3/s** |
| Input payload | avg 3.6-17.5 KB (typically ~5 KB), **max 1.08 MB** |
| Storage | `xchange` 560 MB heap / **1,632 MB indexes**; `xchange_promoted_properties` 1,206 MB / 1,215 MB; `xchange_result` 413 MB / 360 MB - for 31k rows, with `n_tup_del = 0` |
| Adapter mix (7 d) | 11,538 of 14,468 xchanges have a handler; **`native.*` adapters in use: 0** |

**Latency, and this is the finding that matters** (7-day window, `started_on` → `result.finished_on`):

| Path | n | p50 | p90 |
|---|---|---|---|
| **without** a handler | 2,930 | **0.33 s** | 0.71 s |
| **with** a handler | 11,538 | **32.67 s** | 78.29 s |
| combined | 14,468 | 22.80 s | 70.35 s (p99 179 s, max 253 s) |

The no-handler row *is* the full Bitween plumbing - blob PUT, insert, commit, publish, two queue
hops, filter, result - and it costs **330 ms p50**. The handler adds **~32 seconds**. With zero
native adapters in use, every handler is a **serverless adapter, i.e. a spawned .NET subprocess per
invocation**: process start, assembly extraction and load, then the actual external call.

**So the ceiling is adapter execution concurrency - not storage, not the database, and nowhere near
the broker.** (This corrects an earlier claim in this document that storage and the DB would be the
limit; measured, they are ~1.5% of the time budget.)

**Capacity model.** Concurrency required is Little's Law, `L = λW`:

| Scenario | Arrival λ | Service W | **Concurrent in-flight** |
|---|---|---|---|
| Traxis peak today | 2.3/s | 32.7 s | **≈ 76** |
| A client at 10× | 23.3/s | 32.7 s | **≈ 762** |
| 10× **if** service time drops to 1 s | 23.3/s | 1 s | **≈ 24** |

`BitweenOptions.BusDefaultQueuePrefetch` is **12**, so concurrency per node per work-group queue is
about 12. 762 in-flight would need ~64 node-queue slots - and each in-flight handler is a
*subprocess*, so "just raise prefetch to 60" means 60 concurrent subprocesses per node, which is
not viable. Three conclusions:

1. **Cutting adapter service time is the highest-leverage change for 10× scale, by far** - native
   adapters (currently unused here) or a warm/pooled serverless process. Going from 32 s to 1 s
   turns 762 required slots into 24.
2. **Broker prefetch is not the lever.** Ingest acks in ~330 ms; at 5 KB payloads a prefetch of 20
   is 100 KB of memory. Bound `prefetchCount` by the **max** payload (1 MB here ⇒ prefetch 500
   would be 500 MB), not the average, and otherwise don't be timid.
3. **The work-group queues are the shock absorber, and that is correct** - acking fast and letting
   the queue grow is exactly what stops a 33-second handler from blocking a client's broker. Which
   makes queue depth (`QueueBackpressureThreshold`, default 5000) the alarm that matters, and makes
   `BusGateway.WorkGroupId` (§5.1a) more valuable, not less: external ingest needs its own tunable
   queue rather than sharing the untunable `0Ungrouped`.

**Also worth acting on independently:** indexes on these three tables total **~3.2 GB against
2.1 GB of heap for 31k rows**, with no deletes recorded - a `REINDEX CONCURRENTLY` should reclaim
most of it, and it will matter more at 10×.

### 6.1 Leader election

Election lives in **SW.Bus**, not in Bitween. The verified layering forces this and happens
to be the right design anyway:

- `SW.Bitween.Api` references only `SimplyWorks.Bus.RabbitMqExtensions` (8.1.11) - the
  contracts package with **no `RabbitMQ.Client` dependency**. `SimplyWorks.Bus` (which has
  it) is referenced only by `SW.Bitween.Web`. Implementing the elector inside
  `SW.Bitween.Api` - where the supervisor and Quartz jobs live - would mean giving it a
  broker dependency it deliberately doesn't have.
- SW.Bus already owns everything the elector needs: the `ConnectionFactory`, a stable
  `BusOptions.NodeId`, the `{env}.{app}` naming scheme, and connection-shutdown events.
- Whoever supplies the bus supplies the primitive. Swap the bus later and the new bus brings
  its own election; Bitween's code is unchanged. That is the "dynamic" part, obtained for
  free by placing one interface in the right assembly - not by writing a second
  implementation now.

**Contract** → `SimplyWorks.Bus.RabbitMqExtensions`:

```csharp
public interface ILeaderElection
{
    string Scope { get; }        // election is per named role, see below
    string NodeId { get; }
    bool   IsLeader { get; }
    int    Epoch { get; }        // local acquisition counter - liveness only, NOT a fence
    event EventHandler<LeadershipChangedEventArgs> LeadershipChanged;
}

public interface ILeaderElectionFactory
{
    ILeaderElection GetOrCreate(string scope);
}

public sealed record LeadershipChangedEventArgs(string Scope, bool IsLeader, int Epoch, DateTimeOffset OccurredOn);
```

**Scoped election matters, and should be in the contract from day one.** A single global
leader means one node performs *all* external ingest while the others idle - for a client
with twelve Kafka topics that is a deliberate bottleneck. With per-scope election
(`"gateway:{id}"` or `"datasource:{id}"`) each gateway is independently owned, so leadership
spreads across nodes naturally and a node loss only redistributes its share. Cost in
RabbitMQ: one extra exclusive queue per scope on one shared connection - negligible.

**Implementation** → `SimplyWorks.Bus`, `RabbitExclusiveQueueElection`, registered by an
opt-in `services.AddBusLeaderElection()`:

- One dedicated `IConnection` for all election scopes, separate from the consumer connection
  so consumer reconnect churn can never drop leadership.
- Per scope, loop `QueueDeclare("{ProcessExchange}.{ApplicationName}.leader.{scope}",
  durable: false, exclusive: true, autoDelete: true)`. Success ⇒ leader for as long as that
  connection lives. `RESOURCE_LOCKED` (405) ⇒ follower; retry every ~5s with jitter.
- On connection shutdown: raise `LeadershipChanged(false)` **before** attempting
  reacquisition, so leader-only work stops first.

**Fencing stays in Bitween's database.** RabbitMQ cannot hand out a monotonic counter, and
`Epoch` is process-local, so it is not a valid fence. On each `LeadershipChanged(true)`
Bitween does one statement per scope:

```sql
UPDATE cluster_leader SET node_id = @me, term = term + 1, acquired_on = now(), renewed_on = now()
WHERE scope = @scope RETURNING term;
```

That returned `term` is the fence: every leader-only write is guarded by
`WHERE term = @myTerm`. Clean split - **the bus provides the lock, the database provides the
fence** - and it means the fencing guarantee is identical no matter which bus supplies
election later. Combined with the existing `RunFlagUpdater` row-level mutex, split-brain
degrades to a liveness blip rather than a correctness bug.

**Default when election isn't registered:** `SingleNodeLeaderElection` in Bitween - always
leader, still takes a `term` from the DB. This is not a second real implementation; it exists
so single-node installs, `dotnet run`, and the integration fixture behave deterministically
without election timing in the test path.

**Sequencing note:** this needs a PR to the public `SW-Bus` repo (CI publishes
`SimplyWorks.Bus*` on merge to `main`) and a version bump from 8.1.11 in both
`SW.Bitween.Api.csproj` and `SW.Bitween.Web.csproj`. Phases 0-2 don't depend on election, so
that PR can land in parallel and only gates phase 3.

### 6.2 Placement and per-node enable/disable

Effective decision for (gateway, node):

```
run = dataSource.Active
   && !gateway.Inactive
   && node.GatewaysEnabled
   && node.Overrides["gateway:{id}"] != false
   && placement matches (All | Leader && IsLeader | Tagged && node.Tags ∩ tags | Explicit && id ∈ nodes)
```

Admin API: `POST /cluster/nodes/{nodeId}/gateways/{gatewayId}/{enable|disable}`,
`POST /cluster/nodes/{nodeId}/{drain|resume}`, `GET /cluster` (nodes, leader, per-node
running subscriptions and their state). Each mutation writes DB **then** broadcasts - DB is
the source of truth, the broadcast is only a latency optimization, so a node that was down
during the broadcast picks the change up from its next heartbeat reconcile.

### 6.3 Egress - review

#### What exists today (verified in code)

Outbound is **not** absent, but it is unmodeled and it has a durability gap:

| Location | Behaviour |
|---|---|
| `XchangeService.cs:418-421` (v2; `:413` on r8.0) | `if (!string.IsNullOrWhiteSpace(xchange.ResponseMessageTypeName) && responseFile != null && !responseFile.BadData) await _publish.Publish(xchange.ResponseMessageTypeName, responseFile.Data);` - raw string publish to the internal bus, **inline, before the `SaveChangesAsync()` that commits the `XchangeResult`** |
| `BitweenDbContext.SaveChangesAsync:399-407` | domain events published **after** `base.SaveChangesAsync` - commit first, publish second |

Both are **dual writes with no outbox**, in opposite directions:

- `ResponseMessageTypeName` publishes *before* the `XchangeResult` commits. Crash in between ⇒
  the downstream system received the message, Bitween has no record of success, and a
  reprocess/retry **publishes it again**. Silent duplicate.
- Domain events publish *after* commit. Broker unavailable ⇒ the `Xchange` exists and nothing
  ever processes it. Silent loss.

Neither is catastrophic today because the target is a co-located internal RabbitMQ that is
essentially always up, and duplicates on `InternalXchangeCreatedEvent` are mostly idempotent.
**Both properties disappear the moment the target is a client's broker over the public
internet.** So the review conclusion is: egress isn't a new risk introduced by this design,
it is an existing latent one whose blast radius external brokers multiply.

#### Why it is genuinely a big change

Ingress and egress are not symmetric, and the asymmetry is the whole difficulty:

> **Ingress:** the broker holds the message until Bitween commits. At-least-once is free -
> just persist, then ack.
> **Egress:** Bitween holds the only copy of the outcome and must guarantee it reaches the
> broker. At-least-once has to be *built*.

That means a **transactional outbox**, which is a new subsystem, not a handler:

1. **Outbox table + drain job.** `OutboundMessage` (payload ref, endpoint id, envelope
   metadata, dedupe key, attempt count, next-attempt-on, state) written **in the same
   transaction** as the `XchangeResult`; a Quartz `OutboxJob` drains it. New table, new job,
   new failure modes, plus interaction with placement/leadership (§6.2) so N nodes don't
   double-drain - solvable with the same `RunFlagUpdater`-style row claim.
2. **Retry ownership collides with `RetryPolicy`.** If a publish fails, is that an `Error` on
   the Xchange - which sends `DelayedRetry` back through **mapper and handler again**, re-doing
   any side effect the handler already performed (an FTP upload, an HTTP POST) - or is it a
   transport-local retry? It must be **transport-local**: retry the publish only. Reuse the
   `DelayStrategy` shapes from `SW.Bitween.Sdk/Model/AutoRetry/` but keep outbox records
   separate from `DelayedRetry`. Getting this wrong produces duplicate real-world side effects,
   and it is the single subtlest point in the whole design.
3. **The envelope must be dynamic, and that is a mapping problem.** Ingress normalizes down to
   `XchangeFile.Data` (a string). Egress needs topic/queue, key, headers, and sometimes
   partition - usually *derived from the payload* (device id → MQTT topic, order id → Kafka
   key). Static config fields can't express that. Recommendation: binding fields accept the
   **`{{partner.KEY}}` / `{{globals.SET.KEY}}` token syntax adapter properties already use**,
   extended with the outbound payload's promoted properties - reusing that mechanism and its
   existing authoring/preview components (§11.5) rather than introducing a second expression
   language. This is the piece most likely to be underestimated.
4. **Loop prevention.** Bitween consuming from broker A and publishing to broker B - where a
   client has a route back - creates cycles that will be discovered in production. Stamp
   `x-bitween-hop` and `x-bitween-origin-xchange` headers, enforce a configurable max hop,
   reject beyond it. Trivial to add now, painful to retrofit, and impossible for brokers where
   `SupportsHeaders = false` (SQS attributes work; raw MQTT 3.1.1 has no headers at all - so
   for those, the loop guard has to live in the payload or be declared unavailable).
5. **Blast radius and authorization.** Egress means Bitween *writes into client systems*.
   `DataSourceEndpoint.Direction` must be enforced server-side (an `Inbound` endpoint refuses
   publish), every publish audited, and per-endpoint rate limits available. An operator who
   configured a read-only connection to a client's production topic must not be able to
   publish to it by editing one subscription field.
6. **Ordering.** A parallel outbox drain destroys per-key ordering. If any client needs it,
   the drain must be single-flight per key or per endpoint - which caps throughput. This is
   §12.1 again, and egress is what turns that question from theoretical to blocking.
7. **The existing feature can't silently change semantics.** Folding
   `ResponseMessageTypeName` into the outbox would change delivery timing and duplicate
   behaviour for every current installation, including the Traxis-style ones. Keep it working
   exactly as-is; make the endpoint path **opt-in** via a new field; migrate deliberately later
   with the old behaviour available as a flag.

#### Recommended split - and it should come earlier than phase 6

| | Scope | Size | Guarantee |
|---|---|---|---|
| **6a. Inline publish** | `native.publishToEndpoint` handler in `SW.Bitween.NativeAdapters`, publishing to a `DataSourceEndpoint` with token-templated binding. Publish inline; failure = handler exception = normal `XchangeResult` error + existing `RetryPolicy`. No outbox. | S-M | at-most-once-ish, same as `ResponseMessageTypeName` today - **explicitly documented as such** |
| **6b. Durable outbox** | `OutboundMessage` table, `OutboxJob`, transport-local retry, dedupe key, loop guard, ordering mode, per-endpoint rate limit. `Subscription.ResponseEndpointId` routes through it. | L | at-least-once with dedupe key |

**6a belongs right after the first provider, not at phase 6.** It is a handler - it composes
with the existing pipeline and needs no core change - and bidirectional flow is exactly what
makes the MQTT and Pulsar demos land (a demo that only *reads* from a device network is half a
demo). Ship 6a for the demos, and gate 6b on the first client who needs guaranteed delivery,
by which point you will know their ordering answer too.

### 6.4 Topology provisioning

- `POST /data-sources/{id}/endpoints/{id}/plan` → `TopologyDiff` (dry run shown in UI).
- `POST .../apply` → `ITopologyManager.Apply` (idempotent).
- `GET /data-sources/{id}/browse?kind=queue|topic|exchange` → picker data.
- `AutoProvision = true` runs `Apply` before `Subscribe` during reconcile - and only on the
  node that is actually going to consume, guarded by leadership for `Placement = Leader`.

## 7. Delivery plan

Each phase is independently shippable and leaves the system working.

| Phase | Scope | Notes |
|---|---|---|
| **0. Abstractions** | `SW.Bitween.DataSources.Abstractions` (contracts, descriptor, capabilities, envelope), `IDataSourceProviderRegistry`, ALC plugin loader, `AddBitweenDataSources()` | no behaviour change; unit tests on descriptor validation + loader isolation |
| **1. Data sources** | `DataSource` + `DataSourceEndpoint` entities, EF config, migrations for **all three** providers, CRUD resources under `SW.Bitween.Api/Resources/DataSources/`, secrets via `SettingsProtector`, RBAC permission area, `POST /test-connection`, cache + `RevokeCacheMessage` invalidation | config-only; nothing consumes yet |
| **2. Supervisor + external RabbitMQ** | `MessagingSupervisor`, reconcile loop, `GatewayControlMessage` (`IListen`), `SW.Bitween.DataSources.RabbitMq` provider, `BusGateway.DataSourceId/EndpointId`, ingest → `SubmitFilterXchange` | `DataSourceId == null` still goes through `BusService`; first end-to-end external broker |
| **3a. Election (SW-Bus PR)** | `ILeaderElection` / `ILeaderElectionFactory` in `SW.Bus.RabbitMqExtensions`, `RabbitExclusiveQueueElection` + `AddBusLeaderElection()` in `SW.Bus`, Testcontainers test that kills the leader's connection and asserts single ownership | parallel to 1-2; publishes via CI, then bump 8.1.11 in Bitween |
| **3b. Cluster** | `ClusterNode`/`ClusterLeader` (per-scope rows), `ClusterHeartbeatJob` (Quartz), `SingleNodeLeaderElection` default, term fencing, placement + overrides, `/cluster` admin API | prerequisite for anything single-consumer |
| **4. Topology** | `ITopologyManager` for RabbitMQ, plan/apply/browse endpoints, `AutoProvision` | "create queues on the client's Rabbit" |
| **5. Providers** | Kafka → Event Hubs (verify over Kafka endpoint) → MQTT → Pulsar, then demand-driven (§8.2); one PR each, none touching core | Kafka spike happens *before* phase 0 freezes the contract |
| **5b. Egress (inline)** | `native.publishToEndpoint` with token-templated bindings - **interleaved with phase 5, before MQTT** (§6.3) | at-most-once, documented as such; unlocks two-way demos |
| **6. Egress (durable)** | `OutboundMessage` outbox + `OutboxJob` + transport-local retry + loop guard + `Subscription.ResponseEndpointId` | gate on the first client needing guaranteed delivery |
| **7. Node health** | `NodeHeartbeat` process metrics on `ClusterNode`, `/ops/nodes` + `/ops/nodes/{id}/connections`, per-connection counters, budget guards (§10) | extends the existing `Ops` resource family |
| **8. UI** | `ApiClient` contract + mock first, then http; DataSources list/editor with **descriptor-driven forms** (reusing the `AdapterConfig` pattern), test-connection, endpoint editor, gateway picker, Nodes page (§11) | v2 `ClientApp` stack: TanStack Query + Tailwind + Headless UI |
| **9. Tests + observability** | Testcontainers: RabbitMQ, Redpanda (Kafka), LocalStack (SQS), Mosquitto (MQTT); reconcile-idempotency and election tests (kill leader, assert single owner); OpenTelemetry spans/metrics per subscription | |

## 8. Provider priorities

### 8.1 The leverage rule: prioritize *protocols*, not *products*

The instinct is to write one provider per brand. Resist it - several brands are the same wire
protocol, and one protocol provider unlocks a whole column of them:

| Build this one | And you largely cover |
|---|---|
| **Kafka** (`Confluent.Kafka`) | Apache Kafka, Redpanda, Confluent Cloud, AWS MSK, Aiven, **and Azure Event Hubs** (Kafka-compatible endpoint, Standard tier and up) |
| **AMQP 1.0** (`AMQPNetLite`) | Azure Service Bus, ActiveMQ Artemis, ActiveMQ 5.x, Solace, Qpid, and parts of IBM MQ |
| **MQTT** (`MQTTnet`) | Mosquitto, EMQX, HiveMQ, VerneMQ, AWS IoT Core, Azure IoT Hub (partially) |

Caveat, stated honestly: a generic protocol provider gets you connect/subscribe/publish, not
the proprietary extras. Azure Service Bus sessions, scheduled messages, and dead-letter
subqueues are poorly expressed over raw AMQP 1.0. So the rule is **generic protocol provider
first, thin product-specific provider later only when a client needs the extras** - and
because `ProviderCapabilities` is declared per provider, both can coexist in the registry
without core caring.

### 8.2 Recommended order, against actual demand

Known demand: **external RabbitMQ** (paying client, hard requirement), **Kafka** (client using
it heavily), **Azure Event Hubs** (client, already served by a bespoke custom connector),
**Pulsar** (Tuya - capability demo), **MQTT** (capability demo). Everything else has no named
client and drops to demand-driven.

| # | Provider | Effort | Driver | What it stresses / notes |
|---|---|---|---|---|
| **0** | **RabbitMQ (external)** | S | paying client | Already phase 2 - it is the vehicle that proves supervisor + reconcile + placement. Cheapest provider because the concepts already match Bitween's model. Do not let it slip. |
| **1** | **Kafka** | L | heaviest real load, **and** the contract stress test | Consumer groups, partitions, **offset commit instead of per-message ack**, no native DLQ, ordering per partition. If the contract survives Kafka it survives everything - which is why it must be first among the plugins regardless of demand. |
| **2** | **Azure Event Hubs** | **XS-S, or M** | existing client | *Verification task before a build task:* Event Hubs exposes a **Kafka-compatible endpoint** (Standard tier and up, SASL_SSL/PLAIN with the connection string), so provider #1 may cover this client for free. Spend a day proving it against their namespace. Fall back to a native `Azure.Messaging.EventHubs` provider only if they are on Basic tier or need checkpoint-store/Capture semantics. Either way, **mine the existing custom connector**: its config surface is a ready-made `ProviderDescriptor` and its quirks are already client-validated. |
| **3** | **MQTT** | S-M | demo, and the cheapest of the demo set | Smallest effort with the most visible payoff, because MQTT demos are inherently **two-way** - subscribe to telemetry *and* publish a command back to a device. Pair it with egress 6a (§6.3); that pairing is what makes the demo land. Capability-wise it is the first real test of `SupportsCompetingConsumers` (plain MQTT has none ⇒ forced `Placement = Leader`; MQTT 5 shared subscriptions lift it) and of `SupportsHeaders = false` on 3.1.1. |
| **4** | **Apache Pulsar** | M-L | Tuya demo | `DotPulsar` is the official .NET client and adequate. Better than I first credited it: Pulsar's subscription types - exclusive / failover / shared / key-shared - map almost exactly onto `SupportsExclusiveConsumer`, `SupportsCompetingConsumers`, and ordered-key placement, so it is arguably the **best showcase** of why the capability model exists rather than a per-broker hack. |

**Demand-driven, no named client - build when one appears:** Azure Service Bus (or generic
AMQP 1.0, which also covers ActiveMQ Artemis and Solace), Amazon SQS (S - cheapest real
provider if an AWS client shows up), Redis Streams (S), NATS/JetStream (S-M), Google Pub/Sub
(M), IBM MQ (banking/gov value, but needs the licensed client library and a test environment).

Two corrections to my earlier ranking, now that demand is known: **Pulsar is not a defer** -
it has a named demo target and it exercises the capability model better than most. And
**Event Hubs is not the wrong Azure target** - it has a paying client; my point was only that
Service Bus is the more common *enterprise messaging* ask, and that stands as a reason to keep
Service Bus on the demand-driven list rather than a reason to demote Event Hubs.

### 8.3 One recommendation that costs almost nothing

**Spike the Kafka provider before phase 0 freezes the contract** - even if you don't ship it
until tier 1. Not the full provider: just write its `ProviderDescriptor` and a throwaway
subscribe loop against Redpanda in a scratch branch. Kafka is the provider whose model differs
most from RabbitMQ (offsets, not acks; groups, not queues), so it is the one that will expose
a Rabbit-shaped assumption baked into `IDataSourceConnection`. Finding that in a two-day spike
is cheap; finding it in phase 5 means a breaking change to a published abstractions package
and every provider written against it.

### 8.4 Worth noting: the highest-demand "providers" may not be brokers

For an integration engine, real-world client asks are often SFTP drops, database
polling/CDC, IMAP mailboxes, and file shares - not message brokers. Those are **pull-based**
and Bitween already handles them through `ReceivingJob` + receiver adapters, which is the
right home; don't stretch `DataSource` to cover them in v1. But keep it in mind as a
deliberate boundary: if pull sources later want the same connection-management, per-node
placement, and leader election that this design gives brokers, the clean move is a sibling
`IPollingProvider` in the same registry that the supervisor schedules via Quartz instead of
subscribing to - reusing `DataSource`, placement, and cluster control unchanged.

## 10. Node health and resource pressure

### 10.1 What v2 already gives, and the gap

v2 ships `SW.Bitween.Api/Resources/Ops/` - `Summary`, `Consumers`, `Queues`, `Retries`,
`DeadLetters`, `Alerts` - all thin handlers over SW.Bus's `IBusDashboardDataService`, gated on
`Permissions.Monitoring.View`, and surfaced by `QueueHealthPage.tsx`.

**But it is queue health, not node health.** `IBusDashboardDataService` reads the RabbitMQ
**Management API**, so every number is cluster-wide broker state: queue depth, incoming/ack
rates, backpressure, dead letters. `ConsumerHealth.TotalNodes` is a *count of consumers on a
queue*, not a view of the processes. There is **no per-process telemetry anywhere today** -
nothing knows a node's memory, thread count, or how many broker connections it holds. Which is
exactly the pressure this feature introduces: every external data source is a live TCP
connection, a client object, receive buffers, and (for Kafka/Pulsar) per-partition fetch
buffers that dwarf a RabbitMQ channel.

### 10.2 Heartbeat carries the metrics

`ClusterHeartbeatJob` (§6, Quartz, every 15-30s) is already writing `ClusterNode.LastHeartbeatOn`.
Have it write process telemetry in the same row - no new job, no scraping, no Prometheus
dependency:

```csharp
// on ClusterNode
public long   WorkingSetBytes { get; set; }      // Process.WorkingSet64
public long   GcHeapBytes { get; set; }          // GC.GetTotalMemory(false)
public long   GcTotalAllocatedBytes { get; set; }
public int    Gen2Collections { get; set; }      // growth rate is the leak signal
public double CpuPercent { get; set; }           // sampled between heartbeats
public int    ThreadCount { get; set; }
public int    ThreadPoolQueueLength { get; set; } // >0 sustained = starvation, the real symptom
public int    OpenConnections { get; set; }      // data-source connections held
public int    ActiveSubscriptions { get; set; }
public long?  ContainerMemoryLimitBytes { get; set; } // GCMemoryInfo.TotalAvailableMemoryBytes
```

`ThreadPoolQueueLength` and `ContainerMemoryLimitBytes` matter most in practice. Provider client
libraries spawn their own threads and do blocking work; the first sign of an over-subscribed node
is thread-pool starvation, not memory. And a working set of 900 MB means nothing until you know
whether the cgroup limit is 1 GB or 8 GB.

**No single metric covers both providers.** Kafka's `librdkafka` allocates *native* memory on
*native* threads, so `GcHeapBytes`, `Gen2Collections`, and `ThreadPoolQueueLength` all stay flat
while the process grows toward an OOM kill - only `WorkingSetBytes` and `CpuPercent` see it.
RabbitMQ is the mirror image: managed heap and thread-pool pressure are the signals. Collect all
of them and never treat a healthy GC heap as a healthy node.

### 10.3 Per-connection detail

`GET /ops/nodes` (list + metrics) and `GET /ops/nodes/{nodeId}/connections`, the latter served
from the supervisor's live in-memory map rather than the DB, since it is per-process state:

| Field | Source |
|---|---|
| data source, endpoint, provider | supervisor map |
| state | `IMessageSubscription.State` (`Starting`/`Running`/`Degraded`/`Stopped`) |
| connected since, reconnect count, last fault | supervisor bookkeeping |
| messages in/sec, in flight, last message at | ingress pipeline counters |
| lag / backlog | `ITopologyManager` where the provider can report it (Kafka consumer lag, Rabbit queue depth); `null` when it can't - a **capability**, not a guess |

Cross-node aggregation: fan out over the node list via `IBroadcast`, or simpler and more robust,
have each node write its own connection snapshot on heartbeat and read the union from the DB.
Prefer the DB - one query, works when a node is unreachable, and it makes "node X went dark
holding four connections" visible instead of a timeout.

### 10.4 Guard rails, because this is the actual risk

Visibility is necessary but not sufficient; add **budgets** so a misconfiguration can't OOM a node:

- `BitweenOptions.MaxDataSourceConnectionsPerNode` (default ~25) and
  `MaxConcurrentInboundPerNode` - the supervisor refuses to start beyond them, records a
  `Degraded` reason, and raises an Ops alert instead of silently over-committing. **Weight the
  budget per provider** rather than counting connections flat: a descriptor-declared
  `ResourceWeight` (rabbitmq 1, kafka ~8) summed against the budget, because a Kafka handle costs
  an order of magnitude more memory and several times more threads than an AMQP connection - see
  [provider-plan-rabbitmq-kafka.md](provider-plan-rabbitmq-kafka.md) §2.4.
- **Per-subscription prefetch/fetch-size is mandatory in every provider descriptor.** An
  unbounded Kafka `fetch.max.bytes` × partitions × topics is the realistic OOM path, and it is a
  config default, not a code bug.
- Memory-pressure backpressure: when `WorkingSetBytes / ContainerMemoryLimitBytes` crosses a
  threshold, the supervisor stops *accepting* (pauses subscriptions) rather than being killed -
  and, if `Placement` allows, sheds to another node.
- Reuse the existing `AlertEvaluator`/Ops alert surface so node alerts appear where operators
  already look, rather than in a new place.

## 11. UI: the real challenges

### 11.1 Decision: dedicated per-provider components, descriptor stays server-side

v2 has two descriptor-driven form precedents - `GET /adapters/{id}/GetStartupValues` →
`AdapterConfig.tsx`, and `SettingsCatalog.All` → `SettingsPage.tsx`. My first instinct was to
render provider config generically from a `ProviderDescriptor` the same way. **Having read
`AdapterConfig.tsx`, that is the wrong call. Build a dedicated component per provider.**

**The generic renderer's payoff is zero in this architecture.** The only reason to build a
schema-driven form engine is to decouple UI releases from provider releases - to let a provider
appear without rebuilding the frontend. But v2 compiles `ClientApp` **into `SW.Bitween.Web`**,
and providers load at startup from that same deployment (§4.1). Adding a provider is already a
rebuild of the one artifact that also contains the UI. You would be paying a permanent
abstraction tax for decoupling that the deployment model makes impossible to use. (If providers
are ever shipped to customers who can't rebuild the UI, revisit this - that is the one scenario
that flips it.)

**The generic path has already broken down once, visibly.** `AdapterConfig.tsx` is **460 lines**
to render the *simplest possible* descriptor - flat string keys with `optional`/`default`/
`secret`/`description` - and every field renders as **one control type, a textarea**. There are
no enums, booleans, numbers, conditional fields, or cross-field rules. And it already contains
`adapter.id === "NativeJSONMapper"` → escape to a bespoke editor: a hardcoded per-implementation
special case, which is exactly the registry pattern in its least maintainable form.

Broker connections need all the things that descriptor can't express: SASL mechanism as an enum
that *changes which fields appear*, TLS toggle with dependent cert fields, IAM-role vs
access-key auth modes, numeric prefetch/fetch sizes with bounds, and validation that spans
fields. Extending the generic renderer to cover that means building a form framework. Five
hand-written forms are less code, better UX, and no shared abstraction to break.

**And the primitives for hand-written forms are already complete.** `components/ui/forms.tsx`
ships `Field`, `TextInput`, `PasswordInput`, `Checkbox`, `Select`; `basics.tsx` ships `Button`,
`Badge`, `FormError`, `EmptyState`; plus `Panel`, `SearchSelect`, `KeyValueEditor`,
`SummaryDisclosure`, and a wizard kit (`WizardShell`, `StepNav`, `OptionCard`,
`usePersistentDraft`). A Kafka connection form is **composition, not new components** - a few
hundred lines of layout.

**Keep `ProviderDescriptor` on the backend regardless.** Its job changes from *rendering
instruction* to **validation and metadata contract**, and it is still required:

- the API must reject invalid `Settings`/`Binding` from *any* client, not just this UI (§3.1);
- `secret: true` is what routes a field through `SettingsProtector`;
- `GET /providers` powers the provider picker (labels, icons, blurbs) and a **generic fallback
  form** for a provider with no dedicated component, so an unknown provider degrades to a plain
  key/value editor instead of being unusable.

**Secrets: reuse `SettingsProtector`, not `AESCryptoService`.** It is the newer design -
AES-GCM, PBKDF2 100k iterations, `enc.v1:` prefix, fresh salt+nonce per value, passphrase from
`BitweenOptions.SettingsEncryptionKey` (configuration only, never stored). Inherit its rule too:
`IsConfigured == false` ⇒ **refuse to store**. For a broker credential that means a data source
with a password cannot be saved at all on an install with no passphrase configured - surface
that as a blocking, explanatory validation error at the top of the form, not a silent drop.

**Note what `SettingsCatalog` deliberately excludes:** its membership rule is that a setting
qualifies *only* if consumers read it from the options singleton per call - "anything captured
once during `Startup.ConfigureServices` - **the bus**, CORS, storage, JWT, Quartz, the DB
provider - stays environment-only." Broker connections are exactly that class today, which is
why `DataSource` must be its own entity with its own reconciliation (§5) rather than a Settings
row. That is the feature, stated in the codebase's own words.

### 11.2 Test connection is the highest-value control, and it is not trivial

`POST /data-sources/test` must accept an **unsaved draft** (otherwise you can't test before
committing bad credentials), which means: server-side timeout (~10s) so a wrong host can't hang
a request thread; a distinguishing result, not a boolean -
`{ ok, stage: dns|tcp|tls|auth|authorize|topology, elapsedMs, message, providerDetail }`, because
"auth failed" and "TLS handshake failed" send an operator to completely different places; and
**it must run on the node that will hold the connection**, or it proves nothing about a firewall
rule that only blocks node B. Route it through the supervisor on the target node (§6.2) and let
the operator pick, defaulting to the placement target.

Secret handling in a draft test: never round-trip ciphertext to the browser. When editing an
existing source, send a sentinel (`"__unchanged__"`) and have the server substitute the stored
value.

### 11.3 Making external gateways visible

v2 already has `pages/bus-gateways/` (`BusGatewayPage`, `BusGatewayNewPage`, `AddRouteWizard`,
`EditRoutePage`) built for the internal-only model. Extend rather than fork:

- **Data source column/badge** on the gateway list - provider icon + name, or "Internal bus" for
  `DataSourceId == null`. An operator must never have to open a record to learn which broker it
  listens to.
- **Live state chip** per gateway: `Running` / `Degraded` / `Stopped` / `Not placed here`, plus
  the owning node when placement is `Leader`. Poll via TanStack Query with an interval; nothing
  else in the app needs websockets, so don't introduce them for this.
- **Nodes page** under "Operate" next to Queue health: node table with the §10.2 metrics, leader
  badge, per-node enable/disable, and an expandable per-connection list. Gate on
  `monitoring.view`; gate the toggles on a new write permission.
- **Follow the mock-first contract.** The UI is written against a single `ApiClient` interface
  with `api/mock` and `api/http` implementations - build the mock first and the whole DataSource
  UI is reviewable before any endpoint exists. Genuinely worth exploiting on a feature this
  large.
- **RBAC:** v2 gates every handler on `Permissions.*`. A new `data-sources` permission area
  (`view` / `edit`) plus a `monitoring.manage` for node toggles must be added to the catalog and
  to `nav.ts`, or the pages silently won't render for anyone.

### 11.4 The provider UI registry

One registry in the ClientApp, keyed by provider - the same idea as `nav.ts` being the single
source of the information architecture:

```ts
export interface ProviderUi {
  key: string;                    // matches IDataSourceProvider.Key
  label: string;                  // "Apache Kafka"
  icon: LucideIcon;
  blurb: string;                  // one line for the OptionCard in the new-source wizard
  ConnectionForm: FC<{ value: Settings; onChange: (s: Settings) => void; disabled: boolean }>;
  BindingForm: FC<{ direction: EndpointDirection; value: Binding; onChange: (b: Binding) => void }>;
  defaults: () => { settings: Settings; binding: Binding };
  summarize: (s: Settings) => string;   // "3 brokers · SASL_SSL" for the list row
  docsHref?: string;
}

export const PROVIDERS: Record<string, ProviderUi> = { rabbitmq, kafka, eventhub, mqtt, pulsar };
```

What stays **shared, written once**: the data-source page shell, the test-connection panel
(§11.2), the endpoint list, the secret field, the reference-token menu, and the wizard chrome
(`OptionCard` grid over `PROVIDERS` is the provider picker). What is **per provider**: only the
field layout inside `ConnectionForm` / `BindingForm`. Unknown key ⇒ `GenericProviderForm` driven
by the descriptor.

Bindings are where dedicated components earn their keep hardest. "Kafka topics + consumer group +
starting offset + fetch bounds", "exchange + routing key + queue to declare + prefetch", "MQTT
topic filters + QoS + shared-subscription group", and Pulsar's four subscription types are
different *shapes*, not different fields. `MatchExpressionEditor.tsx` and `ScheduleEditor.tsx`
are the precedent: a real domain-specific editor sitting beside the generic controls.

### 11.5 Extract three things out of `AdapterConfig.tsx` first

Most of those 460 lines are not the form - they are reusable machinery currently trapped inside
it, and **extracting them before five provider forms exist is much cheaper than after**:

| Extract | Why every provider form wants it |
|---|---|
| `SecretField` | mask + **Replace** button; the exact interaction a broker password needs |
| `ReferenceMenu` | searchable `{{globals.…}}` / `{{partner.…}}` token inserter, inserts at the caret |
| `ReferenceHints` | resolves the tokens in a value inline - globals to their literal, partner keys to "defined by 3 of 7 partners" with drill-down |

**This also corrects the egress recommendation in §6.3.** I suggested Scriban templates from the
MappingEditor for dynamic binding fields. Wrong tool: adapter properties **already** support
`{{partner.KEY}}` / `{{globals.SET.KEY}}` interpolation, with UI affordances for authoring *and*
previewing them. Dynamic egress bindings (device id → MQTT topic, tenant → Kafka key) should use
that same token syntax and those same two components - one templating mechanism in the product,
not two.

## 12. Open questions

1. **Ordering.** Do any client integrations need per-key ordering end to end? If yes, the
   ack-then-pipeline model needs a per-key serialization gate, which is a real design change
   - better to know now than at phase 5.
2. **Multi-tenancy.** Should a `DataSource` be scoped to a `Partner`, or shared with the
   partner chosen per gateway route (today's model)? Affects the API surface.
3. **Duplicate suppression.** `Document.DuplicateInterval` exists; does it apply to external
   ingest, and keyed on what - `ProviderMessageId` or payload hash?
4. **Credentials at rest.** Is `AESCryptoService` acceptable for client broker credentials,
   or do we need per-install KMS / Azure Key Vault references (`UseAzureManagedIdentity`
   already hints at that direction)?
5. **Per-node addressing in SW.Bus.** Targeted control messages currently require
   listener-side filtering because `NodeExchange` uses one shared routing key. Worth folding
   into the same SW-Bus PR as election: add a per-node routing key
   (`{NodeRoutingKey}.{NodeId}`) plus `IBroadcast.BroadcastTo(nodeId, message)` - cheap and
   backward compatible, since the existing binding stays.
6. **Election scope granularity.** Per-gateway (`"gateway:{id}"`) spreads load best but means
   N exclusive queues for N gateways; per-data-source is coarser but keeps one broker
   connection firmly owned by one node. Recommendation: **per data source**, since a
   connection is the expensive resource and gateways on the same broker should share it.
   Worth confirming against expected gateway counts per install.
