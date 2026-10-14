# Processing performance: findings and plan

Status: **working document**. Findings come from a code review on 2026-10-13 of Bitween-api
(1446 commits, head of 2026-10-13), SW-Serverless and the public SW-Bus source. Numbers are
estimates from reading code, not measurements; nothing has been load tested yet.

Each finding below is discussed one at a time. A finding's **Decision** section is empty until
it has been discussed; the plan at the end is rebuilt from the decisions.

Goal: shorter exchange duration, queues that drain fast, and a throughput ceiling set by the
partners and the database rather than by Bitween itself.

## Done, pushed and running on bitween-dev (2026-10-14)

Implemented, tested, measured, deployed by CI run 167 and checked on dev (Parallel column, readiness, sync answer times). Removed from the findings below so the file lists open work;
their full write-ups are in this file's git history (`d9a6c52b`, `11ee0a99`). A session touching
these areas should read the commits before changing them.

| Finding | What changed | Commits | Measured on the harness (`tests/load`, one node) |
|---|---|---|---|
| 1, 7: a lane ran one exchange at a time per pod; one shared publisher channel, no confirms | SW-Bus 10.1.0 on RabbitMQ.Client 7 (per-queue concurrency, channel per scope, publisher confirms on, connect in the background, drain on stop), on nuget.org. Bitween: work group **Parallel exchanges** (default = prefetch), `Bitween:BusDefaultConcurrency`, readiness on the bus status, shutdown grace 45 s host and 60 s pod, upgrade notes on partner concurrency and ordering. | SW-Bus `feat/rabbitmq-client-7`; Bitween `259c98c4`, `f19f7123` | a lane with a 500 ms partner: 2/s to the offered 10/s per node, sync p95 557 ms; ceiling run above 10/s still owed |
| 2: sync calls slept 1 s before the first result check | The worker signals the waiting request: in-process bell, cross-pod broadcast to the node named in the message, answer built from the signal; the database poll is a backstop at 2 s then every 5 s. Running exchanges inside the request was rejected: the lane is the throttle that protects Bitween and partners. | `6535a5c3`, `25ec5f50` | sync p50 1011 ms to 115 ms with a 100 ms partner; 8.5 ms with an instant one; database commands per sync exchange 4.03 to 3.03 |
| 3: five object storage calls per sync exchange, all waited for | Writes overlap the work before their save, nothing commits before its upload returns; the created message carries inputs under `Bitween:MessagePayloadMaxBytes` (256 KB) and the worker uses the copy only when it matches the row's hash; the result signal carries the response. Payloads in the database were rejected; storage stays the system of record and every file is still written. | `183a1a6d`, `233ed43e`, `25ec5f50` | storage calls per sync exchange 4 (2 PUT, 2 GET) to 2 (2 PUT, 0 GET) |
| 4: about 14 database round trips per sync exchange | Reads folded or served from the cache (gateway, keys, partners, retry policy, bus documents); outbox rows inserted in the exchange's batch and deleted in batches off the hot path; result-lane message only when something notifies; auto-pause counted on result rows; promoted properties stored at creation; optimistic idempotency insert; `XchangeDeliveries` dropped. Tables kept as they are, no partitioning. Rule recorded: processing an exchange inserts, counters live on the failure path or in batches. | `972412b2`, `aafddd4a`, `2c642588`, `46e125b3`, `bd6908eb` | database commands per sync exchange 13 to 4 (3 with finding 2), enforced by `ExchangeCommandCountTests` |

The cost of one sync gateway call, native handler, success, before and after:

| | Before | After |
|---|---|---|
| Database commands | 13 | 3 |
| Object storage calls | 4 (2 PUT, 2 GET) | 2 (2 PUT) |
| Broker messages | 2 published, 2 consumed | 1 published, 1 consumed, plus a result signal when the request waits on another pod |
| Sync answer with a 100 ms partner, p50 | about 1.0 s | 115 ms |
| Exchanges per second per lane per node, 500 ms partner | 2 | the lane's parallelism divided by the partner's time, 24 at 12 |

## Findings

### 5. Classic adapters spawn a process per call

- **Status:** to discuss
- **Where:** SW-Serverless `ServerlessService.StartAsync`; `ClassicAdapterRuntime`
- **What happens:** A classic .NET adapter is started as a new `dotnet` process per invocation.
  A Python or Node adapter starts an exclusive gRPC instance per session. A subscription with a
  classic mapper and a classic handler does this twice per exchange.
- **Why it hurts:** 200 to 500 ms of process start and JIT before the adapter does anything.
- **Candidate fix:** Make resident pooled adapters the default for connectors; warm pools at pod
  start; size pools per work group (default `PoolSize` is 4, max 64, `MaxInFlight` 16 per
  instance). Treat classic as a legacy path.
- **Expected effect:** hundreds of milliseconds per adapter call removed for custom adapters.
- **Decision:** (pending)

### 6. The payload is parsed three to four times

- **Status:** to discuss
- **Where:** `DocumentSchema.Enforce`, `FilterService.Filter` (JSON and XML readers),
  `XchangeService.RunMapper` enrichment (`JToken.Parse`), the mapper itself;
  `NativeHttpHandler` re-parses the URL template on every call
