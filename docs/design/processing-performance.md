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

- **Status:** decided 2026-10-14 (6a, 6b, 6c with safeguards; 6d deferred). Phases 6.0 to 6.4
  implemented and committed locally on `releases/r10.0-develop` 2026-10-14, not pushed; 6.5
  measured in part (see "Implemented" below). Remove once shipped.
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
  | 6a | Cache compiled templates and rules by their text: Scriban `Template.Parse` and `MappingRules` deserialization, in bounded in-memory caches keyed by a hash of the text. Not the DotLiquid URL template (see validation). | row 9, every call | none; the same text gives the same template |
  | 6b | Drop the parse that only re-checks: `LooksLikeJson` parses the whole payload to answer yes or no, then `RenderText` parses it again; parse once and pass the tree. Row 6 stays: the template produces text. | row 4 | none |
  | 6c | Parse once in the pipeline and hand the tree along: an internal `ParsedXchangeFile` (a subclass of `XchangeFile`, which lives in the SimplyWorks package and cannot change) with a lazy, cached `JToken` or `XDocument`. The filter, the schema check, the enrichment and the built-in adapters (`JsonFormat`, `XmlFormat`, `ScribanJsonHelper`, the HTTP handler's URL model) use the cached tree when they are given one. Adapters that run out of process receive text over the wire and parse it themselves, as they must. | rows 2, 5, 7 and 8 share one parse with row 1 (for exchanges created on the same pod; otherwise one parse in the worker) | internal only; the adapter contract does not change |
  | 6d | For the Scriban mapper, give `__partner__` and `__globals__` to the template as variables instead of writing them into the payload and serializing it back. The enrichment stays as it is for mappers that run out of process, which read them out of the payload. | row 3 and its serialize for the built-in Scriban mapper | a template that iterates the payload's keys stops seeing `__partner__` among them; property access `{{ __partner__.key }}` is unchanged. Needs a note in `mapping.md`. |
  | 6e | Move the mapping engine from Newtonsoft to System.Text.Json | about half the cost of each remaining parse | the rules engine is built on `JToken` with decimal number handling and no date parsing, and the Scriban model is built from `JToken`; a rewrite with subtle number and ordering semantics to preserve. Not worth it while 6a to 6d are undone. |

- **Expected effect:** with 6a, 6b and 6c, the worst case drops from six input parses to two
  (one per parse profile) and no template parsing per call; allocations per exchange fall by
  roughly the payload size times three or four. Latency per exchange barely moves at small payloads; the per-node CPU
  ceiling and GC pauses at large payloads are what improve.

- **How it is measured.** A parse counter per exchange, like finding 4's command counter
  (count the tree builds, enforced by a test), plus the harness at 20/s with a 100 KB and a
  1 MB payload: CPU per exchange and allocated bytes per exchange from the runtime counters,
  p95 before and after.

- **Validation (2026-10-14), read against the code before deciding.** The naive version of 6c,
  one parse shared by everyone, **would change stored data**, so it must not be built that way:

  | Consumer | Parse settings today | What a shared default tree would change |
  |---|---|---|
  | Promoted properties, match expressions (`JsonExchangePayloadReader`), schema check, Scriban model, HTTP URL model | Newtonsoft defaults: floats become `double`, ISO date strings become `DateTime`, and a promoted value is then written back as text, so `1.10` is stored as `1.1` and `2026-10-14T10:00:00Z` as `10/14/2026 10:00:00` | nothing, if the shared tree keeps these defaults |
  | Rules-based mapper (`JsonFormat`) | `FloatParseHandling.Decimal`, `DateParseHandling.None`, and a trailing-content check, so money stays exact and dates stay text | with the default tree, prices would round and dates would reformat in every mapped document |

  So the two profiles cannot share one tree. The safe design keeps **one cached tree per parse
  profile**: "defaults" for the first row, "decimal, no dates" for the mapper. Every consumer
  gets a tree parsed exactly as it parses today, and the count drops from six to two, not to
  one. The other hazards found:

  - The enrichment (row 3) mutates the tree it parsed. A cached tree is shared, so the
    enrichment must deep-clone before it injects, or it would leak `__partner__` into the
    promoted-property read of the same exchange on the same pod.
  - `MappingRules` is a plain mutable object (public setters, lists). Nothing in `DocumentMapper`
    writes to it, so caching the deserialized rules is safe today; the cache must hand out the
    same instance only if that stays true, so a test asserts the rules are not mutated by a run.
  - Scriban renders with a fresh `TemplateContext` per call and the parsed `Template` holds
    only the syntax tree, so caching it is safe. DotLiquid's `Template` keeps per-instance
    state and its thread safety under concurrent renders is not documented; the URL template
    is a few characters, so **do not cache it**. 6a is Scriban templates and mapping rules only.
  - Row 6 cannot be dropped: the template produces text, so its output has to be parsed to be
    tidied. 6b removes row 4 only.
  - XML goes the same way: `XmlExchangePayloadReader` decodes entities and strips invalid
    characters before `XDocument.Parse`; `XmlFormat` loads the raw text. Two profiles again.

  What makes it safe to ship:

  1. **Golden tests captured before any change.** A corpus of inputs (numbers `1.10`, `1e5`,
     large integers, `-0`; dates ISO, with offsets, non-ISO strings; unicode and escapes; nested
     and root arrays; empty objects; duplicate keys; trailing whitespace and content; a BOM;
     XML with entities, invalid characters and namespaces) run through each consumer
     (promoted values, match results, schema errors, rules-mapper output, Scriban output,
     enriched payload text, HTTP URL rendering) on the current commit, outputs stored as
     fixtures, and asserted byte for byte after the change.
  2. **A parse counter per exchange**, like finding 4's command counter, with a test that
     fails if the count rises.
  3. **A kill switch**, `Bitween:SharedPayloadParse` (default on): off restores the per-step
     parsing exactly, so a deployment that sees a difference reverts with a setting, not a
     rollback. Same for `Bitween:CacheCompiledTemplates`.
  4. **A concurrency test** rendering one cached Scriban template from many threads with
     different models.
  5. The existing suites: `JsonPropertyReaderTests`, `DocumentSchemaTests`, the four Scriban
     helper suites, `NativeJsonMapperTests`, the `NativeMapper` folder, `MapperContextOptOutTests`,
     `HttpHandlerBehaviourTests`, plus HTTP and integration.

  **6d is the only option with a visible behaviour change** (a template iterating payload keys
  stops seeing `__partner__`). Recommended: defer it. It saves one parse and one serialize per
  Scriban exchange, and it is the one thing here that cannot be proven identical by a golden
  test.

- **Backward compatibility.** 6a (Scriban and rules only), 6b (row 4 only), 6c (per-profile
  trees, immutable, cloned before enrichment): none, and proven by the golden corpus and the
  kill switch. 6d: deferred.

- **Decision (2026-10-14):** 6a, 6b and 6c with the five safeguards. 6d deferred, 6e not taken.

- **Implementation plan for the separate session.** Bitween-api only. No schema change, no
  adapter contract change, no SW-Bus change. Phase 6.0 must be committed before any
  behaviour changes, because its fixtures are the proof.

  | Phase | Steps | Where | Done when |
  |---|---|---|---|
  | 6.0 | **Golden corpus, captured on the current code.** A fixtures folder of inputs: JSON with `1.10`, `1e5`, `12345678901234567890`, `-0`, `0.1`; dates `2026-10-14T10:00:00Z`, `2026-10-14T10:00:00+03:00`, `14/10/2026`, `2026-10-14`; unicode, `\u` escapes, embedded quotes and newlines; nested objects, root arrays, empty objects, duplicate keys, trailing whitespace, trailing content, a BOM; XML with entities, invalid characters, namespaces and CDATA. For each input run every consumer and store its output: promoted values and match results (`FilterService` with a document that promotes several paths and a match expression), schema errors (`DocumentSchema`), rules-mapper output (`NativeMapper` with a rule set covering numbers, dates, lists), Scriban output (`ScribanJsonHelper` with a template covering numbers, dates, loops, `__partner__`), the enriched payload text (`RunMapper`'s injection, made testable), the HTTP URL rendered from a templated URL (`NativeHttpHandler`). A test asserts byte-for-byte equality against the stored files. | `SW.Bitween.UnitTests/PayloadParsing/Golden/` | fixtures committed; the test is green on the unchanged code |
  | 6.1 | **Parse counter.** Count tree builds per exchange (`JObject.Parse`, `JToken.Parse`, `JToken.ReadFrom`, `XDocument.Parse` and `Load` on the hot path) the way `DbCommands` is counted: a scoped counter, added to `MetricsRecorder` and `ExchangeStats` as `PayloadParses`, exported to OpenTelemetry; a test like `ExchangeCommandCountTests` records the baseline per path (rules mapper plus plain URL; Scriban mapper plus templated URL; with and without a schema). | `BitweenTelemetry`, `MetricsRecorder`, `ExchangeStats` (one nullable column, additive), `ExchangeParseCountTests` | baselines recorded: 2 and 6 |
  | 6.2 | **6a: cache compiled Scriban templates and mapping rules.** Bounded caches (for example 512 entries, least recently used) keyed by a hash of the text, in `ScribanJsonHelper` and `NativeMapper`. Not the DotLiquid URL template. A test asserts a mapping run does not mutate the cached `MappingRules`; a test renders one cached template from 32 threads with different models and checks every output. `Bitween:CacheCompiledTemplates` (default on) bypasses the caches when off. | `ScribanJsonHelper`, `NativeMapper`, `BitweenOptions` | golden test green; the template parse no longer appears per call in a profile |
  | 6.3 | **6b: one parse in the Scriban helper.** `LooksLikeJson` and `RenderText` share one parse; the rendered-output parse stays. | `ScribanJsonHelper` | golden green; Scriban path count drops by one |
  | 6.4 | **6c: one cached tree per parse profile.** An internal `ParsedPayload` held by the pipeline for the input of the exchange being processed, with two lazy slots: `Defaults` (Newtonsoft defaults, as `JObject.Parse` does today) and `Exact` (`FloatParseHandling.Decimal`, `DateParseHandling.None`, trailing-content check, as `JsonFormat` does today), plus the XML pair (`XmlExchangePayloadReader`'s cleaned document and `XmlFormat`'s raw load). The cached trees are immutable by convention: the enrichment deep-clones `Defaults` before injecting. Consumers take the tree when offered and parse for themselves when not: `JsonExchangePayloadReader` and `XmlExchangePayloadReader`, `DocumentSchema.Check`, `RunMapper`'s enrichment, `JsonFormat.Read` and `XmlFormat` through `NativeMapper`, `ScribanJsonHelper.RenderText`, and the HTTP handler's URL model (build the dictionary from the `Defaults` tree with the same `DictionaryConverter` semantics, proven by the golden URL fixtures). Built-in adapters receive it through a `ParsedXchangeFile : XchangeFile` the native runtime passes; adapters out of process still get text. `Bitween:SharedPayloadParse` (default on) returns every consumer to its own parse when off. | `XchangeService`, `Services/ExchangePayloadReaders`, `DocumentSchema`, `NativeAdapterRuntime`, `NativeMapper`, `JsonFormat`, `XmlFormat`, `ScribanJsonHelper`, `NativeHttpHandler`, `BitweenOptions` | golden green with the switch on and off; parse counts 1 (rules path, no schema) and 2 (Scriban path); the kill switch restores the baseline counts |
  | 6.5 | **Measure and document.** The harness at 20/s with a 100 KB and a 1 MB payload, rules mapper and Scriban mapper: CPU per exchange and allocated bytes per exchange from the runtime counters, p95, before (6.1 baseline commit) and after. Record the numbers here. `configuration.md` gains the two settings; `mapping.md` says nothing changes. | `tests/load`, docs | numbers recorded |

  Out of scope: 6d (partner and global values as Scriban variables), 6e (System.Text.Json),
  the DotLiquid URL template cache, anything that changes a stored value or an adapter's input.

- **Implemented (2026-10-14), committed locally, not pushed.** `d3cff002` (6.0), `88f37e1b` (6.1),
  `93b835c9` (6.2), `f10e580b` (6.3), `d3953379` (6.4). Harness and docs for 6.5 are in the working
  tree, uncommitted until the HTTP and integration suites have run on the final state.

  Golden corpus: 43 inputs (JSON numbers, dates, unicode and escapes, shapes, duplicate keys,
  trailing whitespace and content, a BOM, comments, single quotes, NaN, depth 70, empty and
  invalid; XML with entities, invalid characters, namespaces, CDATA, HTML-encoded, a DTD, a BOM,
  trailing content) through twelve consumers, in `SW.Bitween.UnitTests/PayloadParsing/Golden`.
  Green with both switches on and off, in all four combinations; a test holds that no consumer
  changes a shared tree.

  Payload parses per sync gateway call (`ExchangeParseCountTests`, creation plus processing):

  | Path | Before | After, switches on | After, `SharedPayloadParse=false` |
  |---|---|---|---|
  | Rules mapper, plain URL | 2 | 2 | 2 |
  | Rules mapper, plain URL, schema | 4 | 3 | 4 |
  | Scriban mapper, templated URL, enriched object | 5 | 5 | 5 |
  | Scriban mapper, templated URL, enriched object, schema | 7 | 5 | 7 |
  | Scriban mapper on a root array (not enriched) | 4 | 3 | 4 |
  | Scriban mapper on a root array, schema | 6 | 3 | 6 |
  | FTP file name or SMTP fill (`FillIfJson`) | 2 | 1 | 1 (6b has no switch) |

  Measured on the harness, 20/s for 60 s after a 15 s warm-up, one node on Colima (4 CPUs), per
  exchange from the runtime's own counters (`METRICS=1`). Only the clean pairs are given; the
  others were spoiled by the machine and are listed as such.

  | Payload, mapper | CPU before | CPU after | Allocated before | Allocated after | p95 before | p95 after |
  |---|---|---|---|---|---|---|
  | 1 MB, Scriban | 73.8 ms | 66.0 ms | 64.3 MB | 49.0 MB | 88 ms | 74 ms |
  | 100 KB, Scriban | 23.4 ms | 45.0 ms (stalled run) | 7.51 MB | 7.42 MB | 21 ms | 4987 ms (stalled run) |
  | 1 MB, rules | 77.9 ms | not measured | 66.4 MB | not measured | 76 ms | not measured |
  | 100 KB, rules | not measured | 26.4 ms | not measured | 7.02 MB | not measured | 25 ms |

  The 1 MB Scriban pair is the one result to rely on: one parse of the payload fewer is about
  15 MB less allocated per exchange and 11% less CPU. The 100 KB Scriban "after" run stalled for
  reasons outside Bitween (a Kubernetes control plane of another project shares the VM); its CPU
  and latency say nothing about the change. The rules runs that are missing collapsed in k6 (each
  virtual user built the 1 MB order itself, since fixed with a `SharedArray`) or on a cold first
  run. A second round was abandoned when the machine went to sleep mid-run. Owed: the full round
  again on an awake, otherwise idle machine.

- **What turned out different from the plan.**
  - The rules path cannot go from 2 parses to 1: its two parses are the two profiles (defaults at
    creation for promoted properties, exact in the mapper), and nothing else on that path reads
    either. It drops only when a schema is checked (4 to 3).
  - The Scriban path does not go from 6 to 2. Row 4 (`LooksLikeJson`) is not on the mapper's path
    at all, it is the FTP and SMTP fill, so the baseline was 5 (7 with a schema). The Scriban model
    cannot use the enrichment's tree: the enriched text is parsed again today, so a partner or
    global value that looks like a date reaches the template as a date (golden fixture
    `json-dates.json`, `partnerSince`), which a copy of the tree would not do. The mapper's output
    and the URL model read other text. So the enriched Scriban path stays at 5 and saves only the
    schema parse; a payload that is not enriched saves the model parse.
  - The HTTP handler's URL model keeps its own parse: its dictionary drops a later null where a
    tree keeps it (fixture `json-duplicate-keys.json`, `price=1` in the URL), so building it from
    the shared tree would change URLs.
  - Sharing across creation and processing (one parse for an exchange created and run on the same
    pod) was not built: it needs a process-wide store of trees for queued exchanges, which a
    backlog of large payloads would turn into a memory problem. To decide if wanted.
  - `Xchanges/Update` (the older ApiCall entry) checks the schema on one `request.ToString()` and
    creates the exchange from another, so it does not share; left alone.



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
| 1 | 6, phases 6.0 to 6.5 | Golden corpus, parse counter, cached Scriban templates and rules, one Scriban parse, one cached tree per parse profile with a kill switch | one nullable column on `ExchangeStats` | golden fixtures byte-identical; parses per exchange 6 to 2 on the Scriban path and 2 to 1 on the rules path; CPU and allocations per exchange at 100 KB and 1 MB recorded |

Findings 5, 8, 9 and 10 are added here as they are decided. Before them: push the branch, release, and verify the done findings on staging.

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
