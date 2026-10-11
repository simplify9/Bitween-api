# Connecting Bitween to a database: a guide per engine

Written for a developer who knows SQL and has met a relational database before, but has not
necessarily met *this* one. It assumes nothing about Bitween beyond the fact that you are pointing
it at a database and want it to work.

Each engine gets the same five questions answered:

1. **What must I set**, and what happens if I leave the rest alone.
2. **How do I write a statement** - placeholders, row limits, identifier case.
3. **What does the database hand back**, and as what.
4. **How do I call a procedure**, and will it return rows.
5. **What will go wrong**, and what the message means when it does.

The terse settings tables live in
[database-adapters-api.md §2](database-adapters-api.md). This is the explanation behind them.

---

## The parts that are the same everywhere

**A data source is a connection, held open.** The adapter is a long-lived process with an ADO.NET
pool inside it, so a connect, a TLS handshake and an authentication round trip are paid once rather
than per message. That is the whole reason it exists, and it is why the pool settings matter more
than they would in a web app: `MinPoolSize` connections are held **per node**, so multiply by your
replica count before comparing against the server's session limit.

**SQL lives on the data source, not in a message.** You create *statements* - named pieces of SQL
belonging to the connection - and a subscription names one. A message supplies parameter *values*
and never SQL text. This is not ceremony: adapter property values have `{{partner.X}}` substituted
into them before the adapter sees them, so SQL in a subscription's properties would be steerable by
ordinary partner data.

**A statement is checked when you save it.** The engine parses and plans it - never runs it - so a
typo is refused while you are still looking at the form. If the connection is not running at that
moment the save says *"Saved, but not checked"* rather than pretending.

**Only the engine's own answers are reported.** The capability list (`Describe`) is what *this*
server can do, narrowed by version where it matters. If it says `merge: false`, writing a `MERGE`
will fail, and the list is telling you so first.

---

## PostgreSQL - `bitween.db.postgresql`

The easiest of the four to configure, and the one most likely to be right by default.

### What you must set

`Host`, `Database`, `UserName`, `Password`. That is genuinely all.

`Database` is required and has no default, because **PostgreSQL connects to a database, not to a
server** - there is no "log in and then pick one". It is the name you would pass to `psql -d`.

### What the defaults do

| Setting | Default | Leave it unless… |
|---|---|---|
| `Port` | 5432 | |
| `SslMode` | `Prefer` | **Change this to `Require` for anything not on localhost.** `Prefer` will silently fall back to an unencrypted connection if the server does not offer TLS - which is precisely the case you wanted to be told about. |
| `Schema` | server default | You want unqualified names to resolve somewhere other than `public`. Sets `search_path`; takes a list, e.g. `sales, public`. |
| `ApplicationName` | `Bitween` | Keep it distinctive - it is how a DBA finds your connections in `pg_stat_activity`. |
| `AutoPrepare` | `false` | You are running the same statement constantly and have measured a win. It caches a plan per connection, and a plan built against one shape of data can be worse than replanning. |
| `ConnectionIdleLifetimeSeconds` | 300 | |

### Writing a statement

- **Parameters are `@name`.** *Not* `:name` - that collides with the `::` cast operator, and
  `value::text` would be read as a parameter called `text`.
- **Row limit is `limit N`**, at the end.
- **Identifiers fold to lower case** unless quoted. A table created as `"Orders"` must always be
  written `"Orders"`; one created as `Orders` is `orders` and can be written either way.

```sql
select id, order_no, total
  from sales.orders
 where customer_id = @customerId
 order by id
 limit 100
```

### What comes back

`numeric` → decimal, `timestamptz` → DateTime, `uuid` → Guid, `jsonb` → a string, `bytea` → bytes,
arrays → an array. A row count from `Discover` is `reltuples`, which is as fresh as the last
`ANALYZE` and is **null on a table that has never been analysed** - that is an honest "unknown",
not a zero.

### Procedures and functions

This is the one thing PostgreSQL does differently from the other three, and it catches people:

- **A `PROCEDURE` called with `CALL` cannot return a result set.** The capability list says
  `procedureResultSets: false` for exactly this reason.
- **A set-returning `FUNCTION` can**, and it is queried with `SELECT`, not `CALL`:

  ```sql
  select * from orders_by_customer(@code)
  ```

  So a "stored procedure that returns rows" from an Oracle or SQL Server background is a *function*
  here, and it goes through Query rather than Call.

