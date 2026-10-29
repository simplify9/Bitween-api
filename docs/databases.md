# Databases

A relational [data source](data-sources.md) lets Bitween read rows into exchanges and run SQL for deliveries. Four engines are supported.

| Engine | Adapter id | Driver |
|---|---|---|
| PostgreSQL | `bitween.db.postgresql` | Npgsql 8 |
| MySQL | `bitween.db.mysql` | MySqlConnector 2 |
| SQL Server | `bitween.db.sqlserver` | Microsoft.Data.SqlClient 5 |
| Oracle | `bitween.db.oracle` | Oracle.ManagedDataAccess.Core 23, which needs no Instant Client |

Each enabled node runs one adapter process per database, holding a connection pool. Every subscription bound to that database shares the process and its pool. Database sources are placed on every enabled node by default, so pool sizes apply per node.

For engine-by-engine advice on writing statements, see the [per-engine guide](database-adapters-per-engine.md).

## How it fits together

```
Data source            the connection and its pool
 ├── Statements        the named SQL the connection may run
 └── Subscriptions bound to the data source
      ├── as a receiver   poll a statement and turn rows into exchanges
      └── as a handler    run a statement with the exchange as parameters
```

- SQL lives in **statements**, never in subscription properties. A subscription names the statement it runs. Partner values are therefore never templated into SQL, and people can write queries without the right to change credentials.
- Every value reaches the database as a bound parameter.

## Connection settings

### Every engine

| Setting | Default | Meaning |
|---|---|---|
| `MinPoolSize` | 2 | Idle connections kept open, per node |
| `MaxPoolSize` | 20 | Most connections, per node |
| `ConnectTimeoutSeconds` | 15 | |
| `CommandTimeoutSeconds` | 30 | Default statement timeout |
| `MaxRows` | 1000 | Rows returned per query. Further rows stay behind an open cursor. |
| `CursorIdleTimeoutSeconds` | 60 | Idle cursors are closed after this |
| `AllowAdHocSql` | false | Accept raw SQL in a delivery's content instead of a statement name |
| `LogParameterValues` | false | Log parameter values at debug level |
| `ReceiveMode` | none | Default receive mode for subscriptions that do not set one |
| `ReceiveBatchSize` | 500 | Default rows per poll |

`AllowAdHocSql` is an ordinary setting, so anyone with `data-sources.edit` can turn it on.

### PostgreSQL

| Setting | Default | Notes |
|---|---|---|
| `Host`, `Database`, `UserName` *(required)* | | |
| `Password` *(required, secret)* | | |
| `Port` | 5432 | |
| `SslMode` | Prefer | Disable, Allow, Prefer, Require, VerifyCA or VerifyFull |
| `Schema` | | Sets the search path |
| `ApplicationName` | Bitween | |
| `AutoPrepare` | false | Prepares up to 20 frequently used statements |
| `ConnectionIdleLifetimeSeconds` | 300 | |

Placeholders are written `@name`.

### MySQL

| Setting | Default | Notes |
|---|---|---|
| `Host`, `Database`, `UserName` *(required)* | | `Database` is also the schema |
| `Password` *(required, secret)* | | |
| `Port` | 3306 | |
| `SslMode` | Preferred | None, Preferred, Required, VerifyCA or VerifyFull |
| `ApplicationName` | Bitween | |
| `ConnectionIdleLifetimeSeconds` | 180 | |
| `ServerPrepare` | true | When off, the save-time statement check cannot catch errors |
| `AllowZeroDateTime` | false | |

Placeholders are written `@name`. MySQL has no `RETURNING`, so `execute` returns only a row count.

### SQL Server

| Setting | Default | Notes |
|---|---|---|
| `Host`, `Database`, `UserName` *(required)* | | `host\instance` and `host,port` are used as written |
| `Password` *(required, secret)* | | |
| `Port` | 1433 | |
| `Schema` | | Applied by impersonating the schema's owner before each statement |
| `Encrypt` | true | |
| `TrustServerCertificate` | false | |
| `ApplicationName` | Bitween | |
| `MultipleActiveResultSets` | false | |
| `SnapshotIsolation` | false | Runs statements at snapshot isolation |

