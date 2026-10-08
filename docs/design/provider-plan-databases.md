# Provider Plan: Resident Database Adapters (PostgreSQL, MySQL, SQL Server, Oracle)

A relational data source that Bitween can **read from**, **write to**, and **look inside**, served
by a long-lived adapter process that owns a real ADO.NET connection pool.

Companion to [provider-plan-rabbitmq-kafka.md](provider-plan-rabbitmq-kafka.md) and
[external-brokers-architecture.md](external-brokers-architecture.md); the resident runtime itself
is specified in `SW-Serverless/docs/resident-adapters-design.md`.

---

## 1. Verdict

Build **four adapter binaries over one shared core library**, published as resident data source
providers of `DataSourceKind.Relational`:

| Adapter id | Driver | Package |
|---|---|---|
| `bitween.db.postgresql` | Npgsql | `SW.Bitween.Adapters.Db.PostgreSql` |
| `bitween.db.mysql` | MySqlConnector | `SW.Bitween.Adapters.Db.MySql` |
| `bitween.db.sqlserver` | Microsoft.Data.SqlClient | `SW.Bitween.Adapters.Db.SqlServer` |
| `bitween.db.oracle` | Oracle.ManagedDataAccess.Core | `SW.Bitween.Adapters.Db.Oracle` |

all deriving from `SW.Bitween.Adapters.Db.Core` (`DbResidentAdapterBase`), which owns everything
that is not engine-specific: the command surface, parameter binding, paging, the statement
allow-list, telemetry, and the capability/catalog contract.

**Shape: exclusive-per-data-source resident, one instance per node** — not the pooled-worker shape.
The whole point is that the ADO.NET pool lives inside the adapter process and survives between
Xchanges; a pooled worker checked out per message defeats that, and the current pool
implementation is keyed in a way that is actively unsafe for this (§7.1).

**Three things do not exist yet and are the real work**, in descending order of risk:

1. **Placement.** `BusProviderSupervisor` grants an *exclusive cluster-wide lease* per data source.
   That is correct for a broker queue and wrong for a database: every node must hold its own pool.
   A `Placement` axis (`exclusive` | `per-node`) has to be added before a DB source can run on more
   than one replica (§7.2).
2. **A subscription cannot reference a data source.** Only `BusGateway` has `DataSourceId`. Without
   it, a DB handler on a subscription would carry its own connection string in adapter properties —
   which means no shared pool, credentials duplicated per subscription, and no health page (§7.3).
3. **Result sets do not fit the invoke envelope.** One request/one response is fine for a broker
   `Discover`; a `SELECT` over a million rows is not. Paging commands plus a payload-to-cloud-files
   escape hatch are required (§6.4).

Everything else — resident host, supervisor, secret storage, the provider-descriptor form
generation, `Test`, `Inspect`, `Telemetry`, the three-runtime `IAdapterInvoker` — already exists and
is reused unchanged.

---

## 1a. Status