### Receiving

Any always-increasing column works as a cursor - a `serial`/`identity` id, or a `timestamptz`
updated on write. Use `where id > @cursor order by id`; the `order by` is not optional, because
without it the last row read is arbitrary and the cursor will skip rows.

### What goes wrong

| Message | What it means |
|---|---|
| `42P01: relation "x" does not exist` | Wrong name, wrong schema, or your `search_path` does not include it. Qualify it or set `Schema`. |
| `42703: column "x" does not exist` | Usually a case problem - the column was created quoted with capitals. |
| `syntax error at or near ":"` | You wrote `:name`. Use `@name`. The save-time check says this in words. |
| `merge: false` in the capability list | The server is older than 15. `MERGE` does not exist there; use `insert … on conflict`. |

---

## MySQL - `bitween.db.mysql`

Serves MariaDB too. Configuration is simple; the surprises are in what the engine does *not* have.

### What you must set

`Host`, `Database`, `UserName`, `Password`.

**`Database` is also the schema.** MySQL has no separate concept - `CREATE SCHEMA` is a synonym for
`CREATE DATABASE` - so this one value is both what you connect to and what an unqualified table name
resolves against. There is no `search_path` equivalent and no separate `Schema` setting, and
browsing the catalog with no schema filter means *this database*, not every database on the server.

### What the defaults do

| Setting | Default | Leave it unless… |
|---|---|---|
| `Port` | 3306 | |
| `SslMode` | `Preferred` | **`Required` or above off localhost**, same reasoning as PostgreSQL's `Prefer`. |
| `ConnectionIdleLifetimeSeconds` | 180 | **Check your server's `wait_timeout`.** The server default is 28800, but a managed instance or a proxy in front of one is routinely far lower. A connection the *server* closed first surfaces as a broken pipe on the next message rather than as a timeout, so keep this comfortably below it. |
| `ServerPrepare` | `true` | You connect through ProxySQL or similar, where prepared statements pin a backend and defeat the pooling the proxy exists to provide. **Turning it off also weakens the save-time check** - with `IgnorePrepare` on, nothing is sent to the server and every statement reports valid. |
| `AllowZeroDateTime` | `false` | The schema contains `0000-00-00`, which MySQL permits and no .NET date type can hold. Only old schemas have these. |

### Writing a statement

- **Parameters are `@name`.**
- **Row limit is `limit N`**, at the end.
- **Identifier case follows the file system** on the server: case-sensitive on Linux,
  case-insensitive on Windows and macOS. Write table names exactly as they were created and the
  question never comes up.
- Backticks quote an identifier, not double quotes (unless `ANSI_QUOTES` is set).

### What comes back

| Column type | Arrives as | Worth knowing |
|---|---|---|
| `tinyint(1)` | **bool** | This *is* the boolean type - MySQL has no other. The driver returns a bool, so do not expect 0/1. |
| `bigint unsigned` | decimal | It does not fit in a signed 64-bit integer. |
| `int unsigned` | long | Same reason, one size up. |
| `decimal` | decimal | |
| `datetime`, `timestamp` | DateTime | `timestamp` is stored UTC and converted to the session time zone; `datetime` is not converted at all. |
| `json` | string | |
| `blob` family | bytes | |

A row count from `Discover` is InnoDB's estimate from index statistics and can be out by a wide
margin on a table that has not been analysed. It is always null for a view.

### Procedures and functions

**A procedure returns rows simply by `SELECT`ing** - no cursor to declare, nothing to bind. This is
the capability PostgreSQL declares false, and it means the obvious thing works:

```sql
create procedure orders_by_customer(in p_code varchar(20))
begin
    select * from orders where code = p_code;
end
```

A statement meant to be **called** holds the procedure's *name*, not SQL - `orders_by_customer`,
not `call orders_by_customer(...)`. The parameters are bound by the message.

### What it does not have

This is the part worth reading before you design against it:

- **No sequences.** `AUTO_INCREMENT` belongs to a column, not to an object. If you need a shared
  counter, a one-row table is the usual stand-in.
- **No `MERGE`.** The upsert is `insert … on duplicate key update`, which has different semantics.
- **No `RETURNING`.** (MariaDB has it for `INSERT` and `DELETE`; MySQL does not, and the capability
  list says false so that a statement written against it fails here rather than in production.)