- **What happens:** The same document is parsed into a full Newtonsoft object tree by each step.
- **Why it hurts:** CPU and garbage collection at hundreds of exchanges per second. A 1 MB
  payload is 4 MB of allocations per exchange.
- **Candidate fix:** Parse once and pass the parsed document along; move the hot path to
  System.Text.Json; cache parsed URL templates.
- **Expected effect:** lower CPU per exchange and fewer GC pauses; matters above 100 per second.
- **Decision:** (pending)

### 8. Scaling is on the wrong signal and roles are not split

- **Status:** to discuss
- **Where:** `charts/default/templates/hpa.yaml`, `SW.Bitween.Web/Startup.cs`
- **What happens:** One process runs UI, API, gateways, consumers, Quartz and resident adapters.
  The HPA scales on CPU at 70 percent. There is no setting to run a pod without consumers or
  without the HTTP surface.
- **Why it hurts:** The workload is I/O bound; a deep queue does not raise CPU, so no scale-out.
  Gateway bursts and workers compete for the same pod.
- **Candidate fix:** A `Bitween:Roles` setting (gateway, worker, all); KEDA on per-lane queue
  length for workers.
- **Expected effect:** backlog causes more workers; partner bursts do not slow processing.
- **Decision:** (pending)

### 9. Tables grow forever by default and the search cannot use an index for status

- **Status:** to discuss
- **Where:** `BitweenOptions.ExchangeRetentionDays` (0), `Resources/Xchanges/Search.cs`,
  indexes in `BitweenDbContext.OnModelCreating`
- **What happens:** Exchanges, results, promoted properties and notifications are kept for ever
  unless retention is set. The list joins seven tables; the status filter is "result row exists
  and succeeded", which no index serves. `Xchanges` has single-column indexes only.
- **Why it hurts:** Dedupe, search and dashboard slow down every month; index maintenance costs
  more on every insert.
- **Candidate fix:** Non-zero default retention; composite indexes (subscription + started
  date, document + hash + started date, result success + finished date). Partitioning and
  folding the 1:1 tables into `Xchanges` were ruled out with finding 4 (2026-10-14): the tables
  stay as they are. Finding 4 already added `subscription_id` and an index on
  (subscription_id, finished_on) to `XchangeResults` for auto-pause.
- **Expected effect:** flat query times over time with retention on; the status and date
  filters served by indexes.
- **Decision:** (pending)

### 10. The load harness is not in CI

- **Status:** to discuss; most of the original finding was done with finding 4
- **Where:** `tests/load` (k6, docker-compose with a stub partner and SeaweedFS, `run.sh`,
  `README.md`), `BitweenTelemetry`, `MetricsRecorder`
- **What happens:** Finding 4's phase 0 added the per-exchange database command counter
  (`ExchangeStats.DbCommands`, with `ExchangeCommandCountTests` guarding it), timings per stage
  to OpenTelemetry, and the k6 harness, which measured findings 1 to 4. The harness runs by hand
  only. It builds the image from a copy of the tree without the local `NuGet.config`, and it
  needs disk on the Docker host.
- **Why it hurts:** A regression in commands, storage calls or sync latency is only found when
  someone runs it.
- **Candidate fix:** A workflow that runs the harness on the branch (nightly, or on demand)
  against a throughput and latency budget taken from the numbers in the Done table, and fails
  on a regression; the ceiling run above 10/s as its first job.
- **Expected effect:** the numbers in this file stay true without anyone remembering to check.
- **Decision:** (pending)

## Plan

Rebuilt from the decisions above once the findings have been discussed.

| Order | Finding | Change | Schema | Acceptance on the harness |
|---|---|---|---|---|
| | | | | |

Findings 5, 6, 8, 9 and 10 are added here as they are decided. Before them: push the branch, release, and verify the done findings on staging.

## Follow-ups from the implemented findings

| From | Follow-up | Status |
|---|---|---|
| 3.1 | A failed output write is now found after the handler has delivered, so the exchange fails and its retry policy may deliver again. Proposed instead: when the handler succeeded and only the output write failed, save the result as successful with no output file (`OutputSize` 0) and a warning on the result, since the delivery is the business fact and the rule "a saved row's files exist" still holds. | to decide |
| 3.2 | Bulk retry, unpause and aggregation start their input writes in parallel, up to 500 at once for a bulk retry. Bound them with a small limit (for example 32 in flight) so a bulk retry cannot flood storage or the pod's sockets. | to decide |
| 1 | Ceiling run above 10/s on the harness (expected about 24/s per node at 12 with a 500 ms partner), once Colima has disk. | owed |
| 4 | The k6 harness into CI with a throughput and latency budget. | finding 10 |
| 1 | The response and input writes still sit on the sync critical path (about two storage round trips on S3-class storage). Nothing cheaper than finding 3's rejected 3c removes them; accepted. | closed |

## Open questions

- What payload sizes do the live deployments see (p50, p95, p99 of `input_size`, `output_size`,
  `response_size`)? Decides whether the 256 KB cap on `Bitween:MessagePayloadMaxBytes` is right.
- Which deployments run sync gateways, and what response time do their partners expect?
- How many subscriptions still run classic adapters?
