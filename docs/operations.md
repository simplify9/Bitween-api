# Operations

## Health endpoint

`GET /health` returns 200 while the process is up. It does not check the database, RabbitMQ or storage.

## Finding an exchange

The Exchanges page filters by status, subscription, partner, information type, exchange ids, correlation id, a promoted property value and a date range. Filters live in the URL, so a filtered view can be shared as a link.

- Search by **promoted property** to find a business document, such as an order number.
- Search by **correlation id** to see every exchange in one flow, including exchanges created from responses.
- Searching by **id** also finds retries of that exchange and the aggregation that collected it.
- Turn on **Latest attempt only** to hide attempts that were already retried, leaving only the failures that still need action.

Expanding an exchange shows each stage's file, the exception, its retry chain, the aggregation family, and buttons to retry or to run a waiting retry now. An exchange that was already retried links to its later attempt instead of offering Retry.

## Exporting exchange files

Tick exchanges on the Exchanges page, or select everything a filter matches, and choose **Export files** to download their files as one zip. It needs only `exchanges.view`.

- Each exchange gets a folder named like its archive: the main promoted value, then the id (`Amman_3f2a…`). It holds `input`, `mapped` and `handled`, whichever the exchange has, with the extension of the file's name or content type.
- When some selected exchanges are older than storage keeps files, Export says how many first and asks before downloading the rest. When none have files left, it says so and downloads nothing.
- A file missing for another reason, such as one deleted by hand, is listed in `missing.txt` with the reason. The rest of the zip still comes.
- One export takes at most 500 exchanges. Narrow the filter for a larger set.

## Reading an exchange's state

| What you see | What it means | What to do |
|---|---|---|
| Processing for a long time | There is no result yet | Check Queue health for a backlog on the subscription's work group |
| Failed, with *Auto-retry in …* | A retry is scheduled | Wait, or use **Run now** |
| Failed, with a blocked reason | The retry policy declined | Read the reason, fix the cause, then retry by hand or reset the budget |
| Bad response | The handler flagged its response, such as an HTTP 4xx | Open the response file |
| Failed, with a link to a later attempt | This attempt was already retried | Follow the chain to its newest attempt |
| No exchange for a bus message | Nothing matched, or the information type disregards unfiltered messages | Check routes and match expressions on the Flow map |

## Queue health

The Queue health page reads the RabbitMQ management API every 5 seconds. It needs `monitoring.view` and the three RabbitMQ management keys.

- **Tiles** show lanes, queue depth, retry backlog, dead letters, and incoming and acknowledged rates.
- **Lanes** list each consumer with its nodes, in-flight, queued, retrying and dead messages, prefetch, rates and health. They are grouped as front doors, work, notifications, legacy and control.
- **Alerts** come from the bus library, such as backpressure and dead letters.
- **Nobody is reading these** lists queues with Bitween's prefix that no consumer reads. They are usually left behind by a deleted or renamed work group or information type, and RabbitMQ does not remove them.

The bus library retries a message whose consumer throws through the `.retry` queue, and parks it in the `.bad` queue when those retries run out. This is transport-level failure, separate from retry policies. An exchange that fails in its adapters still gets a result and does not go to `.bad`.

The same data is available from `GET /api/ops/summary`, `/api/ops/consumers`, `/api/ops/queues`, `/api/ops/retries`, `/api/ops/deadletters`, `/api/ops/alerts` and `/api/ops/unattendedqueues`.

## Data sources

- The **Data sources** page lists each connection with its state, last error and the node that holds it.
- **Test connection** runs the adapter's own checks and shows each stage. For databases it also checks every statement.
- The live panel refreshes every 3 seconds, but only when the page is served by the node running the adapter.
- A data source nobody runs keeps showing its last recorded state.

See [Data sources](data-sources.md).

## Scheduled work

- The **Scheduled jobs** and **Aggregations** pages show each subscription's last run, reliability, next run and schedule health.
- A subscription's overview lists its receive attempts and the exchanges each created.
- **Stuck** means a run was killed and left its running flag set, so later runs are skipped.
- **Not scheduled** means a trigger is missing. Saving the subscription again recreates it.

See [Scheduling](scheduling.md).

## Dashboard

The dashboard, opened from the logo, shows today's exchanges, the 7-day success rate, today's failures, waiting retries, queue alerts, a 14-day chart, the latest failures, unhealthy subscriptions and the busiest subscriptions. The browser computes it from up to 1,000 recent exchanges, so figures on a busy instance are approximate.

