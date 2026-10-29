# Database Adapters: API Reference

What `bitween.db.oracle` and `bitween.db.postgresql` expose, and how to configure them for the
things people actually want to do with a database.

Design rationale is in [design/provider-plan-databases.md](design/provider-plan-databases.md); this document is
the contract.

---

## 1. The shape of it, in one picture

```
DataSource (Kind = Relational)          ← the connection: host, credentials, pool
   │  one resident adapter process per node, holding an ADO.NET pool
   │
   ├── DataSourceStatement records ..... the named SQL it may run - its own permission,
   │                                     its own audit trail, its own usage count
   │
   ├── operations endpoints ............ Test / Inspect / Telemetry on the data source screen
   │      TestConnection, Describe, Discover, GetStats
   │
   └── Subscription.DataSourceId ....... the pipeline runs through THAT connection
          ├── as Handler or Mapper ..... Handle(XchangeFile)  →  Query / Execute / Call
          └── as Receiver .............. Initialize → ListFiles → GetFile → DeleteFile → Finalize
```

Two rules run through everything below:

1. **SQL is configuration; messages carry values.** Statements are records on the data source. A
   subscription names one and supplies parameters. `AllowAdHocSql` is off by default and should stay off
   - a mapper is a template evaluated over message content, so SQL it can emit is SQL an inbound
   message can steer.
2. **The adapter is shared.** One process per data source per node serves every subscription bound
   to it, concurrently. It keeps no per-message state.

---

## 2. Connection settings

The data source form is generated from the adapter, so this table is what you will see on screen.
Settings marked **secret** are never returned by the API, which masks them. They are stored unencrypted in the database.

> **Meeting one of these engines for the first time?**
> [database-adapters-per-engine.md](database-adapters-per-engine.md) is the guide behind these
> tables: what you must set and what you can leave alone, how each engine writes a placeholder and
> a row limit, what its types arrive as, whether a procedure can return rows, and what its error
> messages mean. This section is the reference; that one is the explanation.

### 2.1 Common to both engines

| Setting | Default | What it is for |
|---|---|---|
| `MinPoolSize` | 2 | Connections kept open while idle. Above zero is the difference between a 3am job that runs and one that times out reconnecting. Costs one server session each, **per node**. |
| `MaxPoolSize` | 20 | Ceiling on concurrent connections from this node. Multiply by replica count before comparing to the server's session limit. |
| `ConnectTimeoutSeconds` | 15 | |
| `CommandTimeoutSeconds` | 30 | Default per statement; a statement may raise its own. |
| `MaxRows` | 1000 | Rows one query may return. Exceeding it opens a paging cursor rather than truncating. |
| `CursorIdleTimeoutSeconds` | 60 | An unread paging cursor holds a pooled connection, so idle ones are reclaimed. |
| *(statements)* | - | Not a setting: they are records on the data source. See §3. |
| `AllowAdHocSql` | false | Lets a caller send SQL text instead of naming a statement. |
| `LogParameterValues` | false | Logs parameter values at Debug. For a controlled test only. |
| `ReceiveMode` | - | Default for subscriptions that do not choose one. §6. |
| `ReceiveBatchSize` | 500 | Default rows one poll takes; a subscription may override. |
| `ReceiveStatement`, `CursorColumn`, `KeyColumn`, `MarkProcessedStatement` | - | **Legacy.** Still honoured if set, hidden from the form. See §6.1 for where each now lives. |

### 2.1a MySQL - `bitween.db.mysql`

Serves MariaDB too. `Database` is also the schema - MySQL has no separate one - so it is what an
unqualified name resolves against, and a discovery call with no schema means the database this
connection opened rather than every database on the server.

| Setting | Default | Notes |
|---|---|---|
| `Host`, `Port` | localhost, 3306 | |
| `Database` | - | Required. Also the schema. |
| `UserName`, `Password` | - | |
| `SslMode` | Preferred | Required or above off localhost. |
| `ConnectionIdleLifetimeSeconds` | 180 | Keep below the server's `wait_timeout`; a connection the server closed first surfaces as a broken pipe on the next message. |
| `ServerPrepare` | true | Off for a connection through ProxySQL, where prepared statements pin a backend. Also what makes the save-time check real - with it off, `Prepare` does nothing and every statement reports valid. |
| `AllowZeroDateTime` | false | MySQL permits `0000-00-00`, which no .NET date type holds. |

