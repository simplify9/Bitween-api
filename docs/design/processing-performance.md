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

### 6. The payload is parsed several times

- **Status:** to discuss
- **Where:** `FilterService` and the payload readers, `DocumentSchema`, `XchangeService.RunMapper`
  (enrichment), `NativeAdapters/Mapper` (`NativeMapper`, `JsonFormat`, `XmlFormat`),
  `NativeAdapters/JsonMapper/ScribanJsonHelper`, `NativeAdapters/HttpHandler/NativeHttpHandler`
- **What happens now**, after findings 3 and 4 moved the promoted-property read to creation.
  Each line is one full parse of the input into a Newtonsoft object tree (or an `XDocument`):

  | # | Parse | When | Always? |
  |---|---|---|---|
  | 1 | `JObject.Parse` for promoted properties and routing (`JsonExchangePayloadReader`) | creation | yes |
  | 2 | `JToken.Parse` for the schema check (`DocumentSchema.Check`) | worker | only when the type has a schema |
  | 3 | `JToken.Parse`, inject `__partner__` and `__globals__`, serialize back (`RunMapper`) | worker | every mapper except the rules-based one |
  | 4 | `JToken.Parse` just to see whether the payload is JSON (`ScribanJsonHelper.LooksLikeJson`) | worker | Scriban mapper |
  | 5 | `JToken.Parse` to build the template model (`ScribanJsonHelper.RenderText`) | worker | Scriban mapper |
  | 6 | `JToken.Parse` of the rendered output to tidy it | worker | Scriban mapper |
  | 7 | `JToken.ReadFrom` with decimal handling (`JsonFormat`) | worker | rules-based mapper |
  | 8 | `JsonConvert.DeserializeObject` of the whole payload into a dictionary for a URL with `{{ }}` (`NativeHttpHandler`) | worker | HTTP handler with a templated URL |
  | 9 | `Template.Parse` of the Scriban template, of the DotLiquid URL template, and `DeserializeObject<MappingRules>` of the rules JSON | worker | every call, none cached |

  A Scriban mapper with an HTTP handler on a templated URL parses the input six times (1, 3, 4,
  5, 8 plus the output in 6) and re-parses its template every call. The rules-based mapper
  with a plain URL parses it twice (1 and 7). XML goes the same way with `XDocument`: once at
  creation and once in the mapper.

- **Why it hurts:** each parse allocates a full tree; a 1 MB document parsed six times is
  about 6 MB of allocations per exchange, and the garbage collector pauses the whole pod to
  reclaim them. Template parsing is CPU on every call for a result that never changes. None of
  this shows at 5 KB payloads and 5 per second; all of it shows at 100 KB and 50 per second,
  where it sets the CPU ceiling per node rather than the latency of one exchange.

- **Options, from safest to largest.**

  | Id | Change | Removes | Risk and compatibility |
  |---|---|---|---|
  | 6a | Cache compiled templates and rules by their text: Scriban `Template.Parse`, DotLiquid URL `Template.Parse`, `MappingRules` deserialization, in bounded in-memory caches keyed by a hash of the text | row 9, every call | none; the same text gives the same template |
  | 6b | Drop the parses that only re-check: `LooksLikeJson` parses the whole payload to answer yes or no, then `RenderText` parses it again; parse once and pass the tree. The rendered-output tidy (6) can work on the writer's tree instead of re-parsing text. | rows 4 and 6 | none |
  | 6c | Parse once in the pipeline and hand the tree along: an internal `ParsedXchangeFile` (a subclass of `XchangeFile`, which lives in the SimplyWorks package and cannot change) with a lazy, cached `JToken` or `XDocument`. The filter, the schema check, the enrichment and the built-in adapters (`JsonFormat`, `XmlFormat`, `ScribanJsonHelper`, the HTTP handler's URL model) use the cached tree when they are given one. Adapters that run out of process receive text over the wire and parse it themselves, as they must. | rows 2, 5, 7 and 8 share one parse with row 1 (for exchanges created on the same pod; otherwise one parse in the worker) | internal only; the adapter contract does not change |
  | 6d | For the Scriban mapper, give `__partner__` and `__globals__` to the template as variables instead of writing them into the payload and serializing it back. The enrichment stays as it is for mappers that run out of process, which read them out of the payload. | row 3 and its serialize for the built-in Scriban mapper | a template that iterates the payload's keys stops seeing `__partner__` among them; property access `{{ __partner__.key }}` is unchanged. Needs a note in `mapping.md`. |
  | 6e | Move the mapping engine from Newtonsoft to System.Text.Json | about half the cost of each remaining parse | the rules engine is built on `JToken` with decimal number handling and no date parsing, and the Scriban model is built from `JToken`; a rewrite with subtle number and ordering semantics to preserve. Not worth it while 6a to 6d are undone. |

- **Expected effect:** with 6a to 6d, the worst case drops from six input parses to one or
  two and no template parsing per call; allocations per exchange fall by roughly the payload
  size times four. Latency per exchange barely moves at small payloads; the per-node CPU
  ceiling and GC pauses at large payloads are what improve.

- **How it is measured.** A parse counter per exchange, like finding 4's command counter
  (count the tree builds, enforced by a test), plus the harness at 20/s with a 100 KB and a
  1 MB payload: CPU per exchange and allocated bytes per exchange from the runtime counters,
  p95 before and after.

- **Backward compatibility.** 6a, 6b, 6c: none; same input, same output, same adapter
  contract. 6d: the one behaviour change above, for the built-in Scriban mapper only.

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