Two parts come from the server instead. **Failures to act on** counts failed exchanges that are the newest attempt of their chain, of any age, and links to that search. **Chains that keep failing** lists the five retry chains with the most attempts, with the success rate of retries over the last 7 days.

## Audit trail

The Audit trail page shows who changed which configuration entity, when, and the values before and after. Filter by entity type, member, entity id, date range, or everything changed in one save. See [Security](security.md#audit-trail).

## Logs

Bitween logs to the console. When `SwLogger:ElasticsearchUrl` is set and the environment is listed in `SwLogger:ElasticsearchEnvironments`, logs also go to Elasticsearch, enriched with request context.

Messages worth watching for:

- `Database migration failed on startup against {Host}:{Port}/{Database}`
- A startup warning that the Microsoft sign-in redirect URI is not the sign-in landing page
- `Auto-retry evaluation failed for xchange …`
- `Retry budget of subscription … could not be cleared after a success`
- `Bus gateway route references subscription …, which is not active; skipping.`

## Data retention

| Data | Retention |
|---|---|
| Exchange files | The bucket's rule for the prefix they were written under: 30 days under `temp30/`. On Azure only once the account's lifecycle policy is configured, see [Deployment](deployment.md#object-storage). |
| Exchanges, results, promoted properties, notifications | **Keep exchanges (days)** on the Settings page. 0, the default, keeps them for ever. |
| Archived exchanges | Kept for ever, unless a bucket rule covers the archive prefix |
| Receive attempts | `Bitween:ReceiveAttemptRetentionDays`, 30 days by default |
| Broker deduplication keys | Each data source's window, 30 days by default |
| Scheduler execution history | Managed by the scheduler library |
| Audit entries | Kept indefinitely |

The retention job runs on the **Retention schedule** setting, 04:00 daily by default. It removes exchanges that started more than **Keep exchanges (days)** ago, oldest first and in small batches. An exchange waiting for a scheduled retry is kept, and so is one a newer retry still points at, until that retry is removed too.

With **Archive before deleting** on, the default, each exchange is first written to `archive/{prefix}/{subscription id}/yyyy/MM/dd/{main value}_{exchange id}.json`. `{prefix}` is the document prefix without its `tempN/` part, and the main value is the exchange's value for the first promoted property of its information type; without one the file is named by the id alone. The file holds the exchange's details, its promoted properties, result and notifications, and the contents of its files while storage still has them. Handler and mapper properties are left out, because they can hold resolved secrets. If an exchange can't be archived, the run stops and keeps it for the next run.

The **Documents & storage** section of the Settings page shows the bucket's rules and what the settings do: how long files and exchanges are kept, when the job next runs, how many exchanges are listed without their files, and warnings such as exchanges that outlive their files, archives that come too late to hold them, aggregations that run less often than exchanges are kept, or a bucket that lets anyone read Bitween's files without credentials. Saving a change to these settings shows the same list and asks first.

Exchange rows can outlive their files. Opening such a file says it was deleted by the retention policy, and so does retrying an exchange whose input has gone. A waiting retry for one is dropped with a reason.

## Common problems

| Symptom | Likely cause |
|---|---|
| The Microsoft sign-in popup fails with `user_cancelled` | The redirect URI does not point at `/blank.html` |
| Changing appsettings or Helm values does not change a setting | The setting is already stored in the database, which wins |
| Queue health says RabbitMQ management isn't configured | One of the three management keys is missing |
| Rebex adapters are missing from the pickers | No Rebex license key is saved |
| An API gateway returns 404 at the right URL | The attached subscription is inactive |
| An API gateway returns 503 | The gateway is deactivated |
| A scheduled job never runs | Check schedule health for Stuck or Not scheduled, and that the subscription is active |
| A mapper fails parsing JSON on XML input | The subscription uses a mapper other than the rules-based mapper |
| `{{partner.KEY}}` appears literally in a request | The partner has no such property, or the adapter is a receiver or notifier, which get no partner values |
| Sync gateway calls hang | The sync wait never times out for a `Wait-Period` of 8 or more, including the default |
| Retry is refused with `ALREADY_RETRIED` | That exchange was already retried. Retry its newest attempt. |
| A data source shows no live data, or statements save as *not checked* | The request reached a node that does not run the adapter, or `Bitween__BusProvidersEnabled` is off |
| A delivery fails with *not running on this node* | Data sources are not enabled on the node processing the exchange |
| A new adapter version still shows its old properties | Adapter descriptions are cached on each node |
| A database receives the same row twice | Marking the row or saving the cursor failed after its exchange was stored |