No sequences (AUTO_INCREMENT belongs to a column), no MERGE, no RETURNING, no array type. A
procedure returns result sets by SELECTing - the capability PostgreSQL declares false.

### 2.1b SQL Server - `bitween.db.sqlserver`

Serves Azure SQL too. The default schema belongs to the LOGIN rather than the connection, so
`Schema` is applied per connection with `EXECUTE AS` - and failure is not fatal, because
impersonating a schema's owner is a privilege many service accounts do not have.

| Setting | Default | Notes |
|---|---|---|
| `Host`, `Port` | localhost, 1433 | `Port` is ignored for `host\instance`, which resolves through the Browser service. |
| `Database` | - | Required. |
| `UserName`, `Password` | - | |
| `Schema` | - | Run as this schema, for unqualified names. |
| `Encrypt` | true | On by default in the modern driver - a change from the old one, and why an instance that worked for years fails on upgrade. |
| `TrustServerCertificate` | false | Needed for the usual self-signed on-premises certificate. Use knowingly. |
| `MultipleActiveResultSets` | false | Stops the connection resetting cleanly between uses, which is the opposite of what a shared pool wants. |
| `SnapshotIsolation` | false | Readers do not block writers. Needs the database to have it enabled. |

MERGE, OUTPUT and snapshot isolation are all real here. Query Notifications over Service Broker
exists and is not wired, so `changeNotification` is false.

**Its statement check is not the shared one.** `SqlCommand.Prepare` refuses unless every parameter
has an explicit type - which a caller checking somebody else's SQL does not know - so this adapter
asks `sys.sp_describe_undeclared_parameters`, which parses the batch and binds every name in it.

### 2.2 Oracle - `bitween.db.oracle`

| Setting | Default | Notes |
|---|---|---|
| `Host` | localhost | **required** |
| `Port` | 1521 | |
| `ServiceName` | - | What `lsnrctl services` lists, e.g. `FREEPDB1`. What a modern Oracle wants. |
| `Sid` | - | For an older instance with no service name. **Set exactly one of ServiceName and Sid** - both, or neither, is refused with a message saying so rather than guessed at. |
| `ConnectDescriptor` | - | A full TNS descriptor or Easy Connect string, *instead of* host/port/service. The escape hatch for RAC, Data Guard and wallet-based cloud connections. |
| `UserName` | - | **required** |
| `Password` | - | **required, secret** |
| `Schema` | login's own | Default schema for the schema browser only. It does not change how unqualified names resolve. |
| `AsSysDba` | false | Almost never right for an integration login. |
| `WalletDirectory` | - | Holds `cwallet.sso`, for mTLS or Autonomous Database. Pair with `ConnectDescriptor`. |
| `FetchSize` | 100 | Rows per round trip. |
| `BindByName` | true | **Leave it on.** Off, ODP.NET binds by position, so a statement using `:id` twice - or listing parameters in a different order than the caller supplies them - silently binds the wrong values. |

Parameters are written `:name`.

### 2.3 PostgreSQL - `bitween.db.postgresql`

| Setting | Default | Notes |
|---|---|---|
| `Host` | localhost | **required** |
| `Port` | 5432 | |
| `Database` | - | **required.** PostgreSQL connects to a database, not a server. |
| `UserName` | - | **required** |
| `Password` | - | **required, secret** |
| `SslMode` | Prefer | `Require` or above for anything non-local. `Prefer` silently falls back to plaintext if the server does not offer TLS - which is the case you wanted to be told about. |
| `Schema` | server default | Sets `search_path`. Point a data source at a schema without rewriting the SQL. |
| `ApplicationName` | Bitween | How this connection appears in `pg_stat_activity` - how a DBA works out which connections are yours. |
| `AutoPrepare` | false | Caches a plan per connection. Faster for repeated statements; a plan cached against skewed data can be worse than replanning. |
| `ConnectionIdleLifetimeSeconds` | 300 | Prunes pooled connections above `MinPoolSize`. |

