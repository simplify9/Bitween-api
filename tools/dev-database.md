# Dev database: a working set to develop against

The dev database ships with demo rows that **cannot be saved**: every subscription is a `Receiving`
type with no receiver and no schedule, which the update validator refuses. So opening any of them
in the UI and pressing Save fails, and nothing in the seed exercises a database data source at all.

This is how to get a dev environment whose records are valid and whose data sources actually
connect to something.

## 1. A database to integrate with

`dev-warehouse.sql` builds the shape an ERP integration actually meets - customers, orders,
order lines, an outbox that an integration drains, a set-returning function, a stored procedure and
a sequence - with 60 orders and 30 unsent shipment notifications.

There is one per engine, and they are deliberately the same schema with the same rows, so a
statement written against one is recognisably the same job on another and the differences you meet
are the ones that are real:

| File | Engine | What is different about it |
|---|---|---|
| `dev-warehouse.sql` | PostgreSQL | A set-returning function, because a PROCEDURE here cannot return rows. |
| `dev-warehouse-mysql.sql` | MySQL | A procedure that returns rows by SELECTing. No sequence - a counter table stands in. |
| `dev-warehouse-sqlserver.sql` | SQL Server | Everything in a `sales` schema; comments are extended properties; an inline table-valued function. |
| `dev-warehouse-oracle.sql` | Oracle | Parameters are `:name`; a REF CURSOR procedure; `fetch first` rather than `limit`. |

Each of those differences is explained in
[docs/database-adapters-per-engine.md](../docs/database-adapters-per-engine.md), along with what to
configure for each engine and what its errors mean.

```bash
# MySQL. --log-bin-trust-function-creators because creating a FUNCTION needs SUPER while binary
# logging is on, and the warehouse user is not SUPER.
docker run -d --name bw-mysql \
  -e MYSQL_ROOT_PASSWORD=root -e MYSQL_DATABASE=warehouse \
  -e MYSQL_USER=warehouse -e MYSQL_PASSWORD=warehouse \
  -p 55441:3306 mysql:8.4 --log-bin-trust-function-creators=1
docker exec -i bw-mysql mysql -uwarehouse -pwarehouse warehouse < tools/dev-warehouse-mysql.sql

# SQL Server. The 2022 image, not 2019: 2019 has no arm64 build and exits immediately on Apple
# silicon, which reads as "container is not running" rather than as anything about architecture.
docker run -d --name bw-mssql \
  -e ACCEPT_EULA=Y -e "MSSQL_SA_PASSWORD=Warehouse!2026" \
  -p 55442:1433 mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04
docker exec bw-mssql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'Warehouse!2026' -C \
  -Q "create database warehouse"
docker cp tools/dev-warehouse-sqlserver.sql bw-mssql:/tmp/w.sql
docker exec bw-mssql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'Warehouse!2026' -C \
  -d warehouse -i /tmp/w.sql

# Oracle. Slow to start - wait for "DATABASE IS READY TO USE" in the log before seeding.
docker run -d --name bw-oracle \
  -e ORACLE_PASSWORD=warehouse -e APP_USER=warehouse -e APP_USER_PASSWORD=warehouse \
  -p 55443:1521 gvenzl/oracle-free:23-slim-faststart
docker cp tools/dev-warehouse-oracle.sql bw-oracle:/tmp/w.sql
docker exec bw-oracle sqlplus -s warehouse/warehouse@localhost/FREEPDB1 @/tmp/w.sql
```

```bash
docker run -d --name bw-sample-db \
  -e POSTGRES_USER=warehouse -e POSTGRES_PASSWORD=warehouse -e POSTGRES_DB=warehouse \
  -p 55440:5432 postgres:16-alpine

docker cp tools/dev-warehouse.sql bw-sample-db:/seed.sql
docker exec bw-sample-db psql -U warehouse -d warehouse -f /seed.sql
```

## 2. Publish the database adapter

The provider only appears in the data source form once its package is in cloud storage. Publish it
through `ICloudFilesService.WriteAsync`, so the object's METADATA is written in the form
`AdapterInstaller` reads it back. With `StorageProvider: Local` and no bucket name configured it
lands under `.../SW.CloudFiles.LocalTests/default/adapters/`.

**If the app suddenly reports "metadata ... is missing 'EntryAssembly'", something deleted the
bucket.** The integration suite used to share it: `BitweenFixture` called
`AddLocalTestsCloudFiles()` with no bucket override and its teardown calls `Cleanup()`, which
deletes the bucket outright - so running the tests silently unpublished every adapter the dev
environment had, and the next thing anyone did failed with an error pointing nowhere near a test
run. The fixture now uses `bitween-integration-tests`; if you add another host that writes to the
local store, give it its own bucket too.

## 3. Repair the seeded subscriptions

Give each `Receiving` subscription a receiver and a schedule - through the API, so validation runs:

```
receiverId          native.httpreceiver
receiverProperties  Url = https://api.example/orders
schedules           one Hourly, 30 minutes
```

An hourly schedule with `hours: 1` is rejected as "Invalid hourly schedule"; the offset has to be
inside the hour, so use minutes.

## 4. Two subscriptions that use the database

Once a `Warehouse PostgreSQL` data source exists with statements on it:

| Subscription | Slot | Statement | Operation |
|---|---|---|---|
| Warehouse Outbox Drain | receiver | `drainOutbox` | query |
| Warehouse Order Intake | handler | `insertOrder` | execute |

The data source needs its receive settings for the drain to work: `ReceiveMode = marker`,
`KeyColumn = id`, and a `MarkProcessedStatement` that sets `processed`. Marker mode has no cursor,
so the mark is the only thing stopping the same rows being read on every poll - which is why the
adapter refuses to start in that mode without one.

## Proving it works

`POST /subscriptions/8/receivenow` drains a batch. With 30 unsent rows and a batch size of 25, two
polls produce 30 exchanges and leave the outbox empty, every row marked processed.
