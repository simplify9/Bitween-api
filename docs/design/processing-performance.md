# Processing performance: findings and plan

Status: **working document**. Findings come from a code review on 2026-10-13 of Bitween-api
(1446 commits, head of 2026-10-13), SW-Serverless and the public SW-Bus source. Numbers are
estimates from reading code, not measurements; nothing has been load tested yet.

Each finding below is discussed one at a time. A finding's **Decision** section is empty until
it has been discussed; the plan at the end is rebuilt from the decisions.

Goal: shorter exchange duration, queues that drain fast, and a throughput ceiling set by the
partners and the database rather than by Bitween itself.

## What one exchange costs today

A synchronous gateway call, native mapper, native HTTP handler, success, no notifications.
Code: `SW.Bitween.Api/Controllers/GatewayController.cs`, `XchangeService.Process`.

| Stage | DB round trips | Object storage ops | Broker hops |
|---|---|---|---|
| Create (request thread) | 4 to 5 | 1 PUT | 1 publish |
| Process (consumer) | 6 | 1 GET, 2 PUT | 1 publish |
| Result lane (notifications) | 2 | 0 | 1 consume |
| Sync wait and answer | 2 or more | 1 GET | 0 |
| Total | about 14 | 5 | 3 |

| Component | Estimate |
|---|---|
| Storage, 5 ops | 150 to 400 ms |
| Database, 14 trips | 15 to 45 ms |
| Broker hops | 10 to 40 ms |
| Sync poll floor | 1000 ms |
| Sync call end to end, partner answering in 100 ms | 1.3 to 1.6 s |
| Async end to end | 0.4 to 0.7 s |

## Findings

### 1. A lane is single-threaded per replica

- **Status:** decided 2026-10-14 (option C). Part 1 done 2026-10-14: SW-Bus on RabbitMQ.Client
  7.2.2, released as **10.1.0** (a minor, not the 11.0 the plan named; 10.1.0 and 10.1.1 are on
  nuget.org, same code). Part 2 done 2026-10-14 in Bitween, committed locally on
  `releases/r10.0-develop`, not pushed: `259c98c4` (bump to 10.1.0, step 2.2,
  `Bitween:BusPublisherConfirms`) and `f19f7123` (steps 2.3 to 2.5: work group `Concurrency`
  shown as **Parallel exchanges**, `Bitween:BusDefaultConcurrency` and the chart's
  `busDefaultConcurrency`, Queue health column, `/health/ready` on `IBusConnectionStatus`, host
  `ShutdownTimeout` 45 s and chart `terminationGracePeriodSeconds` 60, docs and upgrade notes).
  Tests (2.6) on Colima: unit 893 passed, TransportTests 43, HttpTests 409 (with a new parallel
  exchanges test), IntegrationTests 812 of 832 with 2 failures that pass on a rerun and at the
  parent commit (a memory-limit test under load, and a serverless temp folder shared with another
  session's run). The measurement (2.7) is below. Remove both findings and their plan rows once
  shipped.

  Notes from Part 2: `Bitween:BusDefaultConcurrency` also applies to Bitween's other consumers
  (bus-enabled information types, retry and auto-pause alerts), which are safe in parallel, one
  scope per message; the upgrade notes say so. A ceiling run above 10/s (expected about 24/s per
  node at 12 with a 500 ms partner) is still owed once Colima's disk has room.

  Part 1 deviations from the plan, all accepted: two connections kept (publisher and consumer
  separate, so broker flow control on a busy publisher cannot hold back acks); no SW-Bus
  re-attach existed to remove, the client's recovery already owned reconnects; confirmation
  tracking adds an `x-dotnet-pub-seq-no` header to published messages (stripped from the
  bad-queue copy); channel close is bounded so a hung handler cannot block a pod's stop.
  Open point for the user: 10.1 as a minor flips three defaults for every product on SW-Bus
  (confirms on, connect in the background instead of at registration, drain on stop); the
  README's "Upgrading to 10.1" section documents them, but a major would say it louder.
- **Where:** SW-Bus `ConsumersService.AttachConsumer` and `IServiceCollectionExtensions.AddConsumerService`; RabbitMQ.Client 6.8.1
- **What happens:** The connection is created with `DispatchConsumersAsync = true` and no
  `ConsumerDispatchConcurrency`, whose default is 1 per channel. The `Received` handler awaits
  `ConsumerRunner.Run` inline. Each lane has its own channel, so each lane processes one message
  at a time per pod. Prefetch (12 by default) only pre-buffers messages on the client.
- **Why it hurts:** Throughput per lane per pod is one divided by exchange duration. One slow
  partner (up to `Bitween:NativeAdapterTimeout`, 900 s) stalls the lane on that pod. This is the
  "every subscription slows down while one partner is slow" symptom in `docs/operations.md`.
  `docs/exchange-pipeline.md` says prefetch is the concurrency; under this client it is not.
- **How to confirm:** Queue health, in-flight per node per lane never exceeds 1.
- **Candidate fix:** Set dispatch concurrency to the lane's prefetch (or run each handler on the
  pool behind a `SemaphoreSlim(prefetch)` in `ConsumerRunner`). Consider moving to
  RabbitMQ.Client 7 at the same time (async API, per-channel concurrency, confirms).
- **Expected effect:** roughly prefetch times more throughput per lane per pod.

- **What happens today, exactly.** SW-Bus opens one RabbitMQ connection per pod and one
  channel per queue. Each queue's consumer is an `AsyncEventingBasicConsumer` whose `Received`
  handler awaits `ConsumerRunner.Run` inline: create a DI scope, resolve `XchangeService`, call
  `Process`, then `BasicAck`. The RabbitMQ 6.x client runs the handlers of one channel on a
  work pool with as many loops as `ConnectionFactory.ConsumerDispatchConcurrency`, which SW-Bus
  never sets, so one loop. Prefetch 12 means the broker sends up to 12 unacknowledged messages
  to the pod, where they wait in the client's buffer for the one loop. Across lanes it is
  parallel: five work groups are five channels and five loops. Within a lane it is one at a time
  per pod.

- **Two ways to fix it, both in SW-Bus.**

  | | A. Set the client's dispatch concurrency | B. Dispatch each message to the thread pool behind a per-queue limiter |
  |---|---|---|
  | Change | `factory.ConsumerDispatchConcurrency = N` in `AddConsumerService` | In `AttachConsumer`, the handler starts `Run` as a task and returns; a `SemaphoreSlim` per consumer, sized by a new `ConsumerOptions.Concurrency`, bounds it |
  | Granularity | one number for the whole connection, so every lane gets the same loop count; prefetch still caps in-flight per lane, so the effective concurrency is min(N, prefetch) | per queue, so a work group can say 1, 4 or 12 |
  | Ack from several threads on one channel | yes, which is how the client's own multi-loop mode works | same |
  | Shutdown | the client drains its loops on close | SW-Bus must track in-flight tasks and wait for them, with a deadline, after `BasicCancel`; today an inline handler is simply cut off when the connection closes, so this is an improvement either way |
  | Other products on SW-Bus | behaviour changes for them unless the default stays 1 | nothing changes unless they set the option |

  Recommendation: B, with the option defaulting to 1 in SW-Bus so nothing changes for other
  products, and Bitween setting it per lane from the work group. C, moving SW-Bus to
  RabbitMQ.Client 7 (async API, per-channel concurrency, thread-safe channels, confirms), is the
  right end state and is where finding 7 goes; it is a larger library change and should not gate
  this one.

- **Option C, SW-Bus on RabbitMQ.Client 7: the impact.** This fixes finding 1 and finding 7
  together, so it is assessed here.

  What changes in the client library: `IModel` becomes `IChannel`; every operation becomes
  async (`CreateConnectionAsync`, `CreateChannelAsync`, `BasicPublishAsync`, `BasicAckAsync`,
  `QueueDeclareAsync`, `BasicConsumeAsync`); the consumer event is `ReceivedAsync`; dispatch
  concurrency is set **per channel** when it is created, which gives the per-lane knob natively;
  publisher confirms are a channel option and `BasicPublishAsync` then completes when the
  broker has the message; channels are safe to use from several threads.

  | Where | What has to change | Size |
  |---|---|---|
  | SW-Bus, RabbitMQ half | `ConsumersService` (connection, channels, declarations, consume), `ConsumerRunner` (ack, reject, publish to bad), `BasicPublisher`, `Broadcaster`, the node listener. Two things move out of DI registration, where they run synchronously today: the exchange declarations in `AddBus` and the connection plus shared channel in `AddBusPublish`. They become a hosted start-up step, and the publisher gets a channel per scope or a small pool instead of one shared channel. | the whole RabbitMQ plumbing, about six files; the public interfaces (`IPublish`, `IBroadcast`, `IConsume`, `ConsumerOptions`) already return `Task` and stay as they are |
  | SW-Bus, version | a new major (11.0): behaviour changes even if the API does not | release to nuget.org before Bitween |
  | SW-Bus, other products | nothing to change unless they use `RabbitMQ.Client` directly; with concurrency defaulting to 1 their behaviour is unchanged; publishes now wait for a broker confirm, about a millisecond on a local broker | |
  | Bitween-api, direct client use | `RabbitMqLeaderElection`, `Ops/DeadLetterQueues`, `Ops/RequeueDeadLetters`, `Web/DependencyHealthChecks`, plus 7 integration test files and one HTTP test: `IModel` to `IChannel` and async calls. Must move in the same release, because one process loads one version of the client, and it arrives transitively through SW-Bus. | mechanical, 4 production files |
  | Bitween-api, everything else | no change: it talks to SW-Bus through its interfaces | |
  | The external RabbitMQ adapter | already on client 7 in its own process; no change | |
  | Queues on the broker | none: queue names and arguments stay exactly as they are. Changing an argument on an existing queue (quorum, for example) makes the broker refuse the declaration, so quorum queues are a separate, later step with a migration of their own. | |

  Behaviour that changes, beyond parallelism:

  - A publish now waits for the broker's confirm, so an outbox row deleted after publish is
    deleted because the broker has the message. Finding 4's 4a gets the guarantee it was
    designed for.
  - Client 7 turns automatic connection recovery on by default and recovers topology itself.
    SW-Bus has its own reconnect handling (`ConnectionShutdown`, `RefreshConsumers`). The two
    must not both re-attach consumers after an outage, or a lane consumes twice on one pod.
    This is the one place that needs a deliberate design and a test with a broker restart
    (`SW.Bitween.TransportTests` and the Testcontainers suite already cover the bus path).
  - The connection is no longer opened inside DI registration, so a pod whose broker is down at
    boot starts, reports not ready, and connects when the broker returns, instead of failing
    to start.

  Effort: three to five days in SW-Bus including tests, one to two in Bitween, plus the
  SW-Bus release. Rollout: one Bitween release that bumps the package; no schema, no topology
  change, no configuration change.

  Compared with B: B is a few lines and ships this week; C is a week and ships findings 1 and 7
  together, with thread-safe publishing and confirms that B cannot give. If C is chosen, B is
  not needed. If finding 1 is wanted sooner, B first and C later, where B's per-queue option
  simply maps onto C's per-channel concurrency.