Parameters are written `@name`. **Not `:name`** - that collides with PostgreSQL's `::` cast
operator, and `value::text` would be read as a parameter called `text`.

---

## 3. Statements

The SQL a data source is allowed to run. **Each statement is its own record**, not a field on the
data source - `POST /datasourcestatements`, with `dataSourceId` in the body:

```json
{ "name": "insertOrder",
  "sql": "insert into orders (id, customer, amount) values (:id, :customer, :amount)",
  "description": "Posts an order from the ERP feed",
  "workGroupId": 3,
  "inactive": false }
```

A subscription names one; the SQL itself never travels with a message and never sits in a
subscription's adapter properties. Bitween composes the active statements of a data source into the
name-to-SQL bag the adapter receives, so the adapter resolves a name and knows nothing about where
the SQL is kept.

### 3.1 Why a record and not a field

Statements have to live on the **connection**: a subscription's adapter property values have
`{{partner.X}}` substituted into them before the adapter sees them, so SQL there would be an
injection surface fed by ordinary partner data. But a field on the data source meant that writing a
query required `data-sources.edit` - the same right that changes the **credentials**.

Hence a separate record with a separate permission, `data-source-statements.*`. Rationale and the
costs accepted are in [design/provider-plan-databases.md §12a](design/provider-plan-databases.md).

### 3.2 What that buys

| | |
|---|---|
| **Permissions** | `data-source-statements.{view,create,edit,delete}`, distinct from the connection's |
| **Namespacing** | Unique per data source, case-insensitively - a collision is an error naming the clash, not a silent overwrite. The same name on another data source is a different statement |
| **Ownership** | `workGroupId` says who to ask before changing it |
| **Audit** | Created/modified by whom and when, per statement |
| **Usage** | `POST /datasourcestatements/{id}/usage` lists which subscriptions name it, in which slot, with which operation. `UsageCount` rides on the list rows |
| **Retiring** | `inactive: true` drops it from the adapter's set without deleting the row, so a subscription still naming it fails loudly while it is migrated |

### 3.3 Rules that will stop you

* **Only a Relational data source takes statements.** A broker is refused rather than storing
  configuration nothing will read.
* **Deleting one in use is refused**, and the refusal names the subscriptions.
* **Renaming one in use is refused.** A rename breaks every subscription naming the old name, and
  nothing on this side can repair that. Changing the *SQL* is allowed - that is the common case.

### 3.4 Procedures

A statement may hold a procedure name rather than SQL; `Call` resolves its target through the same
registry, so a procedure is named on the data source exactly as a query is:

```json
{ "name": "ordersByCustomer", "sql": "ORDERS_BY_CUSTOMER" }
```

### 3.5 Validated on the Test button

`TestConnection` **prepares** every active statement against the live schema, so a typo, a dropped
table or a renamed column fails while someone is looking at the screen rather than on the first
message through a subscription.

---

## 4. Operations commands

Reachable from the data source screen. `Discover`, `Describe` and `GetStats` are relayed by
`POST /datasources/{id}/inspect`, which allow-lists them; `TestConnection` backs the Test button.

**`Query`, `Execute`, `Call` and `Batch` are deliberately NOT relayable through Inspect** - a
View-level read must not become a way to run SQL against a customer's database by naming it in a
request body.

### `TestConnection()` → staged result

```json
{
  "ok": true,
  "steps": [
    { "step": "connect",              "ok": true, "detail": "db.example:1521/PROD in 84 ms" },
    { "step": "authenticate",         "ok": true, "detail": "23.26.3.0.0" },
    { "step": "query",                "ok": true, "detail": "select 1 from dual" },
    { "step": "privileges",           "ok": true, "detail": "CREATE SESSION, SELECT ANY TABLE, ..." },
    { "step": "statement:orderById",  "ok": true },
    { "step": "statement:insertOrder","ok": true }
  ],
  "details": { "engine": "Oracle", "server": "23.26.3.0.0" }
}
```

Staged because "it did not work" is not something anyone can act on. The failing stage names what
to go and fix: a host, a password, a grant, a missing column.

