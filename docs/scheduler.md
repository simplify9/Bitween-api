# Scheduler

Bitween uses **SW-Scheduler** (`SimplyWorks.Scheduler.*`) as its job scheduling backbone. SW-Scheduler is a thin, opinionated wrapper around [Quartz.NET](https://www.quartz-scheduler.net/) that replaces Quartz's raw `IJob` / `ITrigger` APIs with typed C# records and attributes.

---

## Why SW-Scheduler instead of raw Quartz

Quartz.NET is powerful but verbose: you wire jobs through `IJobDetail`, pass runtime data via an untyped `JobDataMap`, and manage trigger keys manually. SW-Scheduler removes that boilerplate:

| Raw Quartz | SW-Scheduler equivalent |
|---|---|
| `IJob.Execute(IJobExecutionContext)` | `IScheduledJob.Execute()` or `IScheduledJob<TParam>.Execute(TParam)` |
| `JobDataMap` string dictionary | Typed `TParam` record, serialized/deserialized automatically |
| Trigger keys + `IScheduler.ScheduleJob(...)` | `IScheduleRepository.Schedule<TJob, TParam>(param, cron, key)` |
| Quartz attributes on the class | `[Schedule]`, `[RetryConfig]`, `[ScheduleConfig]` |
| `IJobExecutionContext.Scheduler.Clustered` | `EnableClustering = true` on the provider options |
| Separate `JobStore` configuration | EF Core migration in the same DB as the app |

The result is that Bitween's two background jobs are about 50 lines of plain C# each, with no Quartz types in their signatures.

---

## NuGet packages

SW-Scheduler is published on NuGet.org under the `SimplyWorks.Scheduler.*` prefix. The solution references them as follows:

| NuGet package | Version | Referenced by | What it adds |
|---|---|---|---|
| `SimplyWorks.Scheduler.Sdk` | 8.1.1 | `SW.Bitween.Api` | `IScheduledJob<TParam>`, `[ScheduleConfig]`, `IScheduleRepository` interfaces — no Quartz dependency |
| `SimplyWorks.Scheduler.EfCore` | 8.1.1 | `SW.Bitween.Web` | `AddSchedulerMonitoring<TDbContext>()`, `job_executions` EF model |
| `SimplyWorks.Scheduler.PgSql` | 8.1.1 | `SW.Bitween.PgSql` | `AddPgSqlScheduler(...)`, `modelBuilder.UseSchedulerPostgreSql(schema)` |
| `SimplyWorks.Scheduler.SqlServer` | 8.1.1 | `SW.Bitween.MsSql` | `AddSqlServerScheduler(...)`, `modelBuilder.UseSchedulerSqlServer()` |
| `SimplyWorks.Scheduler.MySql` | 8.1.1 | `SW.Bitween.MySql` | `AddMySqlScheduler(...)`, `modelBuilder.UseSchedulerMySql()` |

`SW.Bitween.Api` references only `SimplyWorks.Scheduler.Sdk` — it defines jobs and uses `IScheduleRepository` but has no dependency on Quartz itself. Each DB provider project references the matching provider package, which transitively brings in the full Quartz runtime. `SW.Bitween.Web` adds `SimplyWorks.Scheduler.EfCore` directly; the three provider packages reach it transitively through the DB provider project references.

---

## The two Bitween jobs

### `ReceivingJob`

Polls a configured receiver adapter for new files and creates an inbound `Xchange` for each one.

```csharp
public record ReceivingJobParams(int SubscriptionId, string? CronExpression);

[ScheduleConfig(AllowConcurrentExecution = false, MisfireInstructions = MisfireInstructions.Skip)]
public class ReceivingJob(
    BitweenDbContext dbContext,
    RunFlagUpdater runFlagUpdater,
    NativeAdapterDiscoveryService nativeAdapterDiscovery,
    IServerlessService serverless,
    XchangeService xchangeService,
    ILogger<ReceivingJob> logger) : IScheduledJob<ReceivingJobParams>
{
    public async Task Execute(ReceivingJobParams jobParams) { ... }
}
```

- `AllowConcurrentExecution = false` — Quartz will not fire a second instance of this job for the same subscription while one is still running.
- `MisfireInstructions.Skip` — if the scheduler was down at the scheduled fire time, skip that execution rather than pile up missed runs.
- The `RunFlagUpdater` adds a DB-level guard (`is_running` on the subscription) so that even across a cluster restart, two nodes cannot run the same subscription's job simultaneously.

### `AggregationJob`

Collects successful `Xchange` records belonging to a source subscription, generates a single aggregation `Xchange` containing their file URLs, and marks each source Xchange as aggregated.

```csharp
public record AggregationJobParams(int SubscriptionId, string? CronExpression);

[ScheduleConfig(AllowConcurrentExecution = false, MisfireInstructions = MisfireInstructions.Skip)]
public class AggregationJob(
    BitweenDbContext dbContext,
    XchangeService xchangeService,
    ILogger<AggregationJob> logger) : IScheduledJob<AggregationJobParams>
{
    public async Task Execute(AggregationJobParams jobParams) { ... }
}
```

The aggregation query selects Xchanges where `XchangeResult.Success = true` and no `XchangeAggregation` link exists yet, so each source Xchange is included in exactly one aggregation batch.

---

## Schedules: from Subscription to Quartz trigger

Scheduling is driven by the `Schedule` owned entity on each `Subscription`. A `Schedule` stores a `Recurrence` (Hourly / Daily / Weekly / Monthly) and a `TimeSpan On` that encodes the offset within the period.

### Schedule → cron conversion

`ScheduleToCronExtension.ToCronExpression()` converts a `Schedule` to a 6-field Quartz cron string:

| Recurrence | Example `On` | Cron output |
|---|---|---|
| `Hourly` | `00:15:00` | `0 15 * * * ?` |
| `Daily` | `02:30:00` | `0 30 2 * * ?` |
| `Weekly` | `1.08:00:00` (Mon 08:00) | `0 0 8 ? * 2` |
| `Monthly` | `15.09:00:00` (15th 09:00) | `0 0 9 15 * ?` |

Quartz uses 6-field syntax (`second minute hour day-of-month month day-of-week`); day-of-week is 1-based starting from Sunday.

### Schedule key

Each `(subscription, schedule)` pair maps to a deterministic Quartz schedule key:

```
receiver-{subscriptionId}-{recurrence}-{on.Ticks}-{backwards ? 1 : 0}
aggregator-{subscriptionId}-{recurrence}-{on.Ticks}-{backwards ? 1 : 0}
```

The key is stable across restarts, which is what makes `ScheduleIfNotExists` idempotent.

---

## `SubscriptionSchedulerService`

This scoped service is the bridge between the Bitween domain and Quartz. It wraps `IScheduleRepository` with subscription-aware logic:

```csharp
// Registers all active schedules for a subscription (idempotent).
await subScheduler.ScheduleAll(sub);

// Syncs after an update — removes old triggers, adds new ones.
// Pass oldSchedules captured BEFORE calling sub.SetSchedules(...).
await subScheduler.Sync(sub, oldSchedules);

// Triggers one immediate execution outside the cron cadence.
await subScheduler.RunNow(sub);
```

`Sync` is called from the subscription update command handler whenever a subscription's schedules change or its active/inactive state is toggled.

---

## `SchedulerSeedService`

A `BackgroundService` that runs once on startup and registers all active Receiving and Aggregation subscriptions with Quartz. It uses `ScheduleIfNotExists` so that restarting a node against a persistent Quartz store never creates duplicate triggers:

```
startup
  └── SchedulerSeedService.ExecuteAsync()
        ├── query all active Receiving + Aggregation subscriptions with at least one Schedule
        └── for each: SubscriptionSchedulerService.ScheduleAll(sub)
              └── IScheduleRepository.ScheduleIfNotExists<Job, Param>(param, cron, key)
```

---

## Quartz tables in the database

Quartz stores its own state (job definitions, triggers, calendar data, cluster locks) in a set of `qrtz_*` tables. In Bitween these tables live **in the same database as the application** under the same schema, added via normal EF Core migrations.

Each DB provider calls the matching extension in `OnModelCreating`:

```csharp
// SW.Bitween.PgSql/BitweenDbContext.cs
modelBuilder.UseSchedulerPostgreSql(Schema);  // all qrtz_* + job_executions

// SW.Bitween.MySql/BitweenDbContext.cs
modelBuilder.UseSchedulerMySql();

// SW.Bitween.MsSql/BitweenDbContext.cs
modelBuilder.UseSchedulerSqlServer();
```

The Quartz tables were added in migration `Quartz` (generated 2026-06-14) in each provider project. Apply like any other migration:

```bash
# PostgreSQL
dotnet ef database update --project SW.Bitween.PgSql

# MySQL
dotnet ef database update --project SW.Bitween.MySql

# SQL Server
dotnet ef database update --project SW.Bitween.MsSql
```

Tables added by the migration:

| Table | Purpose |
|---|---|
| `qrtz_job_details` | Registered job types and their serialized data maps |
| `qrtz_triggers` | All triggers (base row for every trigger type) |
| `qrtz_cron_triggers` | Cron expression per cron trigger |
| `qrtz_simple_triggers` | Interval/count for simple triggers (one-off runs) |
| `qrtz_simprop_triggers` | Property bag for calendar interval and daily triggers |
| `qrtz_blob_triggers` | Fallback for non-standard trigger types |
| `qrtz_fired_triggers` | Currently executing or recently fired triggers |
| `qrtz_scheduler_state` | Heartbeat rows per cluster node |
| `qrtz_locks` | Pessimistic row-level locks used by the clustering algorithm |
| `qrtz_paused_trigger_grps` | Paused trigger group names |
| `qrtz_calendars` | Named calendars for blackout dates |
| `job_executions` | SW-Scheduler execution history (not a Quartz table) |

---

## Job execution monitoring

`AddSchedulerMonitoring<TDbContext>()` registers `IJobExecutionStore` backed by the application's `BitweenDbContext`. After each job run, SW-Scheduler writes a row to `job_executions`:

| Column | Description |
|---|---|
| `job_name` / `job_group` | Quartz job identity |
| `fire_instance_id` | Unique per execution; cluster-safe |
| `start_time_utc` / `end_time_utc` / `duration_ms` | Timing |
| `success` / `error` | Outcome |
| `node` | `Environment.MachineName` of the executing node |
| `context` | JSON blob — contains `JobParameter` (the serialized `ReceivingJobParams` / `AggregationJobParams`) |

---

## Host registration (`Startup.cs`)

The scheduler is registered in the provider-specific block of `Startup.ConfigureServices`, using the same connection string as the application database:

```csharp
// PostgreSQL
services.AddPgSqlScheduler(
    connectionString: connectionString,
    schema: PgSql.BitweenDbContext.Schema,
    configure: o => o.EnableClustering = true,
    assemblies: typeof(BitweenDbContext).Assembly);

// SQL Server
services.AddSqlServerScheduler(
    connectionString: connectionString,
    configure: o => o.EnableClustering = true,
    assemblies: typeof(BitweenDbContext).Assembly);

// MySQL
services.AddMySqlScheduler(
    connectionString: connectionString,
    configure: o => o.EnableClustering = true,
    assemblies: typeof(BitweenDbContext).Assembly);

// Shared — regardless of provider
services.AddScoped<SubscriptionSchedulerService>();
services.AddHostedService<SchedulerSeedService>();
services.AddSchedulerMonitoring<ProviderSpecificBitweenDbContext>();
```

`EnableClustering = true` means each node acquires a DB lock before firing a trigger, preventing duplicate execution in a horizontally scaled deployment.

---

## Adding a new scheduled job type

1. Add a `record` for the parameters in `SW.Bitween.Api`:
   ```csharp
   public record MyJobParams(int SubscriptionId, string? CronExpression);
   ```

2. Implement `IScheduledJob<MyJobParams>` in the same project:
   ```csharp
   [ScheduleConfig(AllowConcurrentExecution = false, MisfireInstructions = MisfireInstructions.Skip)]
   public class MyJob(BitweenDbContext db, ...) : IScheduledJob<MyJobParams>
   {
       public async Task Execute(MyJobParams p) { ... }
   }
   ```

3. In `SubscriptionSchedulerService`, add a branch to `Schedule` / `TryUnschedule` / `RunNow` for the new subscription type.

4. No Quartz plumbing needed — discovery is automatic because `assemblies: typeof(BitweenDbContext).Assembly` is passed to the provider registration.