- **In Bitween.** A work group gains a **Parallel exchanges** setting beside prefetch, stored in
  its existing options JSON, shown on the Work groups page and on Queue health, and passed to
  SW-Bus through `ConsumerOptions` in `XchangeService.GetMessageTypeNamesWithOptions`. The
  default lane takes `Bitween:BusDefaultConcurrency`.

- **What changes for a deployment once lanes run in parallel.** These are the real decisions.

  | Effect | Today | After | Handling |
  |---|---|---|---|
  | Calls to a partner from one pod | one at a time per lane | up to the lane's parallelism at once | A partner that rate-limits answers 429 and the circuit breaker opens, which is handled, but it is a visible change for the partner. Lanes for sensitive partners can be set to 1. |
  | Order of exchanges | effectively first-in first-out per lane on a single pod (never guaranteed across pods) | not ordered within a lane | A flow that sends "create" then "update" for the same order 100 ms apart can now run them out of order on one pod. Lanes that need order stay at 1. The docs never promised order, and multi-pod deployments never had it. |
  | A hung partner | stops the lane on that pod for up to the timeout | holds one slot of the lane | `Bitween:NativeAdapterTimeout` (900 s) and `ServerlessCommandTimeout` (300 s) become slot hogs instead of lane stoppers. Lower defaults are a separate change, with the settings still there to raise them. |
  | Database connections | one per lane per pod in use | up to parallelism per lane per pod | Npgsql pools 100 per pod by default; five lanes at 12 is 60. Check `Maximum Pool Size` on deployments with many lanes. |
  | Memory | one payload per lane per pod in flight | up to parallelism payloads | Request bodies are capped at 50 MB; the chart sets no memory limit. Note in the release notes. |
  | Classic adapters | one process at a time per lane | up to parallelism processes per lane | Finding 5 makes resident the default; until then a lane with classic adapters should keep a low parallelism. |
  | Resident adapters | | | pool of 4 instances, 16 in flight each per pod: 64 concurrent per adapter, enough |

- **Default for existing deployments.** Two choices:

  | | Default parallelism = prefetch | Default parallelism = 1 |
  |---|---|---|
  | On upgrade | every lane runs up to 12 at once, which the docs always said it did | nothing changes until someone raises it |
  | Risk | partners see concurrent calls; ordering within a lane goes | nobody gains until configured |
  | Fits "backward compatible" | the documented behaviour, but not the observed one | the observed behaviour |

- **How the gain is measured.** Queue health, in-flight per node per lane, above 1. On the
  harness: a lane with a simulated partner answering in 500 ms goes from about 2 exchanges per
  second per pod to about 20 at parallelism 12; the queue depth graph stops climbing under the
  same load. Commands per exchange stay the same, so the database load rises with throughput.


- **Measured 2026-10-14 (step 2.7).** `tests/load` harness, one Bitween node on Colima (4 CPU,
  8 GB), sync gateway calls on the Ungrouped lane at a constant 10 calls/s for 60 s after a 10 s
  warm-up, the partner answering every call in 500 ms (`PARTNER_DELAY_MS=500`), `WAIT_PERIOD` 30 s.
  Same build both times; "before" is the lane pinned to `Bitween:BusDefaultConcurrency=1`, which is
  how every lane ran on 10.0 (one handler per lane per node), "after" the new default (the
  prefetch, 12). Partner concurrency counted by the partner itself.

  | | Parallel exchanges 1 (before) | Parallel exchanges 12 (after) |
  |---|---|---|
  | Exchanges the lane processed | about 2/s (one 500 ms partner call at a time) | 10/s, the whole offered rate |
  | Sync calls answered 200 | 30 of 371 (0.5/s); the rest waited out the 30 s wait period | 595 of 601 (9.9/s) |
  | Sync latency p50 / p95 / p99 | 29.7 s / 29.7 s / 29.8 s (the wait period) | 512 ms / 557 ms / 566 ms |
  | Iterations k6 could not start (200 VUs all waiting) | 241 | 0 |
  | Most partner calls at once | 1 | 6 (10/s x 0.5 s, plus overhead) |

  The lane's ceiling at 12 with a 500 ms partner is 12 / 0.5 = 24/s per node; a run above 10/s to
  show it was not made because Colima's Docker disk was full (the stack's statement count step
  failed with "No space left on device" at the end of the second run, which may also account for
  its 6 failed calls). Queue health's **In flight** is the broker's unacknowledged count, which
  includes prefetched messages, so it read up to the prefetch before this change too and cannot
  show the gain; the new **Parallel** column and the partner's own count are the evidence. The
  plan's "about 2 to about 20" holds: 2/s before, the offered 10/s after with p95 a few
  milliseconds over the partner's own 500 ms.
- **Backward compatibility.** SW-Bus: a new major version; no public API removed. Bitween: an
  additive field in the work group options JSON; no migration; the chart gains one value.
  Partner-facing concurrency and ordering as in the table above.

- **Decision (2026-10-14):** Option C. SW-Bus moves to RabbitMQ.Client 7 with per-channel
  concurrency and publisher confirms, and Bitween sets parallelism per work group. This also
  closes finding 7. Assumptions to confirm at the start of the implementing session, chosen
  here so the work can begin: Bitween lanes default to parallelism = prefetch (the documented
  behaviour) with upgrade notes that say how to pin a lane to 1; the SW-Bus library default
  stays 1; the shutdown drain is included.