Placeholders are written `@name`. `Schema` and `SnapshotIsolation` each add a round trip per statement.

### Oracle

| Setting | Default | Notes |
|---|---|---|
| `Host`, `UserName` *(required)* | | |
| `Password` *(required, secret)* | | |
| `Port` | 1521 | |
| `ServiceName` or `Sid` | | Exactly one, unless `ConnectDescriptor` is set |
| `ConnectDescriptor` | | A full connect descriptor instead of the two above |
| `Schema` | | Only the schema browser's default. It does not change how unqualified names resolve. |
| `AsSysDba` | false | |
| `WalletDirectory` | | Applies to the whole adapter process |
| `FetchSize` | 100 | Applies to the whole adapter process |
| `BindByName` | true | |

Placeholders are written `:name`.

## Statements

A statement belongs to one data source.

| Field | Meaning |
|---|---|
| Name | Unique on the data source, ignoring case |
| SQL | The statement, or a bare procedure name |
| Description | |
| Work group | |
| Key column | For polling: the column that identifies a row |
| Cursor column | For polling: the column that orders rows and records progress |
| Inactive | Leaves the statement out of what the adapter can run |

Statements are managed on the data source's page and need the `data-source-statements` permissions. A statement cannot be deleted or renamed while a subscription uses it, and expanding a statement lists those subscriptions.

### Checked when saved

When a statement is saved, Bitween asks the running adapter whether the database accepts it.

| Engine | Check |
|---|---|
| PostgreSQL, MySQL | Prepares the statement with each placeholder bound to null |
| SQL Server | `sp_describe_undeclared_parameters` |
| Oracle | `DBMS_SQL.PARSE` |

- A bare procedure name passes without checking that the procedure exists.
- An error that looks like the wrong placeholder style comes with a hint.
- The check is skipped, and the save reports *not checked*, when the adapter is not running on the node serving the request.
- Editing a statement re-checks it only when the SQL changed.

> **Oracle executes DDL while parsing.** Saving or testing a statement such as `DROP TABLE` against an Oracle source runs it. Connect Oracle sources with a login that has no DDL rights, and grant statement permissions accordingly.

Saving any statement restarts the database's adapter on the next reconcile, within 30 seconds. A new statement can be used once that has happened.

## Receiving rows

Use the database adapter as a scheduled job's **Source**, and bind the job to the data source.

| Subscription setting | Meaning |
|---|---|
| Data source | The connection |
| `ReceiveStatement` | The statement to poll |
| `ReceiveMode` | How progress is tracked. Defaults to the data source's. |
| `MarkProcessedStatement` | A statement run for each accepted row. Required by `bulk` and `marker`. |
| `ReceiveBatchSize` | Rows per poll. Defaults to the data source's. |

The key and cursor columns belong to the polled statement. The subscription page can edit them, which saves the statement straight away.

| Mode | How rows are chosen |
|---|---|
| `incrementing` | The statement filters on a numeric `cursor` parameter, and the cursor column must only increase |
| `timestamp` | The same, with `cursor` as a UTC timestamp |
| `timestamp+incrementing` | Currently the same as `timestamp`. There is no tie-break column. |
| `bulk` | The statement returns rows, and the mark-processed statement deals with each accepted one |
| `marker` | The statement returns rows not yet marked, and the mark-processed statement marks them |

On the first run the cursor is 0, or 1900-01-01 for timestamps.

```sql
select id, order_no, customer, updated_at
from orders
where id > @cursor
order by id
```

Each run does the following.

1. Runs the statement with the cursor, taking at most the batch size of rows.
2. Turns each row into one exchange. The input is a JSON object keyed by column names as the database returns them, so Oracle names are upper case. The file is named `{key}.json`.
3. After each exchange is stored, runs the mark-processed statement with `key` and every column of the row as parameters, then saves the cursor at that row's value.