### `Describe()` → capabilities

```json
{
  "engine": "PostgreSQL",
  "serverVersion": "16.4",
  "supportedObjects": ["table","view","materialized_view","procedure","function","sequence"],
  "storedProcedures": true,
  "procedureResultSets": false,
  "namedParameters": true,
  "transactions": true,
  "isolationLevels": ["ReadCommitted","RepeatableRead","Serializable"],
  "bulkCopy": false,
  "merge": true,
  "returning": true,
  "json": true,
  "arrayTypes": true,
  "changeNotification": false,
  "logBasedCdc": false,
  "receiveModes": ["bulk","incrementing","timestamp","timestamp+incrementing","marker"],
  "privileges": ["CONNECT","CREATE","TEMPORARY"],
  "details": { "statements": "insertOrder, orderById", "allowAdHocSql": "False" }
}
```

**`privileges` is probed with the real credentials, and it is the half that matters.** "Oracle
supports change notification" is worthless if the login lacks `CHANGE NOTIFICATION`. Oracle reads
`session_privs`; PostgreSQL reads role attributes plus `has_database_privilege`, so
`REPLICATION` shows up - the gate on log-based CDC, if that ever becomes a provider.

Where the engines differ honestly:

| | Oracle | PostgreSQL |
|---|---|---|
| `procedureResultSets` | **true** - via a declared REF CURSOR out parameter | **false** - a `CALL` cannot return a set; a set-returning *function* is queried with `Query` instead |
| `multipleResultSets` | false | true |
| `arrayTypes` | false | true |
| `isolationLevels` | ReadCommitted, Serializable | + RepeatableRead |

Both report `bulkCopy: false`, `changeNotification: false`, `logBasedCdc: false` today. Those are
declared rather than omitted so the UI can say *not available* instead of leaving a gap.

### `Discover(request)` → catalog

```json
{ "objectType": "table", "schema": "SALES", "nameLike": "ORDER",
  "includeColumns": true, "includeRowCounts": true, "skip": 0, "take": 200 }
```

`objectType` is one of `table`, `view`, `materialized_view`, `procedure`, `function`, `sequence`
(Oracle adds `package`). Paged, filtered, and never `SELECT COUNT(*)` - row counts are the
optimiser's estimate (Oracle `ALL_TABLES.num_rows`, PostgreSQL `reltuples`), as fresh as the last
stats gather.

```json
{
  "objects": [{
    "schema": "SALES", "name": "ORDERS", "type": "table", "rowCount": 148213,
    "comment": "Customer orders",
    "columns": [
      { "name": "ID", "dbType": "NUMBER", "clrType": "decimal", "nullable": false,
        "primaryKey": true, "generated": true, "ordinal": 1 },
      { "name": "AMOUNT", "dbType": "NUMBER", "clrType": "decimal", "precision": 10, "scale": 2,
        "nullable": true, "ordinal": 3 }
    ],
    "parameters": []
  }],
  "hasMore": false,
  "applied": { "objectType": "table", "schema": "SALES", "take": "200" }
}
```

`includeColumns` is off by default on purpose: columns for four thousand tables is a download, not
a menu. For a routine, `parameters` is always populated - a procedure without its argument list is
just a name nobody can call.

Oracle reads `ALL_*`, never `DBA_*`: `ALL_*` is what this login can see, which is the honest answer
and needs no privilege an integration account should not have. System schemas are excluded, or the
menu is thirty thousand rows of Oracle's own catalogue.

### `GetStats()` → counters

`executed`, `failed`, `rowsRead`, `rowsWritten`, `meanElapsedMs`, `openCursors`, `statements`,
`lastStatementOn`, `lastError`.

The heartbeat carries more, and it is what the health card shows: `pool.min`/`pool.max`,
`statements.executed`/`failed`, `rows.read`/`written`, `latency.meanMs`, `cursors.open`, plus
per-engine detail (`oracle.schema`, `postgres.searchPath`, `postgres.sslMode`).

---

## 5. Data commands

Called by the pipeline, not by the data source screen.

### `Query(StatementRequest)`

```json
{ "name": "openOrders", "parameters": { "customer": "acme" }, "maxRows": 500 }
```