- **Implementation plan for the separate session.** Two repositories, in this order, because
  Bitween can only take the package once it is on nuget.org. SW-Bus is not in this workspace:
  clone `https://github.com/simplify9/SW-Bus` beside `Bitween-api`. House rules apply to both:
  backward compatible, no em dashes anywhere, commit locally and push only when asked, no
  Co-Authored-By lines.

  **Part 1, SW-Bus (new major, 11.0.0)**

  | Step | What | Where in SW-Bus | Done when |
  |---|---|---|---|
  | 1.1 | Run the existing tests to get a baseline. Note Bitween's `SW.Bitween.TransportTests` and the Testcontainers suite as the second safety net. | | tests green on master |
  | 1.2 | Bump `RabbitMQ.Client` to the current 7.x. Keep `EasyNetQ.Management.Client`, `Scrutor` and `SimplyWorks.HttpExtensions` as they are. | `SW.Bus.csproj` | builds with the new API errors listed |
  | 1.3 | Options: `BusOptions.DefaultConsumerConcurrency` (default 1), `BusOptions.PublisherConfirms` (default true), `BusOptions.RequestedHeartbeat` kept; `ConsumerOptions.Concurrency` (nullable, per queue) and the same on `AddQueueOption`. | `BusOptions`, `ConsumerOptions`, `ConsumerDefinition` (resolve `Concurrency = queueOptions?.Concurrency ?? DefaultConsumerConcurrency`) | options documented in the README table |
  | 1.4 | Connection: one shared connection opened asynchronously by a hosted service at start, not inside `AddBus` or `AddBusPublish`. Exchange declarations move there too. Client automatic recovery stays on and owns reconnects and topology recovery; SW-Bus's own re-attach on `ConnectionShutdown` is removed. `RefreshConsumers` keeps doing what it does: attach consumers for queues that appeared after start. | `IServiceCollectionExtensions`, `ConsumersService`, a new `BusConnection` | a pod whose broker is down at boot starts, reports not ready, and connects when the broker returns |
  | 1.5 | Consumers: a channel per queue from `CreateChannelAsync(new CreateChannelOptions(consumerDispatchConcurrency: definition.Concurrency))`, `BasicQosAsync(0, prefetch, false)`, `AsyncEventingBasicConsumer.ReceivedAsync`, `BasicConsumeAsync(autoAck: false)`. Queue names, arguments, bindings, the retry queue's TTL and the bad queue's TTL stay byte for byte what they are: the broker refuses a redeclaration with different arguments. | `ConsumersService.AttachConsumer`, `DeclareAndBindListener` | a queue with concurrency 4 runs 4 handlers at once on one pod; with 1, one at a time in order |
  | 1.6 | Runner: `BasicAckAsync`, `BasicRejectAsync`, `BasicPublishAsync` for the bad queue; the `x-death` retry counting and the `OnFail` hook unchanged. | `ConsumerRunner` | retry and bad queue behaviour identical to 10.0.3 in the tests |
  | 1.7 | Publishing: no shared channel. A channel per DI scope, created lazily on the first publish of the scope from the shared connection and disposed with the scope, with `publisherConfirmationsEnabled` and tracking on when `PublisherConfirms` is true. `BasicPublishAsync` then completes when the broker has the message. `DeliveryMode` persistent as today. `IPublishWithValues` headers unchanged. | `BasicPublisher`, `Publisher`, `Broadcaster`, `AddBusPublish` | a publish against a stopped broker throws instead of returning |
  | 1.8 | Drain: `StopAsync` cancels every consumer, waits for in-flight handlers up to `BusOptions.DrainTimeout` (default 30 s), then closes channels and the connection. | `ConsumersService.StopAsync` | an exchange running during a pod stop finishes and acks |
  | 1.9 | Tests with a Testcontainers broker: concurrency honoured per queue; order kept at 1; confirms; broker restart leaves exactly one consumer per queue per pod (read through `IConsumerReader`); drain on stop. | `SW.Bus.Tests` or the existing test project | green |
  | 1.10 | Version 11.0.0, README updated (options table, the concurrency and confirms behaviour, the breaking notes for products that use `RabbitMQ.Client` directly), commit, then ask before pushing and releasing to nuget.org. | | package on nuget.org |

  **Part 2, Bitween-api**

  | Step | What | Where | Done when |
  |---|---|---|---|
  | 2.1 | Bump `SimplyWorks.Bus` and `SimplyWorks.Bus.RabbitMqExtensions` to 11.0.0 in `SW.Bitween.Web` and `SW.Bitween.Api`. | the two csproj files | restore succeeds |
  | 2.2 | Port the direct client use to the 7 API: `Services/Cluster/RabbitMqLeaderElection.cs`, `Resources/Ops/DeadLetterQueues.cs`, `Resources/Ops/RequeueDeadLetters.cs`, `SW.Bitween.Web/DependencyHealthChecks.cs`, then the 7 integration tests and `HttpTests/UnattendedQueueTests.cs`. `IModel` to `IChannel`, synchronous calls to their async forms, nothing else. Leader election keeps its exclusive-queue semantics exactly. | the files named | builds; `BusProviderSupervisorTests` and `SharedBrokerTests` green on Colima |
  | 2.3 | Work group: `RabbitMqOptions.Concurrency` (nullable int) in the options JSON. `XchangeService.GetMessageTypeNamesWithOptions` passes `Concurrency = Concurrency ?? Prefetch ?? BusDefaultQueuePrefetch` for the work lane and 1 for its `-Result` lane. `Bitween:BusDefaultConcurrency` (default: the default prefetch) for the ungrouped lane. | `Domain/WorkGroup`, `XchangeService`, `BitweenOptions`, `Startup` (`config.DefaultConsumerConcurrency`, `config.PublisherConfirms = true`, heartbeat 60 s) | Queue health shows in-flight above 1 on a lane |
  | 2.4 | UI: **Parallel exchanges** beside prefetch on the Work groups page, with help text saying 1 keeps order and limits a partner to one call at a time per node; Queue health lanes show it. | `ClientApp` work groups page and queue health lane rows | field saves and shows |
  | 2.5 | Docs: `concepts.md` (work group), `exchange-pipeline.md` limits table (prefetch is the buffer, parallel exchanges is the concurrency), `configuration.md` (the new setting), `operations.md` (the slow-partner row), `deployment.md` or the chart README (`busDefaultConcurrency`), and upgrade notes: partners now receive concurrent calls, order within a lane is not kept above 1, how to set a lane to 1 before upgrading. | docs, `charts/default/values.yaml` | docs match the code |
  | 2.6 | Tests: `SW.Bitween.TransportTests`, the Testcontainers suites (DOCKER_HOST on the Colima socket), `HttpTests`. | | green |
  | 2.7 | Measure: on the harness from finding 4 phase 0 if it exists, else by hand against the docker-compose stack: a lane with a 500 ms partner goes from about 2 to about 20 exchanges per second per pod at 12; in-flight on Queue health above 1. Record the numbers in this file. | | numbers recorded |
  | 2.8 | Commit; push to `r10.0-develop` only when asked, and only after SW-Bus 11.0.0 is on nuget.org. | | |

  Out of scope for this session, deliberately: quorum queues or any queue argument change,
  changes to retry or bad queue semantics, lowering the adapter timeouts, finding 4's work.

### 2. Sync gateway calls wait at least one second