- **Order the statement by the cursor column.** The cursor moves to each accepted row's value in turn, so unordered rows can be skipped or read again.
- The cursor is kept per subscription, so several scheduled jobs can poll the same database independently.
- If a row fails before its exchange is stored, the run fails and the row is read again next time.
- If a row's exchange is stored but marking it or saving the cursor fails, the row's key is remembered for its subscription (the last 1,000). The next poll that reads it runs the marking again instead of creating a second exchange, and skips it if the marking still fails.
- If Bitween stops between storing a row's exchange and marking it, nothing was remembered, and the next poll creates the exchange again. Receiving is at least once.

## Writing with a handler

Use the database adapter as a subscription's **Delivery**, and bind it to the data source.

| Subscription setting | Meaning |
|---|---|
| `Statement` | The statement to run |
| `Operation` | `query` (the default), `execute` or `call` |

With a statement set, the exchange content must be a JSON object. Each property becomes the parameter of the same name, with or without the engine's prefix. Nested objects and arrays are sent as JSON text, and null as a database null.

```json
{ "order_no": "SO-1001", "customer": "Acme", "total": 125.5 }
```

```sql
insert into orders (order_no, customer, total)
values (@order_no, @customer, @total)
returning id
```

Without a statement, the content must be a request that names one, or carries raw SQL when `AllowAdHocSql` is on.

```json
{ "name": "insert-order", "parameters": { "order_no": "SO-1001" }, "timeoutSeconds": 10 }
```

| Operation | For | Rows returned |
|---|---|---|
| `query` | Selects | Up to `MaxRows` |
| `execute` | Inserts, updates and deletes | `RETURNING` or `OUTPUT` rows on PostgreSQL, SQL Server and Oracle; only a count on MySQL |
| `call` | Stored procedures | Rows that a MySQL or SQL Server procedure selects. None from PostgreSQL procedures or Oracle REF CURSORs. |

The response file is JSON with `rows`, `affectedRows`, `output`, `columns` and `elapsedMs`, plus `cursorId` and `hasMore` when rows were left over.

- Each statement commits on its own.
- A database error fails the exchange, and retry policies see it as an `Error`. A database result is never a bad response.
- Rows beyond `MaxRows` cannot be fetched by a delivery, and the cursor left open holds a pooled connection until it times out. Keep delivery queries under the limit.

## Testing and browsing

- **Test connection** checks the connection, authentication, a simple query and the login's privileges, then checks every active statement and reports each result.
- **Stats** shows the running adapter's counters.
- The **schema browser** lists tables, views, functions, procedures and sequences, with approximate row counts. **Use in a statement** fills in the statement form with a starting query in the engine's style. Names that are not lower case are wrapped in double quotes, which MySQL rejects unless `ANSI_QUOTES` is on.

These all ask the adapter on the node serving the request. See [Data sources](data-sources.md#testing-and-inspecting).

## API

| Method and path | Permission |
|---|---|
| `GET /api/datasourcestatements` | `data-source-statements.view`, or any signed-in member with `lookup=true` |
| `GET /api/datasourcestatements/{id}` | `data-source-statements.view` |
| `POST /api/datasourcestatements` | `data-source-statements.create`. The body carries `dataSourceId`. Returns `{ id, checked }`. |
| `POST /api/datasourcestatements/{id}` | `data-source-statements.edit`. Returns `{ id, checked }`. |
| `DELETE /api/datasourcestatements/{id}` | `data-source-statements.delete` |
| `POST /api/datasourcestatements/{id}/usage` | `data-source-statements.view` |
| `POST /api/datasources/{id}/inspect` | `data-sources.view` |

Inspect accepts `{ "command": "Discover", "arguments": { ... } }` with `objectType`, `schema`, `nameLike` and `take` (1 to 1000, default 200), or `{ "command": "Describe" }` for the engine's capabilities, placeholder prefix and the login's privileges.

## Limits

- The database adapters do not declare the mapper role, so they are not offered as mappers.
- Bitween does not check that a subscription's data source matches its adapter. A mismatch fails when an exchange runs.
- A subscription that uses a database adapter without a bound data source returns its own connection settings, including the password, unmasked.
- Anyone with `data-sources.view` can read the database catalogue and the login's privileges.
- Data source settings, including passwords, are stored unencrypted.