- **No array type.** A JSON array is the stand-in and arrives as a string.

### Receiving

An `AUTO_INCREMENT` id is the natural cursor. A `timestamp` column with
`on update current_timestamp` works for `timestamp` mode - but note that `timestamp` has
second resolution unless you declare `timestamp(3)` or finer, and two rows written in the same
second can straddle a poll boundary. Prefer the id where you have one.

### What goes wrong

| Message | What it means |
|---|---|
| `Table 'db.x' doesn't exist` | Wrong database, or a case mismatch on a Linux server. |
| `You have an error in your SQL syntax … near ':name'` | You wrote `:name`. Use `@name`. |
| `You do not have the SUPER privilege and binary logging is enabled` | You are trying to **create a function**, not to run one. The server needs `log_bin_trust_function_creators=1`, which is a DBA switch - Bitween never creates routines. |
| Broken pipe / "server has gone away" on the first message after a quiet period | `ConnectionIdleLifetimeSeconds` is above the server's `wait_timeout`. |

---

## SQL Server - `bitween.db.sqlserver`

Serves Azure SQL too. The configuration surprise here is encryption; the SQL surprise is `TOP`.

### What you must set

`Host`, `Database`, `UserName`, `Password` - and very likely `TrustServerCertificate`.

**`Encrypt` defaults to `true`**, which is a change from the old `System.Data.SqlClient` everyone
learned on. The modern driver encrypts by default and then *validates the certificate*, and the
usual on-premises instance presents a self-signed one. That combination is why an instance that
worked for years starts failing the moment something is upgraded.

- **Azure SQL**: leave both alone. The certificate is real and validates.
- **On-premises with a self-signed certificate**: set `TrustServerCertificate = true`, knowing that
  it means an attacker between you and the server could present their own. Installing the
  certificate properly is better; this is the pragmatic answer.

**A named instance is addressed through `Host`, not `Port`.** Write `SERVER\SQLEXPRESS` and leave
the port alone - a named instance is resolved by the SQL Server Browser service, and supplying both
is an error the driver reports obscurely.

### What the defaults do

| Setting | Default | Leave it unless… |
|---|---|---|
| `Port` | 1433 | You are using a named instance, in which case it is ignored. |
| `Schema` | the login's own | You want unqualified names to resolve to something other than the login's default (usually `dbo`). |
| `MultipleActiveResultSets` | `false` | Something specifically needs it. It stops the connection being reset cleanly between uses, which is the opposite of what a shared pool wants. |
| `SnapshotIsolation` | `false` | You want readers not to block writers. Only has an effect where the database has snapshot isolation enabled. |

**A note on `Schema`.** SQL Server has no `search_path`: the default schema is a property of the
*login*, not of the connection. Bitween applies the setting per connection with `EXECUTE AS`, and
**failure is deliberately not fatal** - impersonating a schema's owner is a privilege many service
accounts do not have. If it cannot, the connection still works and unqualified names resolve as they
would have. Qualify your names (`sales.orders`) and the question never arises.

### Writing a statement

- **Parameters are `@name`.**
- **Row limit is `top N`, and it goes *before* the column list** - not at the end. This is the
  dialect difference people hit first:

  ```sql
  select top 100 id, order_no, total
    from sales.orders
   where customer_id = @customerId
   order by id
  ```

  `offset … fetch next … rows only` also works and is what paging uses, but it **requires an
  `order by`**.
- **Schemas are real and worth using.** `dbo` is a default, not a rule.
- Square brackets quote an identifier: `[order]`.

### What comes back

| Column type | Arrives as | Worth knowing |
|---|---|---|
| `bit` | bool | |
| `decimal`, `money` | decimal | |
| `datetime2`, `datetime` | DateTime | Prefer `datetime2`; `datetime` has ~3ms resolution and a 1753 floor. |
| `datetimeoffset` | DateTimeOffset | |
| `uniqueidentifier` | Guid | |
| `nvarchar`, `varchar` | string | |
| `varbinary`, `rowversion` | bytes | |

A declared length you see in `Discover` is **halved for `n`-types**, because `max_length` in the
catalog is in bytes and an `nvarchar` stores two per character - so `nvarchar(50)` reports 50, as
you would write it, not 100.

Row counts come from the partition statistics, which the engine maintains - cheaper and closer to
true than most engines' estimates, but still an estimate.

### Procedures and functions