| Phase | State |
|---|---|
| 0 — SW-Serverless unblocks | **Done and published in 8.1.23.** [#131](https://github.com/simplify9/SW-Serverless/pull/131): `AdapterSpec.PoolKey`, `IAdapterContext.Get/SetStateAsync`, `IAdapterStateStore`. [#132](https://github.com/simplify9/SW-Serverless/pull/132): per-invocation properties (`ValueOf` / `InvocationValues`), without which a subscription cannot tell a shared instance which statement to run |
| 0b — Bitween host | **Done**: `adapter_state` table + `BitweenAdapterStateStore`, `DataSource.Placement` (Auto/Exclusive/PerNode), `Subscription.DataSourceId`, pipeline routes a bound subscription to the data source's running instance, migrations for all three providers |
| 1 — core + first engine | **Done for Oracle**: `SW.Bitween.Adapters.Db.Core` + `SW.Bitween.Adapters.Db.Oracle`, with integration tests against a real Oracle container |
| 2 — receiving | **Done**: five receive modes, host-held cursor, mark-processed |
| 3 — PostgreSQL, MySQL, SQL Server | Not started — each is a driver, a connection string, a catalog query and a capability list over the same core |
| 4 — UI | Not started |
| 5 — push ingress | Not started; declared `ChangeNotification = false` |

Note that Oracle came first rather than PostgreSQL, against the original sequencing: it is the
engine that matters most here, and it is also the awkward one — REF CURSOR, service name versus
SID, `ALL_*` versus `DBA_*` — so proving the core against it says more about the core than
PostgreSQL would have.

---

## 2. What already exists (and is therefore not in scope)

| Machinery | Where | What it gives the DB adapters |
|---|---|---|
| Resident host, supervision, crash-loop quarantine, resource limits | `SW.Serverless/Resident/*` | process lifetime, heartbeats, restart policy, memory/CPU ceilings |
| `IResidentAdapter` + `IAdapterContext` | `SW.Serverless.Sdk/Resident/*` | `StartAsync`/`StopAsync`/`GetStatusAsync`, `PublishAsync` ingress, logging, metrics |
| `DataSource` entity with `Kind = Relational` already declared | `SW.Bitween.Api/Domain/DataSources/DataSource.cs` | connection config, secrets, limits, health columns |
| Provider descriptor generated from adapter attributes | `SW.Bitween.Adapters.Shared/AdapterSettingAttribute.cs`, `Services/DataSources/DataSourceProviderCatalog.cs` | the connection form renders itself; `Kind = "Relational"` already filters it away from bus gateways |
| `Test`, `Inspect`, `Telemetry` endpoints | `SW.Bitween.Api/Resources/DataSources/*` | staged connection test, read-only command relay, live process + adapter stats |
| Three-runtime dispatch | `SW.Bitween.Api/Services/Adapters/{IAdapterRuntime,AdapterInvoker,ResidentAdapterRuntime}.cs` | a resident adapter is already reachable as Mapper / Handler / Validator / Receiver |
| Receiver session protocol | `Services/ReceivingJob.cs` — `Initialize`, `ListFiles`, `GetFile`, `DeleteFile`, `Finalize` | polling ingestion maps onto it without a new pipeline (§5.3) |

`[AdapterKind("datasource")]` is the stamp the installer reads (`ProviderKinds = ["bus",
"datasource"]` in `DataSourceProviderCatalog`), so third-party DB providers are discoverable
without the `bitween.` prefix.

---

## 3. How other middleware does this, and what we take

| Product | What it does | Take / leave |
|---|---|---|
| **MuleSoft Database Connector** | Generic JDBC connector; **no pooling by default** — every statement opens and closes a connection unless a pooling profile is configured. Stored procedures stream their `ResultSet`, and the connection is held until the flow ends or the stream is drained. | **Take** the lesson inverted: pooling is the default and the reason the adapter is resident. **Take** the warning: a streamed result pins a connection, so paging must have a server-side TTL that closes abandoned cursors. |
| **Kafka Connect JDBC source** | Four polling modes — `bulk`, `incrementing`, `timestamp`, `timestamp+incrementing`; cursor advanced per poll; cannot see deletes, and can miss a row updated twice inside one poll window. | **Take** the four modes verbatim as `ReceiveMode` — they are the vocabulary operators already know. **Take** the honesty: the UI must state that polling cannot see deletes. |
| **Debezium** | Reads the transaction log; captures inserts, updates and deletes in commit order, no polling. | **Leave out of v1.** Log-based CDC needs replication slots, privileges, and a durable offset store — a separate provider (`bitween.cdc.*`) later, not a mode of this one. Declare it as a capability that reports `false`. |
| **Airbyte protocol** | Connectors implement exactly four verbs: `spec`, `check`, `discover` (→ catalog of streams + JSON schema), `read(config, catalog, state)`. | **Take the whole shape.** `spec` = the provider descriptor we already generate; `check` = `TestConnection`; `discover` = `Discover`; `read` + `state` = the receiver session plus a host-held cursor (§5.3). This is the single most useful borrowing here. |
| **Apache Camel SQL / SQL-Stored** | `onConsume` runs a statement against each consumed row (mark-processed) in the consuming transaction; the stored-procedure component is **producer-only**. | **Take** `onConsume` as `MarkProcessedStatement`, bound to `DeleteFile` in the receiver session. |
| **ADO.NET `DbConnection.GetSchema`** | Every provider exposes `MetaDataCollections`, `Tables`, `Columns`, `Procedures`, … and you can ask which collections exist. | **Take** as the first-pass implementation of `Discover`, with engine-specific `information_schema` / `ALL_OBJECTS` queries where `GetSchema` is thin (Oracle counters, Postgres functions vs procedures). |
| **Push-based DB events** — Postgres `LISTEN/NOTIFY` (payload capped ~8 KB), Oracle CQN / AQ, SQL Server Service Broker + `SqlDependency`, MySQL binlog | Each engine has a native change-notification channel, with wildly different privilege and setup costs. | **Take** as a capability-gated *optional* ingress that exploits residency: a process that already stays alive can hold a `LISTEN` for free. Phase 4, off by default, and the payload cap means the notification is a *trigger to re-poll*, not the message itself. |

The pattern common to all of them and to this plan: **the connector declares what it can do; the
platform never assumes.** That is exactly what `AdapterSettingsAttribute` already does for the
connection form, extended to runtime features.

---

## 4. Visibility: the `Describe` / `Discover` split

Two different questions, two commands. Conflating them is the mistake — "what can this engine do"
is answerable offline and cheaply cached; "what is in this database" is a live, privilege-dependent,
potentially enormous answer.

### 4.1 `Describe()` — engine capabilities

Static per adapter build + a few probes at `StartAsync`. Returned to the UI so it can grey out what
this engine cannot do, rather than offering it and failing at run time.

```csharp
public class DbCapabilities
{
    public string Engine { get; set; }              // PostgreSQL
    public string ServerVersion { get; set; }       // probed at connect
    public string[] SupportedObjects { get; set; }  // table, view, materialized_view, procedure,
                                                    // function, package, sequence, synonym, type

    public bool StoredProcedures { get; set; }      // true everywhere except older MySQL variants
    public bool ProcedureOutParameters { get; set; }
    public bool ProcedureResultSets { get; set; }   // Oracle needs a REF CURSOR; PG needs a function
    public bool MultipleResultSets { get; set; }
    public bool NamedParameters { get; set; }       // Oracle :p, SQL Server @p, MySQL ?
    public bool Transactions { get; set; }
    public string[] IsolationLevels { get; set; }
    public bool BulkCopy { get; set; }              // COPY / LOAD DATA / SqlBulkCopy / array binding
    public bool Merge { get; set; }                 // MERGE / ON CONFLICT / ON DUPLICATE KEY
    public bool Returning { get; set; }             // RETURNING / OUTPUT
    public bool Json { get; set; }
    public bool ArrayTypes { get; set; }
    public bool ChangeNotification { get; set; }    // LISTEN/NOTIFY, CQN, Service Broker
    public bool LogBasedCdc { get; set; }           // false in v1, everywhere
    public bool SchemaDiscovery { get; set; }
    public bool RowCountEstimates { get; set; }
    public string[] ReceiveModes { get; set; }      // bulk, incrementing, timestamp, both, notify

    /// Things the connection is allowed to do, probed with the actual credentials —
    /// a capability the engine has and the login does not is a capability we do not have.
    public string[] Privileges { get; set; }
    public Dictionary<string, string> Details { get; set; }
}
```

The privilege probe matters more than the feature list. "Oracle supports CQN" is useless if the
configured user lacks `CHANGE NOTIFICATION`; the operator needs to be told that at configure time,
by the Test button, not at 3 a.m.

### 4.2 `Discover(DiscoverRequest)` — catalog

Paged and filtered, never "everything". Backed by `GetSchema` where it is adequate and by
engine-specific catalog queries where it is not.

```csharp
public class DiscoverRequest
{
    public string ObjectType { get; set; }   // table | view | procedure | function | sequence | package
    public string Schema { get; set; }       // null = every schema the login can see
    public string NameLike { get; set; }
    public bool IncludeColumns { get; set; } // off by default: columns for 4,000 tables is not a menu
    public bool IncludeRowCounts { get; set; } // estimates only — never SELECT COUNT(*)
    public int Skip { get; set; }
    public int Take { get; set; } = 200;
}
```

Returned objects carry name, schema, type, columns (name, db type, CLR type, nullable, length,
precision, key/identity flags), and for routines the parameter list with direction. Counters —
sequences in PG/Oracle, `AUTO_INCREMENT` in MySQL, identity in SQL Server — are one object type, so
"what is the next value" is one question rather than four.

`Describe` and `Discover` both go through the existing `Inspect` endpoint, whose allow-list gains
them (§7.4). Both are strictly read-only; neither may ever be reachable with a statement body.

---

## 5. The command surface

Everything below is a method on `DbResidentAdapterBase`, reachable through
`IAdapterInvoker`/`IAdapterLease`. Grouped by which Bitween role calls it.

### 5.1 Configuration / operations (no role — called by the DataSources endpoints)

| Command | Purpose |
|---|---|
| `TestConnection()` | staged: resolve → connect → authenticate → server version → privilege probe → each configured statement `Prepare`d. Returns the `DataSourceTestResult` stage list the UI already renders. |
| `Describe()` | §4.1 |
| `Discover(req)` | §4.2 |
| `Explain(req)` | plan for a configured statement; capability-gated |
| `GetStats()` | pool counters, per-statement call counts and latency percentiles |

### 5.2 Handler / Mapper role (Bitween → database)

| Command | Semantics |
|---|---|
| `Execute(StatementRequest)` | non-query; returns affected rows and `RETURNING`/`OUTPUT` rows when supported |
| `Query(StatementRequest)` | result set as JSON; `MaxRows` enforced; paging via `Cursor` (§6.4) |
| `Call(ProcedureRequest)` | stored procedure/function; IN/OUT/INOUT binding, `REF CURSOR` unwrapping on Oracle |
| `Batch(BatchRequest)` | ordered statements in **one transaction**, all-or-nothing |
| `BulkLoad(BulkRequest)` | `COPY` / `SqlBulkCopy` / `LOAD DATA` / array binding — capability-gated, falls back to a batched insert with a warning |

`StatementRequest` never carries raw SQL by default:

```csharp
public class StatementRequest
{
    public string Name { get; set; }                       // key into the configured statement set
    public string Sql  { get; set; }                       // rejected unless AllowAdHocSql = true
    public Dictionary<string, object> Parameters { get; set; }
    public int? TimeoutSeconds { get; set; }
    public string IsolationLevel { get; set; }
    public int MaxRows { get; set; }
}
```

### 5.3 Receiver role (database → Bitween), polling

Maps onto the existing `ReceivingJob` session without touching the pipeline:

| Session call | DB adapter behaviour |
|---|---|
| `Initialize` | open a transaction if `MarkProcessedStatement` is set and the engine supports it |
| `ListFiles` | run the select for the configured `ReceiveMode`, return one opaque row key per row (PK, or PK+cursor value) |
| `GetFile(key)` | return that row (or its batch) as an `XchangeFile` |
| `DeleteFile(key)` | run `MarkProcessedStatement` — Camel's `onConsume`. No statement configured and no cursor mode = fail loudly at configure time rather than re-reading the same rows forever |
| `Finalize` | commit, advance and persist the cursor, close |

`ReceiveMode` is Kafka Connect's vocabulary: `bulk`, `incrementing`, `timestamp`,
`timestamp+incrementing`, plus `marker` (a processed-flag column, the mode most enterprise
integration tables actually use).

**Cursor state is the open design point.** The adapter must not be the system of record — it is
restartable and there may be one instance per node. Two options:

* **(A) Host-held cursor (recommended).** Add `Task<string> GetStateAsync(string name)` /
  `SetStateAsync(string name, string value)` to `IAdapterContext`, backed by a small
  `adapter_state` table keyed by `(subscription_id, name)`. This is Airbyte's `state` argument, it
  is engine-independent, it survives a restart on a different node, and it is ~80 lines. It is an
  SW-Serverless SDK change, so it is versioned and shared with Traxis.
* **(B) Cursor in the customer's database** — a `bitween_cursor` table the adapter writes. No SDK
  change, but it needs DDL rights in someone else's database and it puts Bitween's bookkeeping
  where the customer's DBA will eventually find and drop it.

Take (A). Offer (B) only as a capability-gated fallback for read-only-plus-one-table deployments.

### 5.4 Receiver role, push (Phase 4)

Where the engine and privileges allow, the adapter registers for change notification at
`StartAsync` and calls `IAdapterContext.PublishAsync` — the same ingress the RabbitMQ adapter uses,
with the same persist-then-acknowledge ordering. Because the Postgres payload is capped at ~8 KB
and CQN gives you rowids rather than rows, the notification **triggers a targeted re-read**; it
never carries the business payload. `dedupeKey` is `{datasource}:{table}:{pk}:{rowversion|xmin|ora_rowscn}`,
which is what makes at-least-once tolerable.

---

## 6. Design decisions worth arguing about

### 6.1 Four adapters, not one generic JDBC-style provider

A single `bitween.db` carrying four drivers is ~40 MB of unwanted dependency per node, forces the
Oracle client's licensing onto every deployment, and makes the capability matrix a runtime `switch`
instead of a compile-time fact. Four thin binaries over a shared core keeps each package small and
lets Oracle ship on its own cadence. The cost is four publishes per core change — accepted, and
mitigated by the core being a NuGet package rather than linked source.

Adapters and the host both target **net10.0**. They were on .NET 8 until r10, but a resident adapter
only runs under the r10 host, so nothing required the older target. `SW.Bitween.Adapters.Shared`
is still linked as *source*, because Bitween reads its attributes by name and no assembly identity
has to line up.

### 6.2 The pool lives in the adapter, and that is the entire justification for residency

An ephemeral adapter gets a cold ADO.NET pool per process, so every Xchange pays a TCP connect, a
TLS handshake and an authentication round trip — 20–150 ms against a remote database, more against
Oracle. A resident process with `Max Pool Size` configured pays that once. The pool is also where
`Min Pool Size` keeps connections warm through quiet periods, which is the difference between a
scheduled 3 a.m. job that runs and one that times out reconnecting.

### 6.3 Placement is per-node, not exclusive

A broker queue must be consumed by exactly one node. A database connection pool must exist on
*every* node that runs work. Reusing the exclusive lease would mean one node holds the pool and
every other node's Xchanges fail with "this node is not running the adapter". See §7.2.

The exception is **push ingress** (§5.4): a change-notification subscription *is* exclusive, and a
data source with `ChangeNotification` enabled must fall back to the leased placement. So placement
is a property of the *configuration*, not of the adapter — `per-node` unless push is on.

### 6.4 Result sets need paging and an escape hatch

The invoke envelope is one request, one response, over a UDS/named pipe. Two rules:

* **`MaxRows` is mandatory and defaulted** (1,000). A query that exceeds it fails with a message
  naming the paging commands rather than silently truncating.
* **Cursor paging**: `Query` may return `{ rows, cursorId, hasMore }`; `Fetch(cursorId, take)`
  continues; `CloseCursor(cursorId)` releases. An open cursor pins a pooled connection, so it gets a
  server-side idle TTL (default 60 s) — this is exactly the Mule stored-procedure trap.
* **Large payloads bypass the envelope**: over a threshold, the adapter writes to cloud files
  through the host and returns a reference, the way large `XchangeFile` payloads already work.

### 6.5 SQL is configuration, not message content

`AllowAdHocSql` defaults **false**. Statements are named entries defined on the data source or the
subscription's adapter properties, parameters bind by name, and mapper output supplies *parameter
values only*. A mapper is a Scriban template evaluated over message content; if that can emit SQL
text, every Xchange is an injection vector into the customer's database.

Also default to a read-only login for receivers, document the exact grants each mode needs in the
provider description, and keep `Execute`/`Call` out of the `Inspect` allow-list forever.

---

## 7. Gaps in the current code, with fixes

### 7.1 The resident pool is keyed by adapter id alone — **blocker**

`SW-Serverless/SW.Serverless/Resident/ResidentAdapterHost.cs:388`:

```csharp
var pool = pools.GetOrAdd(spec.AdapterId, _ => new AdapterPool(spec, this, options, logger));
```

The `AdapterSpec` — **including its `StartupValues`, i.e. the connection string and credentials** —
is captured from whichever caller created the pool first. Two data sources on the same adapter id
share one pool of processes started with the first one's configuration. For a bus provider this is
masked because bus adapters run as *exclusive* instances keyed by data source, not from the pool;
`ResidentAdapterRuntime.BeginAsync` (which the Xchange pipeline uses) goes through `RentAsync` and
is exposed to it today.

Fix: add `AdapterSpec.PoolKey`, defaulting to `AdapterId + stable hash of StartupValues`, and key
`pools` on it. Small change, upstream in SW-Serverless, and it is a correctness fix worth making
whether or not the DB adapters happen (per the memory note: fix it in the public repo and let CI
publish, rather than working around it here).

### 7.2 No `per-node` placement — **blocker**

`Services/DataSources/BusProviderSupervisor.cs` takes `ILeaderElection` lease
`datasource.{id}` before starting anything. Add `DataSource.Placement`
(`Exclusive` | `PerNode`, defaulted from the provider descriptor's `Kind` — `Broker` → Exclusive,
`Relational` → PerNode) and skip lease acquisition for `PerNode`. `OwnedByNode` becomes
"this node" for those rows, and `Telemetry.RunningHere` stops being a normal `false`. Rename the
supervisor to `DataSourceSupervisor` while touching it.

### 7.3 A subscription cannot point at a data source — **blocker**

Only `BusGateway` has `DataSourceId`. Add a nullable `DataSourceId` to `Subscription` (or, better,
to the adapter selection: `HandlerDataSourceId` / `ReceiverDataSourceId`, since a subscription can
legitimately read from one database and write to another). The invoker then resolves connection
settings from the data source rather than from adapter properties, which is what makes the pool
shared, the credentials single-copy, and the health page truthful.

Follow the existing injection convention: expose the resolved source to templates as
`__datasource__`, beside `__partner__` and `__globals__` (`XchangeService.cs:201-221`).

### 7.4 Smaller items

* **`Inspect` allow-list** (`Resources/DataSources/Inspect.cs`) is `["Discover", "GetStats"]`. Add
  `Describe` and `Explain`. Never add `Query`, `Execute`, `Call`, `Batch` or `BulkLoad` — the
  comment there already explains why, and for a database the stakes are higher than for a broker.
* **`Inspect` finds the instance by `InstanceKey == key.ToString()`**, which assumes the exclusive
  shape. Under per-node placement that still works (each node runs its own instance keyed the same
  way) — but only if DB sources are started as *exclusive-shaped instances without a lease*, not as
  pool rentals. That is the concrete meaning of §6.3, and it is the reason §7.1's pool fix is not
  by itself sufficient.
* **Invoke timeout**: `ResidentOptions.InvokeTimeout` is 300 s globally. Per-call `timeoutSeconds`
  already exists on `IAdapterLease.InvokeAsync`; the DB commands must pass the statement timeout
  plus a margin, so a slow report does not look like a wedged adapter and get restarted.
* **Bus-gateway guard is one-directional.** `Resources/BusGateways/{Create,Update}` rejects a
  non-`Broker` source. Nothing yet stops a `Broker` source being attached to a DB handler; add the
  mirror check when §7.3 lands.
* **Secrets** already work: `DataSource.SecretProperties` + `AESCryptoService`, masked on read,
  delivered over the stream and never on argv (`Handshake`). Mark `Password` `[AdapterSetting(Secret = true)]`
  and prefer integrated/managed-identity auth where the engine offers it —
  `UseAzureManagedIdentity` already exists in `BitweenOptions` for the host's own database.

---

## 8. Observability

`GetStatusAsync` answers every heartbeat and must stay cheap — never probe the database from it.
Report from counters the adapter already keeps:

```
Connected              pool has ≥1 usable connection (last-success timestamp, not a fresh ping)
State                  Starting | Connected | Idle | Degraded | Disconnected | Draining | Failed
Details:
  server               host:port/service, server version
  pool.size            current / min / max
  pool.busy            checked out right now
  pool.waiting         callers queued for a connection   ← the number that predicts an outage
  statements.executed  total, and per named statement
  latency.p50/p95/p99  milliseconds
  cursors.open         paging cursors currently pinning a connection
  receive.cursor       last cursor value the receiver advanced to
  errors.last          message + SQLSTATE / ORA- / error number
```

Metrics via `IAdapterContext.Metric`: `bitween.db.query.duration`, `bitween.db.rows`,
`bitween.db.pool.waiting`, `bitween.db.errors`, tagged with engine and statement name — never with
the SQL text, and never with parameter values.

Log rule: parameter values are **redacted by default** (`LogParameterValues = false`), because
`Trace`-level logging of a payment insert is a data-protection incident wearing a debug flag.

---

## 9. UI surface (`SW.Bitween.Web/ClientApp`)

Almost free, because the data-source form generates itself from the adapter's attributes.

1. **Data sources page** — already lists providers by `Kind`; a Relational source just appears once
   the adapters are published. Health card shows the pool counters from §8.
2. **Schema browser** — DONE. `SchemaBrowser.tsx` on the data source detail, backed by `Inspect` →
   `Describe` + `Discover`. Grouped by schema, filtered by object type and searched by name — the
   search goes to the database as `nameLike`, not to the page in hand, so it answers for the whole
   catalog. Columns and routine parameters on demand, one object at a time. Read-only by
   construction: `Inspect`'s allow-list holds only Describe, Discover and GetStats.

   Two things it needed that did not exist. `Inspect` invoked the command with no argument, so
   Discover's filters were unreachable — `DataSourceInspectRequest.Arguments` now carries them.
   And "use in a statement" writes a draft into the statements panel above it, which is item 4's
   picker arriving early, because a browser that cannot start a statement is a catalog viewer.
3. **Capability chips** — `Describe` output rendered as enabled/disabled features with the reason
   ("stored procedure result sets: needs a REF CURSOR on Oracle", "log-based CDC: not supported").
4. **Statement editor** — PARTLY DONE. The picker fed by `Discover` is built (see 2): a table,
   view, procedure, function or sequence hands a drafted statement to the form, so the common case
   is reviewed rather than typed. Still missing: the parameter list with types, and a Preview that
   runs `Explain` (never `Query`).
5. **Receive mode form** — DONE, and it moved. Mode, batch size and the mark-processed statement
   are on the SUBSCRIPTION (`DataSourceBinding`), because they are the reader's policy; the cursor
   and key columns are on the STATEMENT, because they describe what the query returns. The
   deletes-are-invisible caveat is in the mode's own hint. See §6.1 of the API doc for the split
   and why.

---

## 10. Testing

* **Unit** (`SW.Bitween.UnitTests`): capability matrix, parameter binding per engine, the
  statement allow-list, cursor advance/rollback logic, `MaxRows` enforcement.
* **Integration** (`SW.Bitween.IntegrationTests`): Testcontainers already provides Postgres. Add
  MySQL, `mcr.microsoft.com/mssql/server`, and `gvenzl/oracle-free` (the slim image — Oracle is the
  one that will hurt CI time; gate it behind a trait and run it nightly if needed).
  `AdapterInstaller` publishes and uploads the four adapters exactly as it does
  `SW.Bitween.SampleHandler`, so these are real subprocess tests, not mocks.
* **The tests that matter most**, because they are the ones the design can get wrong:
  1. Two data sources on the same adapter id, different credentials, concurrent traffic — proves §7.1.
  2. Two nodes, one relational data source — both must serve; proves §7.2.
  3. Kill the adapter mid-receive — the cursor must not advance past uncommitted rows.
  4. A query exceeding `MaxRows`, and a cursor abandoned past its TTL — connection returns to pool.
  5. `Inspect` with `Command = "Execute"` — must be refused.

---

## 11. Sequencing

| Phase | Contents | Why here |
|---|---|---|
| **0 — unblock** | `AdapterSpec.PoolKey` (§7.1); `Placement` on data source + supervisor (§7.2); `DataSourceId` on subscription (§7.3) | none of these are database-specific, all three are prerequisites, and the first is a live correctness bug |
| **1 — core + PostgreSQL** | `Db.Core`, `bitween.db.postgresql`: Test/Describe/Discover, Query/Execute/Call, paging, statement allow-list | Postgres is the reference provider everywhere else in this repo; prove the shape once |
| **2 — receiving** | Receiver session, four+one `ReceiveMode`s, host-held cursor state (`IAdapterContext.GetState/SetState`) | the SDK change lands once, with one engine proven against it |
| **3 — the other three engines** | MySQL, SQL Server, Oracle; capability matrix filled in; bulk load per engine | mostly mechanical once §1–2 are settled; Oracle last, it is the awkward one (REF CURSOR, privileges, image size) |
| **4 — UI** | schema browser, capability chips, statement editor, receive-mode form | needs `Describe`/`Discover` stable first |
| **5 — push ingress** | `LISTEN/NOTIFY`, Service Broker, CQN; exclusive placement when enabled | optional, capability-gated, and the only part that needs the leased placement back |
| **later** | `bitween.cdc.*` log-based providers (Debezium-shaped), if demand appears | genuinely a different product: replication slots, offsets, snapshots |

---

## 12. Decisions taken

1. **Cursor state store: option (A).** `IAdapterContext.GetStateAsync` / `SetStateAsync`, backed by
   Bitween's `adapter_state` table. Resident adapters are not in production on Traxis, so the SDK
   change is free to make now — [SW-Serverless#131](https://github.com/simplify9/SW-Serverless/pull/131).
2. **One `DataSourceId` per subscription**, not a pair per adapter slot. Reading from one database
   and writing to another is served by two subscriptions chained through a response.
3. **Placement is `Auto`**, resolved from `Kind`: Broker → Exclusive, everything else → PerNode.
   Overridable per data source for the case that crosses over (push ingress on a relational source).

## 12a. Decision: SQL statements are their own entity

**Context.** Statements have to live on the *connection*, not on a subscription. A subscription's
adapter properties have `{{partner.X}}` and `{{globals.…}}` substituted into their values before the
adapter ever sees them (`StartupValuesFiller.Fill`), and partner records are ordinary data — so SQL
in a handler property is a live injection surface fed by whoever maintains partners. Keeping SQL on
the data source also lets `TestConnection` **prepare every statement** against the live schema, so a
typo or a dropped column fails on the Test button rather than on the first message at 3am.

But the first cut put them in a `Statements` JSON field on `DataSource`, and that had a cost nobody
should accept: `DataSources.Edit` is the right that changes **credentials**. Writing a query and
rotating a database password became the same permission.

**Decision.** `DataSourceStatement` is an entity — data source, name, SQL, description, owning work
group, active flag, full audit trail — with its own permission area, `data-source-statements.*`.

**Consequences, good:**

* Writing a query no longer requires credential-level rights. This was the whole point.
* A unique index on `(DataSourceId, Name)` makes a collision an error at save time rather than a
  silent overwrite. Case is handled in the handler, because collation is provider-specific.
* Usage is countable: `GET /datasourcestatements/{id}/usage` lists which subscriptions name it, in
  which slot, with which operation. Zero usage is the number that matters — it is the only way to
  tell dead SQL from quiet SQL, and it is why nobody ever cleaned up the blob.
* Deleting a statement in use is refused and names what is using it. Renaming one in use is refused
  too: a rename breaks every subscription naming the old name, and nothing on this side can fix
  that.
* An owning work group means a shared database stops being a shared blob — a statement has someone
  to ask.

**Consequences, accepted:**

* One more entity, one more permission area, one more screen to build.
* Usage is resolved by scanning that data source's subscriptions rather than by a join, because the
  statement name lives in a JSON column and there is no portable query for it across PostgreSQL,
  MySQL and SQL Server. Bounded by subscriptions-per-data-source, and it runs on a screen rather
  than in the message path.
* A subscription and its statement still cannot change atomically — they are separate entities.

**What did not change:** the adapter contract. `StatementComposer` builds the same name-to-SQL JSON
the adapter has always received, so an adapter still resolves a name and knows nothing about where
the SQL was kept. The `Statements` setting is now `Hidden` on the generated form, because Bitween
composes it.

---

## 13. Open questions

1. **Oracle in CI** — accept the image cost on every PR, or nightly only? `gvenzl/oracle-free:23-slim-faststart`
   is ~1.5 GB and a minute or two to become healthy.
2. **Do we ever want `AllowAdHocSql = true`?** It is a genuine escape hatch for one-off migrations
   and a permanent injection surface. Suggest: allowed, but gated on a distinct permission and
   audited per execution.
3. **Multi-tenant partner routing** — should `__partner__` properties be able to select a schema or
   a connection, so one subscription serves many partners' databases? Powerful, and a credential-
   confusion risk worth designing deliberately rather than discovering.

---

## Sources

- [MuleSoft Database Connector Reference](https://docs.mulesoft.com/db-connector/latest/database-documentation) and [Pooling Profiles](https://docs.mulesoft.com/mule-runtime/latest/tuning-pooling-profiles)
- [Confluent JDBC Source Connector overview](https://docs.confluent.io/kafka-connectors/jdbc/current/source-connector/overview.html), [Aiven: JDBC source connector modes](https://aiven.io/docs/products/kafka/kafka-connect/concepts/jdbc-source-modes)
- [Debezium vs Kafka Connect JDBC Source](https://risingwave.com/blog/debezium-vs-kafka-connect-jdbc-source-cdc/)
- [Airbyte Protocol](https://docs.airbyte.com/platform/understanding-airbyte/airbyte-protocol)
- [Apache Camel SQL Stored Procedure component](https://camel.apache.org/components/4.18.x/sql-stored-component.html)
- [ADO.NET GetSchema and Schema Collections](https://learn.microsoft.com/en-us/dotnet/framework/data/adonet/getschema-and-schema-collections), [Oracle ODP.NET GetSchema](https://docs.oracle.com/en/database/oracle/oracle-database/26/odpnt/ConnectionGetSchema1.html)
- [PostgreSQL NOTIFY](https://www.postgresql.org/docs/current/sql-notify.html), [Oracle Continuous Query Notification](https://docs.oracle.com/en/database/oracle/oracle-database/19/lnoci/notification-streams-advanced-queuing.html), [SqlDependency / Service Broker](https://learn.microsoft.com/en-us/dotnet/api/system.data.sqlclient.sqldependency.start)