```json
{ "rows": [ { "ID": 1, "CUSTOMER": "acme", "AMOUNT": 10.5 } ],
  "columns": ["ID","CUSTOMER","AMOUNT"],
  "affectedRows": 0, "output": {},
  "cursorId": null, "hasMore": false, "elapsedMs": 12 }
```

**Paging.** When the result exceeds `MaxRows`, `cursorId` comes back with `hasMore: true`. Continue
with `Fetch({ cursorId, take })` until `cursorId` is null; `CloseCursor(cursorId)` abandons it early.
An open cursor pins a pooled connection, which is why it has an idle timeout - an abandoned cursor
is a connection nobody else can have.

### `Execute(StatementRequest)`

Non-query. Returns `affectedRows`, and the rows themselves when the statement uses `RETURNING`
(PostgreSQL) or `RETURNING INTO` / `OUTPUT` - both engines report `returning: true`, so a write that
hands back what it wrote is not lost.

### `Call(ProcedureRequest)`

```json
{ "name": "ordersByCustomer",
  "parameters": { "p_customer": "acme" },
  "outParameters": [ { "name": "p_result", "direction": "RefCursor" } ] }
```

Out parameters are declared by the caller because ADO.NET cannot infer them without a round trip
the engines do not all support. `direction` is `Out`, `InOut` or - Oracle only - `RefCursor`.

**This is the one place the engines genuinely diverge.** An Oracle procedure returns rows through a
declared REF CURSOR; declare it wrong and you get an `ORA-06550` about argument types that points
nowhere near the cause. PostgreSQL has no equivalent for `CALL`: a set-returning function is
`select * from my_function(...)`, so configure it as a statement and use `Query`.

Results come back as `rows` (the cursor's contents) plus `output` (every non-input parameter, read
after the reader closes - before that, most drivers hand back null with no error to explain it).

### `Batch(BatchRequest)`

Several statements, one transaction, all or nothing. Optional `isolationLevel`. A failure rolls
everything back and reports the original error, not the rollback.

---

## 6. Roles in the pipeline

Set `Subscription.DataSourceId` and the subscription's adapters run through that connection.

### 6.1 As Handler or Mapper - Bitween writes to the database

Two adapter properties on the subscription decide what a message means:

| Property | Effect |
|---|---|
| `Statement` | The configured statement to run. **Set this.** The message body is then read as a flat JSON object of parameter values. |
| `Operation` | `query` (default), `execute` or `call`. |

So a mapper produces `{"id": 4412, "customer": "acme", "amount": 99.5}` and the handler runs
`insertOrder` with those values. Nothing about the SQL depends on message content, and nothing in
the template decides which statement runs.