**A procedure returns rows by `SELECT`ing**, like MySQL. Put `set nocount on` at the top so the
"N rows affected" messages do not arrive as extra result sets.

Three kinds of function exist and all are listed: scalar (`FN`), inline table-valued (`IF`) and
multi-statement table-valued (`TF`). A **table-valued** function is the thing to `SELECT` from:

```sql
select * from sales.orders_for(@code)
```

A statement meant to be **called** holds the procedure's name - `sales.orders_by_customer`.

### What it has that the others may not

- **`MERGE`**, the real statement.
- **`OUTPUT`**, which does what `RETURNING` does - and does it for `MERGE` too:

  ```sql
  insert into sales.orders (order_no, total)
  output inserted.*
  values (@orderNo, @total)
  ```
- **Snapshot isolation**, which is genuinely useful for a polling receiver: a long read does not
  block the writers it is reading from.

### Receiving

An `identity` column is the natural cursor. `rowversion` is the SQL-Server-specific answer and is
strictly monotonic across the database - but it arrives as bytes, not a number, so it does not fit
the `incrementing` mode; use an identity or a `datetime2` column.

### What goes wrong

| Message | What it means |
|---|---|
| `A connection was successfully established … but then an error occurred during the login process` / certificate chain errors | `Encrypt=true` against a self-signed certificate. Set `TrustServerCertificate`. |
| `Invalid object name 'x'` | Wrong schema, most often - the login's default is not what you assumed. Qualify it. |
| `Incorrect syntax near ':'` | You wrote `:name`. Use `@name`. |
| `Invalid usage of the option NEXT in the FETCH statement` | `offset/fetch` without an `order by`. Add one, or use `top`. |
| `The multi-part identifier could not be bound` | Almost always a typo in an alias or a join. |

---

## Oracle - `bitween.db.oracle`

The most configuration of the four, and the most dialect to remember. Everything here is normal
Oracle; none of it is Bitween being awkward.

### What you must set

`Host`, **exactly one of `ServiceName` or `Sid`**, `UserName`, `Password`.

Both or neither is refused with a message saying so rather than guessed at. `ServiceName` is what a
modern Oracle wants - it is what `lsnrctl services` lists, e.g. `FREEPDB1` or `ORCLPDB1`. `Sid` is
for an older instance that has no service name.

For **RAC, Data Guard, or Autonomous Database**, use `ConnectDescriptor` instead of host/port/
service: a full TNS descriptor or an Easy Connect string. Pair it with `WalletDirectory` (the
folder holding `cwallet.sso`) for mTLS or cloud wallets.

### What the defaults do

| Setting | Default | Leave it unless… |
|---|---|---|
| `Port` | 1521 | |
| `Schema` | the login's own | Only the schema browser's default today. It does not set `CURRENT_SCHEMA`, so qualify names in statements. |
| `BindByName` | `true` | **Never turn this off.** With it off ODP.NET binds by *position*, so a statement using `:id` twice - or one whose parameters arrive in a different order than they appear - silently binds the wrong values. Silently. |
| `FetchSize` | 100 | Sets the driver's fetch size for the whole adapter process. The adapter multiplies the value by 1024 before applying it. |
| `AsSysDba` | `false` | Almost never right for an integration login. |

### Writing a statement

- **Parameters are `:name`.** This is the one engine of the four that uses a colon, and it is the
  most common mistake when SQL is copied between data sources. The save-time check names it
  explicitly: *"write `:code` rather than `@code`"*.
- **Row limit is `fetch first N rows only`**, at the end (12c and later).
  **Do not use `rownum` with an `order by`** - `rownum` is applied *before* the sort, so you get an
  arbitrary N rows and then sort those. It is the classic Oracle trap.
- **Identifiers are stored UPPER CASE** unless they were created quoted. A table created as
  `orders` is `ORDERS` in the catalog, and the schema browser will show it that way. Unquoted SQL
  is case-insensitive, so `select * from orders` works regardless.
- Oracle has no `boolean` in SQL before 23c - a flag is `NUMBER(1)` or `CHAR(1)`. Compare with
  `= 1` or `= 'Y'` accordingly.

```sql
select id, order_no, total
  from orders
 where customer_id = :customerId
 order by id
 fetch first 100 rows only
```

### What comes back