- **Status:** decided 2026-10-14 (2a to 2d; 2e dropped). Remove once shipped.
  Implemented 2026-10-14 together with finding 3, as local commits on `releases/r10.0-develop`,
  not pushed: 6535a5c3 (phases 2.1 to 2.3) and 25ec5f50 (2.4 with 3.5).
  Measured on the harness (`tests/load`, one node, `PARTNER_DELAY_MS=100`, 5/s for 1 min after a
  15 s warm-up; at 20/s the single lane saturates near 8/s with a 100 ms partner, so latency
  there is queueing, not the sync path): before (259c98c4) p50 1011 ms, p95 1032 ms, p99 1064 ms;
  after (25ec5f50) p50 115 ms, p95 125 ms, p99 132 ms, so about 15 ms of Bitween on top of the
  partner's 100. Against the instant nginx partner at 20/s, the finding 4 run: p50 1006 ms, p95
  1015 ms before, p50 8.5 ms, p95 12.6 ms after. Database commands per sync exchange 4.03 before,
  3.03 after (`exchange_stats`): no poll, and nothing after the result's save.
  Differences from the plan: `AwaitedOn` names the `ResultSignals` instance (`NodeHeartbeat.NodeName`
  plus a random suffix per application instance), because two applications in one process (the
  HTTP tests' second node) share a node name; on the same node the response is handed over in
  memory whatever its size, the cap applies to the broadcast only; a replayed or dropped call,
  answered with an exchange it didn't create, has no signal and is checked at once, then polled
  on the old schedule; the backstop timing is not a setting. The legacy
  `POST /api/xchanges/{type}` wait keeps its poll. A node on the previous version ignores the
  broadcast (SW-Bus acks a message type it can't resolve).
- **Where:** `SW.Bitween.Api/Services/ResultWait.cs`, used by `GatewayController.ProcessAsync`
- **What happens:** After creating the exchange the request sleeps 1 s before the first result
  check, then 1, 2, 3, 5, 8, 8 s between checks. Each check is a database query.
- **Why it hurts:** A 200 ms exchange is answered at 1 s. A 1.2 s exchange at 2 s. A 3.1 s
  exchange at 7 s. Many waiting callers means many polling queries.
- **Candidate fix:** Signal instead of poll: an in-process registry of waiting exchange ids
  completed right after the result save, plus a bus broadcast for results saved on another node,
  with a 100 ms poll as fallback. Optional inline mode for subscriptions whose mapper and handler
  are native: run in the request, no queue hop, same persistence.
- **Expected effect:** sync p50 for a fast partner from about 1.5 s to about 300 ms.

- **What happens today, exactly.** `GatewayController.ProcessAsync` creates the exchange,
  saves, publishes the created event, then calls `ResultWait.UntilAsync`, which sleeps first and
  asks afterwards: 1 s, then 1, 2, 3, 5, 8, 8 s between checks, each check a `SELECT 1 FROM
  xchange_results WHERE id`. When the row is there the request loads the result and downloads
  the response from storage. The worker that produced the result never tells anyone; the
  request finds out by asking. The wait is capped by `Bitween:MaxResponseWaitSeconds` (300 s),
  after which the caller gets 202 with the exchange id.

  Where the time goes for a partner that answers in 100 ms: about 150 ms of real work (storage,
  database, broker hop, the call), then up to 850 ms of sleeping until the first check.

- **The fix: the worker rings a bell.** Three pieces, two of them already shaped by findings 1
  and 3.

  | Piece | What | Where |
  |---|---|---|
  | 2a | A `ResultSignals` singleton per pod: a dictionary of exchange id to a `TaskCompletionSource`. The gateway registers its exchange id **before** publishing the created event, so a result cannot arrive unannounced, then awaits the completion with the caller's timeout and cancellation. | new `Services/ResultSignals.cs`, `GatewayController` |
  | 2b | The created message carries `AwaitedOn`: the name of the node holding the request (`NodeHeartbeat.NodeName`). Only sync gateway calls set it, so async traffic is untouched. After the worker commits the result, it completes the signal locally when `AwaitedOn` is this node, otherwise it broadcasts `XchangeResultReady {Id, Node, Success, ResponseBad, sizes, Response?}` over SW-Bus's node exchange (`IBroadcast`, which every pod receives); the pod whose name matches completes its signal, the others drop it. One broadcast per sync exchange that finished on another pod, nothing otherwise. | `XchangeMessage`, `XchangeService.Process`, a new `IListen<XchangeResultReady>` |
  | 2c | The signal carries what the answer needs: outcome, sizes, and the response body when under the cap (finding 3's 3.5). The success answer is then built with no database read and no storage read; the failure answer keeps its two reads (retry time, mapper present) since it is rare. | `GatewayController` |
  | 2d | The poll stays as a backstop, not the mechanism: if no signal has arrived after 2 s, check the database once, then every 5 s. It covers a signal lost to a pod restart and a worker still on the previous version. | `ResultWait` |

  Why a broadcast and not something else: it needs no new infrastructure (Postgres NOTIFY would
  be Postgres only; Redis would be a new dependency), it is already how cache revokes travel,
  and it is one small message per cross-pod sync exchange. With one pod nothing is broadcast.

- **2e, running the exchange inside the request: not taken.** It was considered and dropped
  (2026-10-14). The queue is not only a hop: the lane's prefetch and parallelism are the
  throttle that protects Bitween and the partner's system from a burst of sync calls, and the
  work group is what isolates one integration from another. Running in the request bypasses
  both, so a burst of partner calls would hit the target system at whatever rate the HTTP
  server accepts. If a shortest-path mode is ever wanted it is a feature of its own, a
  **proxy API**, with its own throttling and limits designed in, not a switch on the gateway.

- **Expected numbers.** Sync answer = creation (one save, one overlapped PUT) + broker hop
  (5 to 20 ms) + processing + signal (0 ms same pod, 5 to 20 ms broadcast) + answer (0 reads
  under the cap). For a partner answering in 100 ms: about 250 to 350 ms end to end, against 1.3
  to 1.6 s today.

- **How it is measured.** The harness drives sync calls against a stub partner with a fixed
  delay; p50 and p95 of the HTTP answer are the numbers; the database poll count per sync
  call, from finding 4's command counter, should be zero on success.

- **Backward compatibility.** No schema. The created message gains an optional field; the
  result-ready message is new and only a listener on the new version receives it. A worker on
  the previous version does not signal, and the backstop poll answers as today within a few
  seconds. The `Wait-Period` header, the 202 fallback and the response shapes are unchanged.
  Mixed versions during a deployment: a sync call answered by the backstop instead of the
  signal, slower but correct.

- **Decision (2026-10-14):** 2a, 2b, 2c and 2d. 2e dropped; a proxy API with its own throttling
  may be designed later as a separate feature. Assumed backstop timing: first database check
  2 s after the call, then every 5 s.

- **Implementation plan for the separate session.** One repository, Bitween-api. No schema, no
  SW-Bus change (the broadcast uses `IBroadcast` and `IListen<T>`, which exist). Phase 2.3
  depends on finding 3's message cap setting (3.3); if that has not shipped, ship 2.3 with the
  response left out and add it when it has.

  | Phase | Steps | Where | Done when |
  |---|---|---|---|
  | 2.1 | `ResultSignals` singleton: `Register(id)` returns an awaitable, `Complete(id, ready)` completes it, entries expire with the caller's timeout so an abandoned call leaves nothing behind. Registered in `Startup`. | new `Services/ResultSignals.cs`, `Startup` | unit tests: complete before and after register, timeout, cancellation |
  | 2.2 | Gateway: register the exchange id before `SaveChangesAsync` publishes the created event; await the signal with the clamped wait and `HttpContext.RequestAborted`; on signal, answer from it; on no signal, fall through to the backstop poll (`ResultWait` with the new timing: first check at 2 s, then every 5 s) and the existing answer code. The 202 path and the failure answers unchanged. | `GatewayController.ProcessAsync`, `ResultWait` | `HttpTests` for sync gateways green; a sync call on one pod answers within processing time plus a few milliseconds |
  | 2.3 | Worker: `XchangeMessage` gains `AwaitedOn` (set by the gateway only). After the result `SaveChangesAsync` in `Process`, both on the success and the failure path: if `AwaitedOn` is this node, `ResultSignals.Complete`; else `IBroadcast.Broadcast(new XchangeResultReady { Id, Node, Success, ResponseBad, OutputSize, ResponseSize, ResponseContentType, Response (under the cap) })`. A new `IListen<XchangeResultReady>` completes the local signal when `Node` matches. Nothing is sent for messages without `AwaitedOn`. | `XchangeCreatedEvent.cs` (`XchangeMessage`), `XchangeService.Process`, new `Services/ResultReadyListener.cs` | a sync call whose exchange is processed on another pod answers within processing time plus the broadcast hop; the Testcontainers pipeline test covers two consumers |
  | 2.4 | Answer from the signal: success with a response under the cap needs no database and no storage read; success with a larger response reads storage as today; failure keeps its two reads. | `GatewayController` | finding 4's command counter shows zero database commands after the save on a small successful sync call |
  | 2.5 | Docs: `exchange-pipeline.md` sequence (the worker signals the waiting request), `entry-points.md` sync section, `operations.md` symptom row for sync calls, `configuration.md` if a setting is added for the backstop timing. Record measured p50 and p95 here. | docs | docs match the code |

  Out of scope: running exchanges in the request (see 2e above), changes to the 202 fallback,
  the `Wait-Period` semantics, or the queue topology.

### 3. Object storage is on the critical path five times

- **Status:** decided 2026-10-14 (3a, 3b, 3e, 3f; 3c rejected). Remove once shipped.
  Implemented 2026-10-14 together with finding 2, as local commits on `releases/r10.0-develop`,
  not pushed: 183a1a6d (3.1, 3.2), 233ed43e (3.3), 25ec5f50 (3.5 with finding 2's 2.4); harness
  additions in a51c9a5e. 3.4 (same-pod cache) skipped: after 3.3 the input reads were already
  zero for every exchange under the cap, so it would only serve inputs over 256 KB and
  dispatcher replays on the creating pod.
  Storage calls per sync exchange on the harness (SeaweedFS request counter, no mapper, so no
  output file): before 4.00 (2 PUT, 2 GET); after 3.3 3.00 (2 PUT, 1 GET, the answer's response
  read); after 3.5 2.00 (2 PUT, 0 GET), at 5/s and at 20/s. The harness storage answers in under
  a millisecond, so it shows the calls removed, not their cost on S3 or Azure Blob; latency moved
  only with finding 2. Size percentiles from live deployments not yet run: the 256 KB cap stands.
  Differences from the plan: overlapping a write with the insert itself would need a transaction
  around the save, whose commit is a round trip finding 4 removed, so each write overlaps the work
  before its save (promoted properties and the caller's checks for the input, the handler for the
  output, result preparation for the response) and the save waits for it. The output write is
  the one that leaves the critical path; the gateway does not get a PUT faster. One consequence:
  an output write that fails is now seen after the handler delivered rather than before it ran;
  the exchange fails and its policy may retry. Batch creators (bulk retry, unpause, aggregation)
  now write their inputs in parallel. The created message carries `Input` only, not the name,
  content type or bad-data flag: those are on the row, and the storage path never used the flag,
  so carrying them would make the worker behave differently by path. The worker uses the carried
  input only when it matches the row's `InputHash`.
- **Where:** `XchangeService.AddFile`, `GetFile`, `Process`, `RunMapper`, `RunHandler`
- **What happens:** Input is uploaded at creation and downloaded again by the consumer. Output
  and response are uploaded one after the other before the result is saved. A sync caller's
  response is downloaded again to answer. Every payload goes to object storage whatever its size.
- **Why it hurts:** 5 network round trips of 30 to 80 ms each on a path whose business logic
  takes a few milliseconds. The consumer re-downloads a file the API pod uploaded seconds earlier.
- **Candidate fix:** Keep payloads under a threshold (for example 256 KB) in the database and
  spill only large ones to object storage. Hand the input in memory from creator to a same-node
  consumer through a short-lived cache keyed by exchange id. Upload output and response in
  parallel with the result save.
- **Expected effect:** 150 to 400 ms off every exchange.

- **The five storage calls today**, sync gateway call, success. The storage client is a
  singleton per provider (`SW.CloudFiles.S3`, `AS`, `OC`), so there is no per-call client
  set-up; each call is one HTTPS request to the storage service.

  | # | Call | When | Who waits for it |
  |---|---|---|---|
  | 1 | PUT input | creation, before the exchange row is saved | the partner's HTTP request |
  | 2 | GET input | start of processing | the lane |
  | 3 | PUT output | after the mapper, before the handler runs | the lane |
  | 4 | PUT response | after the handler, before the result is saved | the lane |
  | 5 | GET response | to answer a sync caller | the partner's HTTP request |

  Every reader of an exchange file goes through `XchangeService.GetFile`, `OpenFile` or
  `FindFileOwner`: the UI viewer (`BitweenDocs/Get`), served links (`FileLinksController`),
  export, archive, retry, source values, the sync answer and the legacy endpoint. That is the
  one place a "where does this file live" decision has to be made, which is what makes the
  options below contained.

- **Options, from smallest to largest. They stack.**

  | Id | Change | Removes | Cost and compatibility |
  |---|---|---|---|
  | 3a | Overlap instead of wait: start the output PUT and run the handler at the same time; start the response PUT and prepare the result save at the same time; await both before the save commits. | the latency of calls 3 and 4 from the critical path (the calls still happen) | code only in `XchangeService.Process`; no schema; a PUT that fails still fails the exchange before the result is saved, as today |
  | 3b | Same-pod handoff: at creation keep the input in a bounded in-memory cache keyed by exchange id (size cap, 60 s expiry); the worker reads it from there and falls back to storage. | call 2 when the pod that created the exchange is the one that processes it: always with one pod, about 1 in N with N pods | code only; memory bounded by the cap; nothing changes when the cache misses |
  | 3c | Small payloads live in the database: input on the exchange row, output and response on the result row, when under `Bitween:InlinePayloadMaxBytes`; larger ones go to storage as today. Readers check the row first, then storage. Storage is still written for inline payloads only if `Bitween:AlwaysStoreFiles` is on (off by default). | calls 1, 2, 3, 4 and 5 for every payload under the threshold | three nullable text columns, additive. Database size grows by about three payloads per exchange, so this needs exchange retention set (finding 9) or the deployment accepts the growth. The storage lifecycle rule (30 days under `temp30/`) no longer applies to inline payloads: they live as long as the exchange row. Old exchanges keep reading from storage. Export, archive and the UI read through the same abstraction and need no change in shape. |
  | 3d | The sync answer comes from memory: with finding 2's result signal, the worker on the same pod hands the response to the waiting request; otherwise the request reads it from the result row (3c) or storage. | call 5 | depends on finding 2 |

  Not an option: changing the storage key layout or the served-link scheme. Partners and
  aggregation handlers hold links to `{prefix}/{id}/{type}` and those keep working.

  **3c rejected (2026-10-14):** the database must not carry payloads. Storage stays the system
  of record for files. The alternative below keeps that and still takes the reads off the hot
  path.

  | Id | Change | Removes | Cost and compatibility |
  |---|---|---|---|
  | 3e | The payload travels with the message. When the input is under `Bitween:MessagePayloadMaxBytes` (256 KB proposed) the created event published right after the commit carries it; the worker uses it and never reads storage. The outbox row stays id-only, so a message the dispatcher re-publishes later carries no payload and the worker falls back to the storage GET, as today. The same in the other direction: the result-ready signal of finding 2 carries the response when small, so a sync answer on another pod needs no GET either. | calls 2 and 5 on every pod, not only the creating one; nothing is added to the database | RabbitMQ holds a transient copy of each small payload until it is consumed, persistent and durable as every message already is; 256 KB is far under the broker's limits. Old pods ignore the extra field and read storage. The message contract gains an optional field, nothing else changes. |
  | 3f | Writes overlap the save: the input PUT runs at the same time as the exchange insert, and the request waits for both before answering (max instead of sum); 3a does the same for output and response. A PUT that fails still fails the exchange before anything is committed. | the waiting time of call 1, 3 and 4 down to whichever of the two is slower | code only; the invariant "a saved row's files exist" is kept because nothing commits until the PUT returns |

  With 3a, 3b, 3e and 3f and no 3c: the three PUTs still happen, for the UI, retry, export,
  archive and served links, but each overlaps database work instead of adding to it, and no GET
  is on the hot path. Storage waiting per exchange drops from five calls in a row to roughly one
  call's worth, with the database unchanged in size. What 3c would have added on top of that is
  the storage bill and the PUTs themselves, at the price of a growing database; not taken.

- **The threshold for 3e** is the message size cap. The size columns already stored answer
  what it should be:

  ```sql
  select percentile_cont(0.5) within group (order by input_size) as p50,
         percentile_cont(0.95) within group (order by input_size) as p95,
         percentile_cont(0.99) within group (order by input_size) as p99,
         max(input_size) as max, count(*) as n
  from infolink.xchanges where started_on > now() - interval '30 days';
  ```

  The same for `output_size` and `response_size` on `xchange_results`. If p95 is under the cap,
  nearly every exchange gets the handoff; what is above it reads storage as today. Numbers from
  two or three live deployments decide the default.

- **How the gain is measured.** Stage timings from finding 4 phase 0: storage read time per
  exchange goes to zero for payloads under the cap, and storage write time stops adding to the
  stage times it overlaps. End-to-end async latency drops by about four calls' worth as measured
  on that deployment's storage (30 to 80 ms each on S3-class storage in the same region, more
  across regions or on Azure Blob with private endpoints).

- **Backward compatibility.** 3a, 3b, 3f: none; files are written as today, only the waiting
  changes. 3e: the created event and the result signal gain an optional payload field; an old
  pod ignores it and reads storage; storage keys, served links, export, archive and retry are
  untouched because every file is still written. No schema change anywhere in this finding.

- **Decision (2026-10-14):** 3a, 3b, 3e and 3f. 3c rejected: the database does not carry
  payloads. Storage stays the system of record and every file is still written. Assumed until
  the size query has been run on live deployments: a 256 KB cap for payloads carried in
  messages. The response half of 3e waits for finding 2's result signal.

- **Implementation plan for the separate session.** One repository, Bitween-api. No schema
  change, no SW-Bus change, no new infrastructure. The work is in `XchangeService` and the
  message the created event carries. Phases are shippable one at a time; 3.1 and 3.2 can ship
  together.

  | Phase | Steps | Where | Done when |
  |---|---|---|---|
  | 3.1 | **3a, overlap in the worker.** In `Process`: start the output PUT (`AddFile`) as a task and run the handler at once; await the PUT before `RunHandler` returns its file. Start the response PUT as a task and prepare the result (`StampAsync`, retry evaluation, `XchangeResult`) meanwhile; await it before `SaveChangesAsync`. A failed PUT still throws before anything is saved, so the failure path is unchanged. | `XchangeService.RunMapper`, `RunHandler`, `Process` | stage timings show storage write time no longer adding to mapper-to-result time; the existing pipeline tests green |
  | 3.2 | **3f, overlap at creation.** In every entry point that creates an exchange (`SubmitSubscriptionXchange`, `SubmitFilterXchange`, `CreateXchange` overloads, `GatewayController`, `ReceivingJob`, `AggregationJob`, the unpause path), start the input PUT as a task and run the duplicate check, token filling and `Add` meanwhile; await the PUT before `SaveChangesAsync`. The cleanest shape is for `AddInputFile` to return the task and for a single "await pending files" step just before the save, inside `XchangeService`, so no caller can forget it. | `XchangeService.AddInputFile` and its callers | gateway request time drops by about one PUT's worth on the harness; `HttpTests` green |
  | 3.3 | **3e, payload in the message.** `XchangeMessage` gains optional `Input`, `InputName`, `InputContentType` and `BadData`. `BitweenDbContext.ToOutboxMessage` keeps the outbox body id-only. In `PublishOutboxAsync` the publish straight after the commit attaches the payload when it is under `Bitween:MessagePayloadMaxBytes`; the dispatcher's re-publish does not. In `Process`, use the message's payload when present and only call `GetFile` when it is not. The hash is not recomputed: it is on the row. | `XchangeCreatedEvent.cs` (`XchangeMessage`), `BitweenDbContext`, `OutboxMessage` (an in-memory, not persisted, payload reference), `XchangeService.Process`, `BitweenOptions` | storage read time is zero for exchanges under the cap; a message replayed by the dispatcher still processes (falls back to `GetFile`); a message with a payload consumed by a pod of the previous version still processes |
  | 3.4 | **3b, same-pod cache.** A bounded `IMemoryCache` entry per exchange id at creation (size-limited, 60 s), read in `Process` before `GetFile` and after the message payload; covers exchanges over the cap and dispatcher replays on the creating pod. Small, optional; skip if 3.3 numbers make it irrelevant. | `XchangeService` | hit rate visible in a counter |
  | 3.5 | **3e, response half.** When finding 2 lands: the result-ready signal carries the response under the cap, and the sync answer uses it instead of `GetFile`. | with finding 2 | sync answer makes no storage call for small responses |
  | 3.6 | Settings and docs: `Bitween:MessagePayloadMaxBytes` (256 KB default) in `configuration.md`; `architecture.md` storage section says the payload also travels with the message; `exchange-pipeline.md` sequence updated. Record the size percentiles from the live deployments and the measured stage timings here. | docs | docs match the code |

  Out of scope: anything that keeps payloads in the database; changes to storage keys, served
  links, retention or the bucket rules; the result signal itself (finding 2).

### 4. Write amplification on the database

- **Status:** implemented 2026-10-14, committed locally, **not pushed**. Remove this finding and
  its rows in the plan table once pushed and released. Follow-ups carried elsewhere: the k6
  harness is not in CI (finding 10); the harness builds the image from a copy without the local
  `NuGet.config` (`tests/load/README.md`). One auto-pause write remains on the failure path by
  design: failures update the subscription's count and last error for its page; successes write
  nothing. 4f's credential staleness concern is closed: any save touching a partner, key,
  gateway, attachment or retry policy revokes the cache on every node.
  Implemented 2026-10-14 as one local commit per phase on `releases/r10.0-develop`, not pushed.
  Measured by `ExchangeCommandCountTests` (one sync gateway call, native handler, no mapper, no
  notifications, caches warm): 13 commands at baseline (not 14: the worker's partner read only
  happens with a mapper), then 9, 7, 5 and 4 after phases 1 to 4. Beyond the plan: the cache is
  revoked by any save touching a partner, key, gateway, attachment or retry policy; Queue health's
  "sent in the last hour" is counted per node on the heartbeat (new nullable
  `ClusterNodes.OutboxPublishedLastHour`), since 4a deletes the rows it counted; auto-pause still
  writes the count and error to the subscription on failures, and a resume still restarts the
  count; the sync wait reads the result in the poll that finds it (the fourth command). Stage
  timings go to OpenTelemetry only.
  Harness (`tests/load`, 20/s for 1 min, final code): 1201 calls, all 200; p50 1006 ms, p95
  1015 ms, p99 1030 ms (the 1 s sync poll floor of finding 2); `exchange_stats` 4.01 commands per
  exchange; pg_stat_statements 6.07 SQL statements per exchange (three inserts in the creation
  batch, the worker's load, the result insert, the poll), plus 4.1 `DISCARD ALL` from Npgsql's
  pool reset, which rides with the next command.
- **Where:** `BitweenDbContext.SaveChangesAsync` and `PublishOutboxAsync`, `OutboxDispatcher`,
  `XchangeService.TryClearingRetryBudgetAfterSuccess`, `XchangeService.ProcessResult`
- **What happens:** Every bus message is an outbox insert plus an update after publish, and
  published rows are kept a day. Every success queries `RetryGroupUsages`. Every exchange
  publishes a result-lane message whose consumer loads the exchange and result again, even when
  no notification or legacy notifier is configured. The dispatcher polls every 5 s on every pod
  with no partial index on unpublished rows.
- **Why it hurts:** About 14 database trips per exchange where 6 or 7 would do. Outbox churn
  bloats Postgres at volume.
- **Every database statement today**, sync gateway call, native mapper and HTTP handler,
  success, no notifications, no duplicate window, no idempotency header. Each numbered line is
  one round trip unless it says otherwise. EF Core batches every row added to one `SaveChanges`
  into one command on all three providers, so a save with three inserts is one round trip.

  Creation, in the HTTP request's scope (`GatewayController.ProcessAsync`):

  | # | Statement | Kind | Note |
  |---|---|---|---|
  | 1 | `SELECT` ApiGateways joined to ApiGatewayPartners and Partners by url name | read | configuration; a second query if the exact name misses |
  | 2 | `SELECT` PartnerApiCredentials by key hash (`CheckPartnerAuthorized`) | read | credential |
  | 3 | `SELECT` Partners by id, no tracking (`GatewayCallers`) | read | configuration |
  | 4 | `UPDATE` ApiKeyUses last used | write | own scope, throttled to once per key per 5 min, so not per call |
  | 5 | `SELECT` Xchanges by document, hash, partner, started date (`DuplicateArrivals`) | read | only when the type has a window |
  | 6 | `SELECT` IdempotencyKeys, then `DELETE` expired same key | read, write | only when the header is sent |
  | 7 | `INSERT` Xchanges + `INSERT` OutboxMessages, one batch | write | the exchange and its created event |
  | 8 | `UPDATE` OutboxMessages set published_on (`PublishOutboxAsync`) | write | after the broker publish |

  Processing, in the consumer's scope (`XchangeService.Process`):

  | # | Statement | Kind | Note |
  |---|---|---|---|
  | 9 | `SELECT` Xchanges by id (`FindAsync`) | read | |
  | 10 | `SELECT` 1 from XchangeResults where id (`AnyAsync`) | read | repeat-delivery guard |
  | 11 | `SELECT` Partners by id (`MappingContextFactory`, or the enrichment path) | read | configuration |
  | 12 | `SELECT` RetryGroupUsages by subscription (`ReleaseExhaustedBudgets`) | read | every success |
  | 13 | `INSERT` XchangeResults + `INSERT` XchangePromotedProperties + `INSERT` OutboxMessages, one batch | write | the result, the properties, the result event; FK check on the result row |
  | 14 | `UPDATE` OutboxMessages set published_on | write | after the broker publish |
  | 15 | `UPDATE` Subscriptions consecutive failures (`TrackDeliveryHealth`) | write | only when auto-pause is set on the subscription |

  Result lane, in a second consumer's scope (`XchangeService.ProcessResult`):

  | # | Statement | Kind | Note |
  |---|---|---|---|
  | 16 | `SELECT` XchangeResults by id | read | |
  | 17 | `SELECT` Xchanges by id | read | then returns: no notifications configured |

  Sync wait, back in the request's scope:

  | # | Statement | Kind | Note |
  |---|---|---|---|
  | 18 | `SELECT` 1 from XchangeResults where id, once per poll | read | first poll after 1 s |
  | 19 | `SELECT` XchangeResults by id | read | to build the answer |

  Total on this path: 14 round trips always (1, 2, 3, 7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 18,
  19 minus the conditional ones), of which 4 write: two insert batches and two outbox updates.
  Five object storage calls and two broker publishes sit between them. On the bus front door,
  `BusService` adds one `SELECT` of bus-enabled documents per message instead of using the cache.

  ```mermaid
  sequenceDiagram
    participant P as Partner
    participant G as Gateway (request scope)
    participant DB as Database
    participant S as Object storage
    participant MQ as RabbitMQ
    participant W as Worker (consumer scope)
    participant R as Result lane consumer
    P->>G: POST /sync
    G->>DB: 1 SELECT gateway+partners
    G->>DB: 2 SELECT credential by hash
    G->>DB: 3 SELECT partner
    G->>S: PUT input
    G->>DB: 7 INSERT exchange + outbox
    G->>MQ: publish created
    G->>DB: 8 UPDATE outbox published
    MQ->>W: deliver
    W->>DB: 9 SELECT exchange
    W->>DB: 10 SELECT result exists?
    W->>S: GET input
    W->>DB: 11 SELECT partner
    W->>S: PUT output
    W-->>P: HTTP call to partner system
    W->>S: PUT response
    W->>DB: 12 SELECT retry budget
    W->>DB: 13 INSERT result + promoted + outbox
    W->>MQ: publish result
    W->>DB: 14 UPDATE outbox published
    MQ->>R: deliver
    R->>DB: 16 SELECT result
    R->>DB: 17 SELECT exchange
    Note over G,DB: sleeps 1 s, then
    G->>DB: 18 SELECT result exists?
    G->>DB: 19 SELECT result
    G->>S: GET response
    G->>P: 200 with response
  ```

  The same call under the target model:

  ```mermaid
  sequenceDiagram
    participant P as Partner
    participant G as Gateway (request scope)
    participant DB as Database
    participant MQ as RabbitMQ
    participant W as Worker (consumer scope)
    participant B as Outbox batcher (background)
    P->>G: POST /sync
    Note over G: gateway, credential, partner, document from cache
    G->>DB: INSERT exchange (payload, promoted, key) + outbox
    G->>MQ: publish created, confirmed
    G-->>B: outbox id to delete
    MQ->>W: deliver (or inline on the same pod)
    W->>DB: SELECT exchange with has-result flag
    Note over W: partner, subscription, policy from cache; payload in the row or in memory
    W-->>P: HTTP call to partner system
    W->>DB: INSERT result (output and response inline when small)
    W-->>B: outbox id to delete, if a result event was needed
    W-->>G: result ready signal
    B->>DB: DELETE outbox rows, once a second
    G->>P: 200 with response
  ```

- **Tables an exchange touches.** Written on every exchange, written on some, or only read.

  | Table | Written | Read | Target |
  |---|---|---|---|
  | `Xchanges` | every exchange, 1 insert at creation | worker load, dedupe, retry chain, search | stays; gains promoted, key and small payload columns (A) |
  | `XchangeResults` | every exchange, 1 insert at completion | repeat guard, sync wait, result lane, search | stays; gains `subscription_id` and consecutive-failure count |
  | `XchangePromotedProperties` | every exchange, 1 insert at completion | search, archive | B: written at creation instead; A: folded into `Xchanges` |
  | `OutboxMessages` | every exchange, 2 inserts and 2 updates | dispatcher every 5 s per pod | stays; insert in batch, delete in batch, no update |
  | `DelayedRetries` | failures the policy retries | retry job every minute, sync failure answer, search | stays |
  | `XchangeNotifications` | each channel or notifier send, and each suppressed one | quiet-window check per send, retention | stays |
  | `XchangeRetryClaims` | each retry | never read; the PK enforces one retry per exchange | stays, or a partial unique index later |
  | `XchangeAggregations` | each exchange rolled up, by the aggregation job | search, aggregation job | stays; a column on `Xchanges` later |
  | `XchangeDeliveries` | never | never | drop |
  | `OnHoldXchanges` | documents routed to a paused subscription | unpause event | stays, rare path |
  | `DuplicateArrivals` | each duplicate within a window | the type's page count, prune | stays, rare path |
  | `IdempotencyKeys` | each gateway call that sends the header, plus an expiry delete | each such call | B: no expiry delete, retention expires; A: a column and unique index on `Xchanges` |
  | `RetryGroupUsages` | each failure under a budget | every success today; only budgeted policies after 4d | stays |
  | `InboundMessages` | each message from a customer broker with a dedupe window | the unique key is the check | stays, front door only |
  | `ApiKeyUses` | once per key per 5 minutes | partner page | stays |
  | `Subscriptions` | per result when auto-pause is set (`TrackDeliveryHealth`); per run by jobs | cache load, retry policy, result lane fallback | no per-exchange write after 4i |
  | `ReceiveAttempts` | per scheduled run, not per exchange | subscription page | stays |
  | `ExchangeStats`, `AdapterStats`, `ErrorStats` | once a minute per node from memory | dashboard | stays |
  | Configuration, read only: `ApiGateways`, `ApiGatewayPartners`, `Partners`, `PartnerApiCredentials`, `Documents`, `WorkGroups`, `GlobalAdapterValuesSets`, `BusGateways`, `BusGatewayRoutes`, `RetryPolicies`, `NotificationChannels`, `Notifiers` | by the admin UI only | per exchange today for gateways, credentials, partners and retry policies; the rest from the cache | all from the cache |

- **Why the side tables exist, from the history.** The first working build (2020-09-08) was
  preceded by a week of commits that set the shape still in use. On 2020-09-03 the exchange was
  split into four tables, each with its own id and a reference to the exchange:
  `Xchange` (submitted), `XchangePromotedProperties` (filtered), `XchangeDelivery` (delivered)
  and `XchangeResult` (finished), each raising its own event. That is a stage log: every stage
  appends one row and the exchange row is never touched again. The columns were never on
  `Xchanges` because each stage was meant to be its own append, possibly its own consumer. The
  delivery stage was never built, which is why `XchangeDeliveries` has always been empty. Later
  the side tables took the exchange id as their key, making them 1:1.

  Promoted properties **were** computed at creation in that first version: `RunFilterXchange`
  filtered on submission and saved the properties in the same save as the exchange. Between
  2020-09-05 and 2020-09-08 the filter moved into the consumer's `Process`, so the request
  thread only stores the file and answers, and every read of the payload happens off the request.
  That is the choice 4j reverses: extracting a few paths costs about a millisecond on the
  request thread, and it removes a parse and an insert from the worker.

- **The original rule and where it was broken.** Bitween was designed so that processing an
  exchange only inserts: the exchange row, its promoted properties, its result as a separate row
  with the same id, a delayed retry. No row is updated while an exchange runs. That rule still
  holds for the core. These later features added updates or extra reads on the hot path:

  | Added | Feature | What it does per exchange | Why it was done that way | Avoidable? |
  |---|---|---|---|---|
  | 2026-08-11 | Retry budgets (`RetryGroupUsages`) | One read per success; an atomic increment per failure under a budget | A counter shared by concurrent failures has to be atomic | The read: yes (4d). The increment: failures only, keep. |
  | 2026-10-08 | Outbox | One update per published message, twice per exchange | Mark the row so the dispatcher does not publish it again | Yes: delete in a batch off the hot path (4a) |
  | 2026-10-08 | Auto-pause (`TrackDeliveryHealth`) | A conditional update of the `Subscriptions` row per result, for subscriptions with the setting on | A consecutive-failure counter that concurrent exchanges must not lose | Yes: count from results on failure only (4i) |
  | 2026-10-08 | Idempotency keys | A delete of an expired same-key row, per call that sends the header | The unique index would refuse a stale key for good | Rare path; fold the expiry check into the insert later |
  | (gateway) | API key last used (`ApiKeyUses`) | One update per key per resolution window, rate-limited in memory | Deliberately throttled | Already respects the rule |

  The rule was never written down, so each feature made a reasonable local choice and nobody
  added up the per-exchange cost. It is recorded here as a design rule: **processing an
  exchange inserts; it does not update or delete. Anything that must be a counter runs on the
  failure path only, or off the hot path in a batch.**

- **Sub-fixes, each independent:**

  | Id | Change | Saves per exchange | Risk and compatibility |
  |---|---|---|---|
  | 4a | Outbox: after a successful publish, delete the row instead of updating it. Batch the deletes on a background channel flushed every second, so the save path makes no extra trip. The dispatcher's 10 s grace already covers the flush window. | 2 trips (5, 11) | Published rows stop being kept a day. The Queue health panel only shows waiting rows and the last error, so nothing visible is lost. Migration: none; old published rows are cleaned by the existing hourly job. |
  | 4b | Partial index on unpublished outbox rows (Postgres and SQL Server filtered index; MySQL plain index on `published_on, created_on`). | dispatcher scans | Additive migration. With 4a the table stays tiny, so this is a safety net. |
  | 4c | Skip the result-lane message when the cached subscription has no notifications and no active legacy notifier watches it. Decided at the result save. | 1 publish, 2 trips (12, 13), one consume | The `-Result` queue stays; it just carries fewer messages. A notification added between exchange start and result save is missed for that one exchange. |
  | 4d | Run the retry-budget release only when the subscription's policy has a group with a total budget. Needs the retry policy in the subscription cache. | 1 trip (9) for most subscriptions | None; same answer, computed only when it can matter. |
  | 4e | One query for trip 6 + 7: load the exchange with a "has result" flag. | 1 trip | None. |
  | 4f | Cache the API gateway with its partners and credentials in the configuration cache; revoke on save like subscriptions. | 1 to 2 trips (1, 2) | Key revocation reaches other pods through the revoke broadcast, as subscription changes do today; up to 10 minutes stale if the broadcast fails. |
  | 4g | `BusService`: read bus-enabled documents from the cache instead of querying per message. | 1 trip per bus message | None; documents are already cached. |
  | 4h | Cache the partner by id on the configuration cache for mapping context and enrichment. | 1 trip (8) | Partner edits reach other pods through the revoke broadcast. |
  | 4i | Auto-pause without a counter: on a failure result only, for subscriptions with the setting on, count failures since the last success from `XchangeResults` and pause with one conditional update when the threshold is crossed. No update on success. | 1 update per success on auto-pause subscriptions; removes the hot row | Needs an index on results by subscription and finished date (finding 9). Same pause behaviour. |

- **Target write model.** What matters for the database is round trips and index writes, not
  whether a statement says INSERT or UPDATE. In Postgres an update is a new row version plus a
  dead one, so it is never cheaper than an insert; the real value of the insert-only rule is no
  row locks, no lost-update logic and no hot rows. A second row written in the same statement
  batch as the exchange costs almost nothing; a second round trip costs a network hop and a
  commit. So the target is:

  | Moment | Write | Round trips | Reads |
  |---|---|---|---|
  | Creation | One `Xchanges` row carrying the promoted properties, the idempotency key and (for small payloads, finding 3) the payload itself; plus the outbox row in the same batch | 1 | configuration from cache; duplicate check only when the type has a window |
  | Completion | One `XchangeResults` row; plus an outbox row only when something will notify; plus a `DelayedRetries` row only on a failure the policy retries | 1 | one query that loads the exchange with its has-result flag |
  | Off the hot path | Outbox rows deleted in a batch once a second per node, after publisher confirms | 0 | |

  Two write round trips and one read per exchange, against about 14 trips today. Tables touched
  on the success path drop from four to three (`Xchanges`, `XchangeResults`, `OutboxMessages`).

  **Columns or side tables?** Two ways to get there, and the round-trip count is the same for
  both, because a row added in the same `SaveChanges` already rides in the same batch today.

  | | A. Fold into columns on `Xchanges` | B. Keep the 1:1 tables, insert them in the exchange's batch |
  |---|---|---|
  | Round trips | same | same |
  | Write cost per exchange | one heap row, one PK entry | plus one heap row, one PK entry and one FK check per side table; roughly 15 to 30 percent of the main insert each |
  | Reads: list, search, dashboard, archive | no join | one left join per side table on every page |
  | Retention | one delete per exchange batch | one delete per table per batch, in FK order |
  | Partitioning by date (finding 9) | natural: the row carries its date | awkward: each side table needs the date denormalised to be partitioned the same way, or stays unpartitioned and grows |
  | Migration risk now | additive columns, a coalesce in the search for two releases, then drop the table | none |
  | Schema | fewer tables | tables stay as they are |

  Recommendation: B is acceptable as a first pass, since it costs nothing to migrate and the
  write difference is small. A is where the schema should end up, and finding 9 forces it: a
  partitioned `Xchanges` with unpartitioned 1:1 side tables is the worst of both. So do the
  behaviour changes under B first (extraction at creation, key expiry by retention), and fold
  the columns in the same release as partitioning.

  Tables removed over time under A: `XchangePromotedProperties` (columns on `Xchanges`),
  `IdempotencyKeys` (a column and unique index on `Xchanges`; exchange retention becomes the key
  expiry), `XchangeDeliveries` (never written). `XchangeAggregations` can become a column later,
  set by the aggregation job, which is batch work. `RetryGroupUsages`, `DuplicateArrivals`,
  `XchangeNotifications` and `XchangeRetryClaims` stay: they are written on rare paths only.

- **Steps, each shippable on its own and backward compatible with the 10 live deployments:**

  | Id | Change | What it removes | Compatibility |
  |---|---|---|---|
  | 4a | Outbox rows are deleted in a batch off the hot path after publisher confirms, instead of updated inline. The insert rides in the same batch as the exchange. | 2 round trips; the outbox table stays tiny | Published rows are no longer kept a day. Queue health shows waiting rows and the last error only, so nothing visible changes. The dispatcher's 10 s grace covers the batch window. |
  | 4b | Partial index on unpublished outbox rows (filtered on Postgres and SQL Server, plain on MySQL). | dispatcher scans | Additive migration. |
  | 4c | The result-lane message is published only when the cached subscription has a notification or an active legacy notifier. | 1 publish, 1 consume, 2 trips | The `-Result` queue stays. A notification added while an exchange is in flight misses that one exchange. |
  | 4d | The retry-budget read runs only when the policy has a group with a total budget. Retry policy joins the subscription cache. | 1 trip per success | None. |
  | 4e | Load the exchange and its has-result flag in one query. | 1 trip | None. |
  | 4f | The API gateway, its partners and credentials come from the configuration cache, revoked on save. | 1 to 2 trips per gateway call | A revoked key can stay valid on another pod for up to 10 minutes if the revoke broadcast fails, the window subscriptions already have. |
  | 4g | The bus front door reads bus-enabled documents from the cache. | 1 trip per bus message | None. |
  | 4h | Partners come from the configuration cache. | 1 trip | Same staleness as 4f. |
  | 4i | Auto-pause without a counter and without a hot row. Each result row stores how many failures in a row it is. On a failure, read the subscription's latest result (one indexed read), store its count plus one, and pause with one conditional update when the threshold is crossed. On a success store zero and read nothing. | 1 update per success; the contended subscription row | Needs `subscription_id` denormalised onto `XchangeResults` with an index on (subscription, finished date), which finding 9 wants anyway. Same pause behaviour. |
  | 4j | Promoted properties are extracted at creation, where the payload is already in hand, and stored as columns on `Xchanges` (`promoted_properties` JSON, `promoted_raw` text for the substring search). The processing step stops inserting them. | 1 insert and 1 table on the hot path; one payload parse in the worker | Additive migration. The search reads the old table for rows older than the cutover (coalesce) for two releases, then the table is dropped. A payload that cannot be read at creation stores nothing and fails in processing as it does today. |
  | 4k | The idempotency key is a column on `Xchanges` with a unique index on (scope, key); the lookup is by that index. Expiry comes from exchange retention instead of a 24 h window, which keys generated as unique ids do not need. | 1 insert and 1 table | Additive migration. Deployments with retention 0 keep keys for ever, which only matters if a partner reuses keys; the API doc says not to. |

- **Expected effect:** with everything above, the sync path goes from about 14 trips to 3 and
  the async path from 8 to 3. No feature is lost; staleness windows are the ones subscriptions
  already have.

- **Plan for this finding** (decided 2026-10-14: tables stay as they are, statements change;
  no partitioning and no column folding; the empty table is dropped; backward compatible for
  data and API, with no precautions for mixed pod versions during a deployment). Four phases,
  each a release on its own, each measured before the next starts. None needs SW-Bus changes.

  | Phase | Steps | Touches | Schema |
  |---|---|---|---|
  | 0 | Baseline: a DB command counter per exchange (EF `DbCommandInterceptor` counting commands per scope), stage timings, and the k6 harness from finding 10 | `BitweenTelemetry`, `MetricsRecorder`, `XchangeService.Process`, a `tests/load` folder | one column on `ExchangeStats` |
  | 1 | 4e, 4g, 4d, 4h, 4f: reads folded or moved to the cache | `XchangeService`, `InMemoryBitweenCache` (retry policy, partners, and gateways with partners and credentials join the load and the revoke), `GatewayController`, `GatewayCallers`, `MappingContextFactory`, `BusService` | none |
  | 2 | 4a, 4b: outbox insert in batch, delete in batch, partial index. Drop `XchangeDeliveries` in the same migration. | `BitweenDbContext.PublishOutboxAsync`, a new `OutboxAcknowledger` background service (a bounded channel of ids, flushed every second or 500 ids), `OutboxDispatcher` unchanged, one migration | one index, one empty table dropped |
  | 3 | 4c, 4i: result event only when something notifies; auto-pause counted on the result rows | `XchangeService.Process` and `TrackDeliveryHealth`, `XchangeResult` gains `subscription_id` and `consecutive_failures`, one migration with an index on (subscription_id, finished_on) | two nullable columns, one index |
  | 4 | 4j, 4k with the tables as they are: promoted properties extracted at creation and inserted in the exchange's batch into `XchangePromotedProperties`; the worker inserts them only when the exchange has none (an exchange created by the previous version); idempotency key inserted optimistically, expiry handled on the unique violation instead of a delete per call | `XchangeService.CreateXchange` and `Process`, `IdempotencyGuard` | none |

  Dropped from the plan: option A (columns on `Xchanges`) and partitioning. The 1:1 tables stay
  and keep being written by insert, in the same batch as the exchange or the result. Finding 9
  is reduced to retention and composite indexes.

- **Cost of the phase 0 counter.** The interceptor is a hook EF calls around every command; the
  hook increments an integer on the current scope. That is nanoseconds against a millisecond
  round trip, and it is the same mechanism EF's own logging uses. Nothing is written to the
  database: the count is added to the in-memory `MetricsRecorder` with the other per-exchange
  numbers and flushed once a minute into the existing hourly `ExchangeStats` rows (one new
  column, a sum of commands), and exported as an OpenTelemetry histogram where a collector is
  set. Off by default outside the harness if preferred; the stage timings cost the same and
  go the same way.

- **How the gain is estimated and then measured.**

  Before anything ships, phase 0 gives three numbers per exchange on the harness: database
  commands, time per stage, and end-to-end latency at a fixed throughput. The estimate is then
  arithmetic, not opinion:

  - Latency: trips removed times the measured per-trip latency from the pod. On a managed
    Postgres in the same zone that is about 1 to 2 ms per trip, so 11 trips removed is 10 to 25
    ms per exchange. This finding is **not** the big latency win; findings 1, 2 and 3 are.
  - Throughput: database CPU per exchange drops in proportion to commands and rows written.
    This is where this finding pays: the ceiling before the database saturates roughly triples,
    and outbox churn (two updates and a day of dead rows per message) disappears, which keeps
    autovacuum and index bloat flat on a busy deployment.
  - Each phase is accepted against the harness: commands per exchange hit the target for that
    phase (14, then about 9, then 7, then 5, then 4 on the sync path; the fourth is the
    promoted-properties row, which now rides in the creation batch and costs no trip of its
    own), p95 does not regress, exchanges per second at one pod do not regress, and
    `pg_stat_statements` shows the removed statements gone. The same harness runs in CI after
    that.

- **Backward compatibility, against the 10 live deployments on all three providers.** Data and
  API compatible in every phase. Mixed pod versions during a deployment are not guarded, by
  decision; what happens in that window is listed so it is known, not so it is prevented.

  | Concern | Where | Handling |
  |---|---|---|
  | Index builds on large tables lock writes during a startup migration (`CREATE INDEX` on a table with tens of millions of rows takes minutes, and the startup probe allows five) | phases 2, 3 | Postgres: `CREATE INDEX CONCURRENTLY` in a migration with `suppressTransaction`; SQL Server: `ONLINE = ON` where the edition allows; MySQL: online DDL is the default for secondary indexes. Release notes state the expected duration. |
  | Dropping `XchangeDeliveries` | phase 2 | Empty on every deployment (never written). The migration's `Down` recreates it. |
  | Existing rows | phase 3, 4 | Old results have no `subscription_id` or `consecutive_failures`; they are simply not counted, and the first failure after the upgrade counts as 1. Exchanges created by the previous version have no promoted-properties row at creation; the worker inserts it as before when it finds none. |
  | API shapes | all | The exchange list, search, export and gateway responses do not change shape. Promoted properties and status come from the same fields. |
  | Idempotency semantics | phase 4 | Unchanged: the 24 h window stays; only the expiry delete moves from every call to the rare case where a stale key collides. |
  | Credential revocation staleness | phase 1, 4f | Same mechanism and window subscriptions have today: revoke broadcast on save, 10 minute expiry as the backstop. |
  | Notification added while an exchange is in flight | phase 3, 4c | That exchange sends none. Documented in `retries-and-alerts.md`. |
  | Published outbox rows no longer kept a day | phase 2 | Nothing reads them; Queue health shows waiting rows and the last error only. |
  | Audit trail | all | Runtime rows stay excluded from the audit; configuration saves are untouched. |
  | SW-Bus | all | Not touched by this finding. Publisher confirms (finding 7) make 4a stronger but are not required for it. |
  | During a deployment with mixed versions, for information only | phases 2, 3, 4 | Phase 2: old pods update outbox rows, new pods delete them, the dispatcher handles both. Phase 3: old pods keep the subscription counter, new pods count results; a pause can fire one or two failures late in that window. Phase 4: an exchange created by a new pod and processed by an old pod gets its promoted-properties row inserted twice; the old pod's save fails on the primary key, the message retries through the bus and lands on a new pod or the bad queue. Only during the minutes of the rollout. |

- **Decision (2026-10-14):** Accepted as the four-phase plan above. Tables stay; statements
  change; `XchangeDeliveries` is dropped; no partitioning, no column folding. 4a to 4k all in,
  including 4c and 4f with their staleness windows. Backward compatible for data and API; no
  precautions for mixed pod versions during a deployment.

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

### 7. One shared publisher channel, no publisher confirms

- **Status:** covered by finding 1's option C (RabbitMQ.Client 7, channel per scope, confirms, heartbeat). Quorum queues stay a separate later step.
- **Where:** SW-Bus `IServiceCollectionExtensions.AddBusPublish`, `BasicPublisher`
- **What happens:** One `IModel` is created at registration and handed to every scope, with no
  lock. `BasicPublish` returns when bytes are written; no `ConfirmSelect`. Messages are persistent
  (`DeliveryMode = 2`). No heartbeat is configured by Bitween (`HeartBeatTimeOut` default 0).
- **Why it hurts:** A single funnel for all publishes in a pod. The 6.x client does not promise
  a channel is safe across threads. A lost frame is a lost message the outbox cannot see.
- **Candidate fix:** RabbitMQ.Client 7, a channel per publisher or a small pool, publisher
  confirms, a 60 s heartbeat, quorum queues for the work lanes.
- **Expected effect:** publishing stops being a serialization point; "published" means the
  broker has it.
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
- **Candidate fix:** Non-zero default retention; monthly partitioning on Postgres for the big
  tables; composite indexes (subscription + started date, document + hash + started date,
  result success + finished date).
- **Expected effect:** flat query times over time; cheap removal of old months.
- **Decision:** (pending)

### 10. No stage timings and no load test

- **Status:** to discuss
- **Where:** `BitweenTelemetry`, `XchangeService.Process`; no harness in the repository
- **What happens:** One `exchange.process` span and one duration histogram per exchange.
  Nothing breaks the time down by stage, and nothing measures queue wait separately.
- **Why it hurts:** Every estimate in this document is unverified, and no change above can be
  proven to have paid off.
- **Candidate fix:** A span and histogram per stage (queue wait, storage read, filter, mapper,
  handler, save, publish); a k6 harness against the docker-compose stack run in CI with a
  throughput and latency budget.
- **Expected effect:** measured baseline before and after each change.
- **Decision:** (pending)

## Plan

Rebuilt from the decisions above once the findings have been discussed.

| Order | Finding | Change | Schema | Acceptance on the harness |
|---|---|---|---|---|
| 1 | 4, phase 0 | DB command counter per exchange, stage timings, k6 harness | one column on `ExchangeStats` | baseline recorded: commands, stage times, latency at fixed throughput |
| 2 | 4, phase 1 | Reads folded or moved to the cache (4e, 4g, 4d, 4h, 4f) | none | about 9 commands on the sync path, no regression |
| 3 | 4, phase 2 | Outbox insert in batch, delete in batch, partial index; drop `XchangeDeliveries` (4a, 4b) | one index, one table dropped | about 7 commands; outbox table stays under a few thousand rows under load |
| 4 | 4, phase 3 | Result event only when something notifies; auto-pause counted on results (4c, 4i) | two columns, one index on `XchangeResults` | about 5 commands; no update on the subscription row per exchange |
| 5 | 4, phase 4 | Promoted properties at creation in the exchange's batch; optimistic idempotency insert (4j, 4k) | none | about 4 commands; one payload parse in the worker |
| 6 | 1 and 7, part 1 | SW-Bus on RabbitMQ.Client 7: per-channel concurrency, channel per scope, publisher confirms, drain. **Done: 10.1.0 on nuget.org.** | none | 15 broker tests green; package published |
| 7 | 1 and 7, part 2 | Bitween on SW-Bus 10.1: work group Parallel exchanges, direct client use ported, readiness on the bus status, shutdown grace, docs and upgrade notes. **Done locally, not pushed** (`259c98c4`, `f19f7123`). | none (options JSON field) | measured: a 500 ms lane from 2/s to the offered 10/s per node, sync p95 557 ms; the Parallel column and the partner's own count are the evidence, not Queue health's in-flight |
| 8 | 3, phases 3.1 and 3.2 | Storage writes overlap the work before their save (3a, 3f). **Done locally** (183a1a6d). | none | the output write leaves the critical path; the input and response writes overlap only a few milliseconds, since overlapping the insert itself would need a transaction and its extra commit |
| 9 | 3, phase 3.3 | Payload travels with the created message under the cap (3e). **Done locally** (233ed43e). | none | measured: storage reads per sync exchange 2 to 1 (the carried copy is used only when it matches the row's hash) |
| 10 | 3, phases 3.5 and 3.6 | Response in the result signal, settings and docs. **Done locally** (25ec5f50). 3.4 skipped: nothing left for it to serve. | none | measured: storage calls per sync exchange 4 to 2 (2 PUT, 0 GET) |
| 11 | 2, phases 2.1 to 2.5 | Result signal: in-process bell, cross-pod broadcast, answer from the signal, poll as backstop (2a to 2d). **Done locally** (6535a5c3, 25ec5f50). | none | measured: sync p50 1011 ms to 115 ms with a 100 ms partner (15 ms of Bitween); 8.5 ms with an instant partner; database commands per sync exchange 4.03 to 3.03 |

Findings 5, 6, 8, 9 and 10 are added here as they are decided.

## Follow-ups from the implemented findings

| From | Follow-up | Status |
|---|---|---|
| 3.1 | A failed output write is now found after the handler has delivered, so the exchange fails and its retry policy may deliver again. Proposed instead: when the handler succeeded and only the output write failed, save the result as successful with no output file (`OutputSize` 0) and a warning on the result, since the delivery is the business fact and the rule "a saved row's files exist" still holds. | to decide |
| 3.2 | Bulk retry, unpause and aggregation start their input writes in parallel, up to 500 at once for a bulk retry. Bound them with a small limit (for example 32 in flight) so a bulk retry cannot flood storage or the pod's sockets. | to decide |
| 1 | Ceiling run above 10/s on the harness (expected about 24/s per node at 12 with a 500 ms partner), once Colima has disk. | owed |
| 4 | The k6 harness into CI with a throughput and latency budget. | finding 10 |
| 1 | The response and input writes still sit on the sync critical path (about two storage round trips on S3-class storage). Nothing cheaper than finding 3's rejected 3c removes them; accepted. | closed |

## Open questions

- Which SW-Bus version runs in production, and does its consumer dispatch differ from master?
- What payload sizes do the live deployments see (p50, p99)? Decides the inline-payload threshold.
- Which deployments run sync gateways, and what response time do their partners expect?
- How many subscriptions still run classic adapters?