These travel with each **call**, not with the adapter process - which matters because the process
is shared. One data source is one connection pool serving every subscription bound to it, so a
per-subscription setting could not live in the process's startup values; the data source can still
set a default for both, and a subscription overrides it. (This rides on per-invocation properties,
[SW-Serverless#132](https://github.com/simplify9/SW-Serverless/pull/132), in SDK 8.1.23; before it,
per-subscription properties were silently dropped and every caller read the data source's own.)

Without `Statement`, the body must be a full request envelope -
`{"name": "...", "parameters": {...}}` - which is useful when one subscription drives several
statements, and is still an allow-list lookup, not raw SQL.

The response `XchangeFile` is the `QueryResult` as JSON, so a response subscription or a bus publish
can carry the rows the database returned.

### 6.2 As Receiver - the database feeds Bitween

Mapped onto the existing receiver session, so nothing about the pipeline changes:

| Session call | What the adapter does |
|---|---|
| `Initialize` | Prunes stale batches. No transaction is held open - see below. |
| `ListFiles` | Runs the named receive statement, returns one opaque id per row |
| `GetFile(id)` | That row, as JSON |
| `DeleteFile(id)` | Runs the named mark-processed statement, then advances that subscription's cursor past the row |
| `Finalize` | Drops what the run did not get through |

### 6.1 Where each receive setting lives

A connection is shared by every subscription pointed at it, so nothing that varies per reader can
sit on the data source - one connection could otherwise only ever feed one receiver. What is left
divides on a single principle: **the SQL and the shape of its rows belong to the statement; the
reading policy belongs to the subscription doing the reading.**

| Setting | Configured on | Why there |
|---|---|---|
| the polling SQL | **statement** (named by the subscription's `ReceiveStatement`) | It is SQL, so it gets a statement's permission, audit trail and usage count - and a per-invocation property is never SQL text, because partner values are templated into those before the adapter sees them. |
| `CursorColumn` | **statement** | Describes what the query returns. The same statement returns the same cursor column whoever reads it; two readers each nominating their own is two chances to be wrong with nothing to check them against. |
| `KeyColumn` | **statement** | Same reason. |
| `MarkProcessedStatement` | **subscription**, naming a statement | It is SQL, and it writes - so it is a statement like any other. Which one runs is the reader's business. |
| `ReceiveMode` | **subscription** (data source default) | Reader policy: the same statement is legitimately read `bulk` once for a backfill and `incrementing` after. |
| `ReceiveBatchSize` | **subscription** (data source default) | One subscription's appetite. |

The **cursor is scoped per subscription**, for the same reason. Host state is keyed by (adapter,
instance, name) and the instance is the data source, so a fixed name meant one cursor per
*connection*: whichever subscription polled first advanced it and the rows it took were invisible
to the other. The state name now carries the subscription id, and the old unscoped name is read as
a fallback so an upgrade resumes rather than replaying.

Everything falls back to the data source setting of the same name, so a receiver configured before
the split keeps working untouched.

**Modes** are Kafka Connect's vocabulary, because it is the one operators already have:

| Mode | Finds new rows by | Needs |
|---|---|---|
| `bulk` | re-reading everything each poll | `MarkProcessedStatement` |
| `incrementing` | an always-growing column | `CursorColumn` |
| `timestamp` | a modified-at column | `CursorColumn` |
| `timestamp+incrementing` | both | `CursorColumn` |
| `marker` | a processed-flag column | `MarkProcessedStatement` |

### 6.2 Statements are checked when they are saved

`ValidateStatement { sql }` → `{ ok, error, note }`. The engine PREPARES the SQL - parsed and
planned, never run, nothing stored - and the create/update handlers call it before a statement is
written. A typo is refused where it was made, rather than surfacing later as a failed connection
test or, if nobody ran one, as a failed message days afterwards.

It is best-effort by construction: the adapter has to be running on this node to answer, and a
source that is stopped or still starting cannot be asked, so the save proceeds unchecked. Refusing
to let someone save a fix because the connection they are fixing it for is down would be backwards.

**Oracle's check is not the shared one either**, and for a worse reason: ODP.NET's `Prepare` is a
client-side no-op - Oracle compiles a statement when it is executed, not when it is prepared - so
the shared check passed everything, including a select from a table that does not exist. It uses
`DBMS_SQL.PARSE` instead, which compiles and resolves names while running nothing. The PL/SQL
frames that raises are stripped, so the answer is the one ORA- line that is about the operator's
SQL rather than the mechanism used to find it.

Two answers are not plain pass/fail. A bare **procedure name** passes with a `note` saying its
existence was not checked - it is resolved when called, and preparing it as text is a syntax error
every time. And a **wrong placeholder prefix** - `:name` on PostgreSQL, `@name` on Oracle - is
named in the error rather than left as the driver's "syntax error at position 76", because that is
what a statement copied between two data sources looks like.

`TestConnection` runs the same check over every configured statement, and reports **all** of them
rather than stopping at the first failure: the second failure is usually the first mistake
repeated, which is obvious when both are on screen and invisible when they arrive a fix apart.

**None of them can see a DELETE.** That is a property of polling, not of this adapter. It is stated
here, and in the form, rather than left to be discovered.

Two things make this safe:

* **The cursor is host-held**, through Bitween's `adapter_state` table. An adapter cannot keep its
  own progress - the supervisor restarts it and the next instance may be on another node - and a
  cursor that resets replays every row already processed.
* **It advances in `DeleteFile`**, which the pipeline calls only after Bitween has durably accepted
  the row. Advancing on read would lose rows on a crash; advancing only at the end of a batch would
  replay the batch. Per row is merely at-least-once, which is the honest guarantee.

No transaction spans the session, deliberately: it would pin a pooled connection for as long as
Bitween takes to persist every row, on an instance serving other subscriptions at the same time.
Each mark-processed commits on its own, after acceptance - the same boundary either way.

---

## 7. Use cases

### 7.1 Look inside a database nobody documented

Create the data source, press Test, then Inspect → `Describe` and `Discover`. You get the server
version, what the login may actually do, and the tables, views, procedures (with argument lists and
directions) and sequences with their current values. Read-only by construction: `Discover` cannot
run a statement, and Inspect will not relay one.

### 7.2 Post an order into an ERP table

```
Statements:  { "insertOrder": "insert into orders (id, customer, amount)
                               values (:id, :customer, :amount)" }
Subscription: DataSourceId = the ERP source
              HandlerId    = bitween.db.oracle
              HandlerProperties: Statement=insertOrder, Operation=execute
```

The mapper turns the inbound document into `{"id":…, "customer":…, "amount":…}`. The handler binds
and runs. The response is the full query result, with `"affectedRows": 1`.

### 7.3 Enrich a message from a lookup table

Same shape with `Operation=query` as the **mapper**: the mapper stage produces the lookup
parameters, and the adapter's output - the rows - becomes the Xchange's output file for the handler
that follows.

### 7.4 Call a stored procedure that returns a result set

*Oracle:*
```
Statements: { "ordersByCustomer": "ORDERS_BY_CUSTOMER" }
HandlerProperties: Statement=ordersByCustomer, Operation=call
Body: {"p_customer":"acme"}      ← plus outParameters when using Call directly
```

*PostgreSQL:* there is no REF CURSOR. Configure the set-returning function as a query instead:
```
Statements: { "ordersByCustomer": "select * from orders_by_customer(@customer)" }
HandlerProperties: Statement=ordersByCustomer, Operation=query
```

### 7.5 Poll a staging table for new rows

```
ReceiveMode:            incrementing
ReceiveStatement:       select * from inbox where id > :cursor order by id fetch first 500 rows only
CursorColumn:           ID
KeyColumn:              ID
MarkProcessedStatement: update inbox set processed = 'Y' where id = :key
Subscription:           ReceiverId = bitween.db.oracle, DataSourceId = the source, plus a Schedule
```

Each row becomes an Xchange. The cursor survives a restart, a redeploy, and the data source moving
to another node.

The PostgreSQL equivalent differs only in dialect:
```
ReceiveStatement: select * from inbox where id > @cursor order by id limit 500
MarkProcessedStatement: update inbox set processed = true where id = @key
```

### 7.6 Drain a queue table that has no id to follow

```
ReceiveMode:            marker
ReceiveStatement:       select * from outbox where sent = 'N' order by created_at
KeyColumn:              ID
MarkProcessedStatement: delete from outbox where id = :key
```

No cursor: the mark is what stops a row being read twice. `bulk` and `marker` both refuse to start
without `MarkProcessedStatement`, because without it they would read the same rows on every poll
forever.

### 7.7 Read a report too large for one response

Ask for it with `Query`, then follow `cursorId` through `Fetch` until it comes back null. Pages are
exact: the row read to discover there *is* another page is carried into the next one rather than
dropped, which is otherwise a silent one-row-per-page loss.

---

## 8. Things to know before you rely on it

* **`AllowAdHocSql` should stay off.** It is a real escape hatch for a one-off migration, and a
  permanent injection surface otherwise.
* **Use a read-only login for receivers**, and grant writes only where a handler needs them.
  `Describe` will tell you what the login actually has.
* **`MaxPoolSize` is per node.** Three replicas at 20 is 60 sessions.
* **Deletes are invisible to polling.** If you need them, you need CDC, which is a different
  provider and not built.
* **Parameter values are never logged** unless `LogParameterValues` is switched on, and metrics
  carry only the engine, never the statement, the SQL or the values.
* **Errors report the driver's message** - `ORA-…`, PostgreSQL's SQLSTATE - because that is the
  string a DBA can search for.