| Column type | Arrives as | Worth knowing |
|---|---|---|
| `NUMBER`, any precision | decimal | Every `NUMBER` is reported as decimal, including `NUMBER(10,0)`. The type hint is coarse on purpose - it tells a mapper author to expect a number rather than a string, and `NUMBER` genuinely has more range than any binary float. |
| `VARCHAR2`, `CLOB` | string | |
| `DATE` | DateTime | Oracle's `DATE` **includes a time**, unlike every other engine here. |
| `TIMESTAMP WITH TIME ZONE` | DateTime | |
| `RAW`, `BLOB` | bytes | |

A row count from `Discover` is `ALL_TABLES.NUM_ROWS`, which is **null until statistics are
gathered** (`DBMS_STATS.GATHER_TABLE_STATS`). Null means unknown, not empty.

### Procedures and functions

**A procedure returns rows through an explicit `SYS_REFCURSOR` out parameter** - this is Oracle's
answer to "a procedure that returns a result set", and the adapter binds and reads it for you:

```sql
create or replace procedure orders_for_customer(p_code in varchar2, p_rows out sys_refcursor)
as
begin
    open p_rows for select * from orders where code = p_code;
end;
```

A statement meant to be **called** holds the procedure's name - `orders_for_customer`. Because a
name is not SQL, the save-time check accepts it with a note saying its existence was not verified;
it is resolved when called.

### Receiving

An `identity` column (12c+) or a sequence-backed id is the natural cursor. Note that **a sequence
does not guarantee gap-free or commit-ordered values** - two sessions can take 5 and 6 and commit in
the other order, so a poll between the commits can miss one. Where that matters, use a
`marker` column (a processed flag) rather than a cursor, which is what the `marker` receive mode is
for.

### What goes wrong

| Message | What it means |
|---|---|
| `ORA-00942: table or view does not exist` | Wrong name, wrong schema, or **no grant** - Oracle reports "does not exist" for an object you cannot see, which is the same message either way. Check `ALL_TAB_PRIVS` before assuming a typo. |
| `ORA-00936: missing expression` | Often `@name` where `:name` was meant. The check says so. |
| `ORA-01017: invalid username/password` | As it says. Note that passwords are case-sensitive from 11g. |
| `ORA-12514: service not registered with the listener` | `ServiceName` is wrong, or the database is still starting. |
| `ORA-01722: invalid number` | An implicit string-to-number conversion failed - usually a parameter bound as text against a `NUMBER` column. |

---

## Choosing a receive mode

Independent of engine, and the thing most worth getting right:

| Mode | Finds new rows by | Needs | Use when |
|---|---|---|---|
| `incrementing` | an always-growing column | cursor column | There is an id. The default answer. |
| `timestamp` | a modified-at column | cursor column | Rows are updated as well as inserted and you want both. |
| `timestamp+incrementing` | both | cursor column | Intended for rows that share a timestamp. There is no tie-break yet, so it currently behaves exactly like `timestamp`. |
| `marker` | a processed-flag column | a mark-processed statement | There is no reliable ordering, or commits arrive out of order. |
| `bulk` | re-reading everything | a mark-processed statement | The table is a queue that is emptied. |

**None of them can see a `DELETE`.** That is a property of polling, not of this adapter. If rows
disappear and that matters, the source needs a soft delete or an outbox.

Always `order by` the cursor column. Without it the "last row read" is whatever the engine happened
to return last, and the cursor will skip rows silently.

---

## A minimal grant

Bitween needs to read what it reads and write what it writes, plus enough catalog access for the
schema browser. It never creates objects.

| Engine | Minimum |
|---|---|
| PostgreSQL | `connect` on the database, `usage` on the schema, `select`/`insert`/`update` on the tables used. Catalog views are readable by default. |
| MySQL | `select` (plus `insert`/`update` where it writes) on the database, and `execute` on any routine it calls. `information_schema` is filtered by grant automatically. |
| SQL Server | `connect` on the database, `select`/`insert`/`update` on the objects, `execute` on the procedures, and `view definition` if you want the schema browser to show things the login has no other permission on. |
| Oracle | `create session`, `select`/`insert`/`update` on the objects, `execute` on the routines. The `ALL_*` catalog views show only what the login is granted, which is why a missing grant reads as "does not exist". |

Whatever you grant, press **Test connection**. It reports what it connected as, what the login is
actually allowed to do, and then parses every statement you have configured against the live schema
- which is the quickest way to find a missing grant, a dropped column, or a statement written in
another engine's dialect.
