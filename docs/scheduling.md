# Scheduling

Scheduled jobs and aggregations run on schedules. Bitween uses Quartz, with its job store in the Bitween database, through `SimplyWorks.Scheduler`.

## Schedules

A subscription can have several schedules. Each has a recurrence and a time, always in UTC.

| Recurrence | Time fields | Quartz cron |
|---|---|---|
| Hourly | minute | `0 {m} * * * ?` |
| Daily | hour, minute | `0 {m} {h} * * ?` |
| Weekly | day from 0 (Sunday) to 6, hour, minute | `0 {m} {h} ? * {day + 1}` |
| Monthly | day from 1 to 27, hour, minute | `0 {m} {h} {day} * ?` |

In the API a schedule looks like this.

```json
{ "recurrence": "Daily", "days": 0, "hours": 2, "minutes": 30, "backwards": false }
```

The `backwards` flag, shown as *count from the end*, is stored and is part of the job key, but the cron conversion ignores it.

Saving a subscription replaces its Quartz triggers, and deactivating it removes them. The UI gives new scheduled jobs a daily schedule at 00:00 UTC.

## Jobs

| Job | When | What it does |
|---|---|---|
| Receiving job | Each scheduled job's schedules | Runs the receiver. See [Scheduled jobs](entry-points.md#scheduled-jobs). |
| Aggregation job | Each aggregation's schedules | Rolls up finished exchanges. See [Aggregation](exchange-pipeline.md#aggregation). |
| Retry job | The **Retry poll schedule** setting, every minute by default | Runs due automatic retries, dropping any whose exchange was already retried |
| Receive attempt cleanup | `Bitween:ReceiveAttemptCleanupCron`, 03:00 UTC daily by default | Deletes receive attempts older than `Bitween:ReceiveAttemptRetentionDays`, 30 days by default |
| Inbound message prune | `Bitween:InboundMessagePruneCron`, 03:30 UTC daily by default | Deletes broker deduplication keys older than each data source's window |
| Exchange retention | The **Retention schedule** setting, 04:00 daily by default | Archives and removes exchanges older than **Keep exchanges (days)**. Does nothing while that is 0. See [Data retention](operations.md#data-retention). |

A job never runs concurrently with itself on one scheduler, and missed fires are skipped rather than caught up.

At startup, a background service registers the retry, cleanup and retention jobs, then the triggers of every active scheduled subscription. Registration is idempotent.

### Running flag

Before a scheduled job runs, it sets a running flag on the subscription with a conditional database update, and it clears the flag when it ends. If the flag is already set, the run is skipped. This stops two instances receiving for the same job at once.

If a run is killed before it finishes, the flag stays set and every later run is skipped. Schedule health reports this as **Stuck**. No API clears the flag, so it has to be reset in the database. Aggregations do not use the flag.

## Manual runs

**Receive now** and **Roll up now** schedule a one-off run immediately. Both need `subscriptions.operate`.

## Health and history

| Information | Shown on | Source |
|---|---|---|
| Consecutive failures, last error | Subscription overview and lists | Updated after each run, reset by a successful run |
| Receive attempts | Overview of scheduled jobs and aggregations | Bitween's own record of each run |
| Recent runs | Overview of other scheduled subscriptions | The scheduler's execution history |
| Last run, reliability | Scheduled jobs and Aggregations lists | The newest run, and successes among the last 20 finished runs |
| Schedule health | Status column of those lists | The Quartz triggers themselves |

Receive attempts say what the receiver found: data, with the exchanges it created; nothing new; or a failure and its message. The scheduler's own history only knows whether the job threw, and the receiving job catches its own errors, so receive attempts are the more useful record.

Schedule health reports the worst state across a subscription's triggers.

| State | UI label | Meaning |
|---|---|---|
| Normal | | The triggers exist and will fire |
| Missing | Not scheduled | There are fewer triggers than schedules, so a schedule will never fire |
| Paused | Trigger paused | A Quartz trigger is paused |
| Blocked | Blocked | A Quartz trigger is blocked |
| Error | Trigger error | A Quartz trigger is in error |
| Complete | Schedule ended | Every trigger has finished and will not fire again |
| Stuck | Stuck | The running flag is set but nothing is executing |

Saving the subscription again recreates missing triggers.

Bitween never pauses or deactivates a subscription because of repeated failures. The failure count is for information only.

## Caveats

- Deleting a subscription does not remove its Quartz triggers. They keep firing, and the job exits because the subscription is gone.
- The startup code states that Quartz clustering is not guaranteed with the pinned scheduler packages. With several replicas, only the running flag prevents duplicate receiving, and aggregations have no such flag.
