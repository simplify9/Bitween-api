# Provider Plan: RabbitMQ (full) and Kafka (connection & resource shape)

Companion to [external-brokers-architecture.md](external-brokers-architecture.md). Baseline
`origin/v2`. Numbers marked **(verify)** are library defaults to re-check against the exact
package version pinned at implementation time; they are stated because the design depends on
them, not because they should be trusted unread.

---

## Part 1 - RabbitMQ provider

### 1.1 Principle: model the broker, not SW.Bus

`SimplyWorks.Bus` is an *opinionated application bus* built on RabbitMQ: it owns naming, invents
`.retry`/`.bad` queues, routes by .NET message-type name, and assumes one exchange per
environment. Every one of those opinions is correct for a microservice bus and wrong for a
gateway into a **client's existing broker**, where the queue is called `ORDERS.INBOUND` because
someone decided that in 2014.

So `SW.Bitween.DataSources.RabbitMq` takes a dependency on `RabbitMQ.Client` **directly** and does
not reference `SW.Bus` at all. Concretely, what it deliberately does *not* inherit:

| SW.Bus behaviour | Provider behaviour |
|---|---|
| queue name `{env}.{app}.{consumer}.{messageType}` | the operator types the exact name; no prefix, no lowercasing, no derivation |
| auto-declares `.retry` + `.bad` queues per consumer | declares **nothing** unless topology says so; retries are `DelayedRetry` (§2.4 of the architecture doc) |
| routes on .NET type name → one exchange per environment | routing key, exchange, and headers are configuration; any exchange type |
| `AutomaticRecoveryEnabled` left on (client-side recovery) | **off** - the supervisor owns reconnection (§1.8) |
| publish = `IPublish.Publish(name, json)` | publish = exchange + routing key + properties + confirms (§1.6) |
| one connection for the whole app | one connection per `DataSource`, one channel per subscription (§1.9) |

### 1.2 Connection settings - mirror AMQP, plus pass-through

Field list follows `ConnectionFactory` / the AMQP URI spec rather than anything Bitween-shaped:

```jsonc
{
  "hosts": ["rabbit-1:5672", "rabbit-2:5672"],  // ordered; client tries each (cluster-aware)
  "virtualHost": "/",
  "username": "bitween", "password": "…",        // secret ⇒ SettingsProtector
  "authMechanism": "plain",                      // plain | external (mTLS) | (extensible)
  "tls": {
    "enabled": true, "serverName": "rabbit.client.com",
    "clientCertPath": null, "clientCertPassphrase": null,   // secret
    "acceptablePolicyErrors": [],                // explicit, never a blanket "trust all"
    "version": "Tls12,Tls13"
  },
  "requestedHeartbeatSeconds": 60,
  "requestedFrameMax": 0,                        // 0 = broker default (128 KiB)
  "requestedChannelMax": 2047,
  "connectionTimeoutMs": 30000,
  "clientProvidedName": "bitween/{node}/{dataSource}",  // shows in the management UI
  "managementUrl": null,                         // optional; enables Browse + lag reporting
  "clientProperties": { }                        // free-form pass-through
}
```

Two deliberate choices:

- **`clientProvidedName` is templated and defaults to something identifying.** When a client's
  DBA asks "what is this connection", the management UI must answer. Costs nothing, saves an
  incident call.
- **`acceptablePolicyErrors` is an explicit list, never a boolean.** "Ignore certificate errors"
  as a checkbox is how a self-signed cert quietly becomes a MITM-tolerant production connection.

### 1.3 Topology - mirror `definitions.json`, support everything

The topology model is a subset of RabbitMQ's own export format, so an operator can paste from
`rabbitmqadmin export` or the management UI's definitions file:

```jsonc
{
  "exchanges": [
    { "name": "orders", "type": "topic", "durable": true, "autoDelete": false,
      "internal": false, "arguments": { "alternate-exchange": "orders.unrouted" } }
  ],
  "queues": [
    { "name": "ORDERS.INBOUND", "durable": true, "exclusive": false, "autoDelete": false,
      "arguments": {
        "x-queue-type": "quorum",            // classic | quorum | stream - all allowed
        "x-dead-letter-exchange": "orders.dlx",
        "x-dead-letter-routing-key": "inbound.failed",
        "x-max-length": 100000, "x-overflow": "reject-publish",
        "x-message-ttl": 86400000, "x-max-priority": 10,
        "x-single-active-consumer": true,
        "x-quorum-initial-group-size": 3
      } }
  ],
  "bindings": [
    { "source": "orders", "destination": "ORDERS.INBOUND", "destinationType": "queue",
      "routingKey": "order.created.*", "arguments": {} },
    { "source": "orders", "destination": "orders.audit", "destinationType": "exchange",
      "routingKey": "#", "arguments": {} }
  ]
}
```

Non-negotiables for "not opinionated":

- **All exchange types, including plugin ones.** `direct`, `fanout`, `topic`, `headers`, plus
  `x-consistent-hash`, `x-delayed-message`, `x-random`, `x-modulus-hash` - the type is a
  **free-text string**, not an enum, because a plugin can define one we've never heard of. Validate
  by attempting the declare and surfacing the broker's error, not by rejecting unknown strings.
- **`arguments` is an open map**, passed through verbatim with correct AMQP type coercion
  (integers as `long`, `x-match` as string, nested tables). Never a fixed field list - the
  `x-*` argument space grows every RabbitMQ release.
- **Exchange-to-exchange bindings** (`destinationType: "exchange"`) are first class. Every
  hand-rolled integration forgets these and then can't model a real client topology.
- **Headers exchanges** need `x-match: all|any` plus arbitrary header keys in binding arguments -
  which falls out of the open-map rule.

### 1.4 Declare mode - the field that decides whether this works at clients

```
"declareMode": "none" | "assert" | "create"
```

| Mode | Behaviour | When |
|---|---|---|
| `none` | touch nothing; just consume/publish | Bitween has only `read`/`write` on the vhost - **the common case at a client** |
| `assert` | `QueueDeclarePassive` / `ExchangeDeclarePassive`; fail fast with a clear error if missing | verify the client actually created what they promised, without needing `configure` |
| `create` | full declare + bind, idempotent | Bitween owns the topology |

This single field is what makes the provider usable against brokers we don't administer. A
provider that always declares will fail with `ACCESS_REFUSED` at exactly the wrong moment - and
worse, a declare with mismatched arguments returns `PRECONDITION_FAILED` **and kills the
channel**, which is an easy way to look broken while being correct.

### 1.5 Inbound binding

```jsonc
{
  "queue": "ORDERS.INBOUND",
  "consumerTag": "",                  // "" ⇒ broker-generated
  "prefetchCount": 20,                // REQUIRED (§1.10) - the memory bound
  "prefetchSize": 0,
  "exclusive": false,                 // exclusive consumer (≠ exclusive queue)
  "arguments": { "x-priority": 5, "x-stream-offset": "last" },
  "noAck": false,                     // exposed, defaulted false, warned in UI
  "contentEncoding": null             // optional override when the publisher lies
}
```

Notes that matter: **prefetch is per-channel**, so one channel per subscription is required to
give each its own prefetch (§1.9). `x-stream-offset` is how stream queues are consumed -
supported by virtue of `arguments` being open. `noAck: true` is exposed because some telemetry
feeds genuinely want it, but it breaks the persist-then-ack guarantee, so the UI must say so.

### 1.6 Outbound binding

```jsonc
{
  "exchange": "orders",               // "" ⇒ default exchange (publish straight to a queue)
  "routingKey": "order.created.{{partner.region}}",   // token-templated (§11.5)
  "mandatory": false,                 // true ⇒ surface basic.return as a publish failure
  "confirms": true,                   // publisher confirms; wait per-publish or per-batch
  "confirmTimeoutMs": 5000,
  "properties": {
    "deliveryMode": 2,                // 1 transient | 2 persistent
    "priority": null, "expiration": null, "contentType": "application/json",
    "headers": { "x-tenant": "{{partner.code}}" }
  }
}
```

`mandatory` + `confirms` together are the only way to know a publish landed. Without them a
publish to a nonexistent routing key succeeds silently - a failure mode that looks exactly like
success in every log. Default `confirms: true` for egress, and treat a nack or a
`basic.return` as a transport failure feeding the outbox retry, not an `Xchange` error.

### 1.7 Capabilities

```csharp
new ProviderCapabilities(
    CanManageTopology: true, CanBrowseTopology: true,   // Browse requires managementUrl
    SupportsHeaders: true, SupportsOrderingKey: false,  // routing key ≠ ordering key
    SupportsPartitions: false,
    SupportsNack: true, SupportsRequeueDelay: false,    // no native delay without the plugin
    SupportsNativeDeadLetter: true,                     // x-dead-letter-exchange
    SupportsExclusiveConsumer: true,                    // exclusive consumer / x-single-active-consumer
    SupportsCompetingConsumers: true,
    SupportsTransactions: false,                        // tx.* is slow; use confirms instead
    SupportsBatch: true, MaxMessageBytes: 134_217_728);
```

`SupportsRequeueDelay` becomes `true` when the `rabbitmq_delayed_message_exchange` plugin is
detected - SW.Bus already probes for it (`DelayedPluginAvailable`), so reuse the technique:
probe once at connect, cache on the connection, report through capabilities. Capabilities are
therefore **per connection**, not per provider type - worth making the contract return them from
`IDataSourceConnection`, not only `IDataSourceProvider.Describe()`.

### 1.8 Reconnection: the supervisor owns it

Set `AutomaticRecoveryEnabled = false` and `TopologyRecoveryEnabled = false`. Reasons:

1. Two recovery mechanisms fight. The supervisor already reconciles on fault (§5), and
   client-side recovery silently re-declares topology that `declareMode: none` says not to touch.
2. Client-side recovery hides state: `IMessageSubscription.State` and reconnect counts (§10.3)
   would be lies.
3. Recovery policy belongs with placement - on a `Leader`-placed subscription, a dropped
   connection may mean *another node should take over*, not that this one should reconnect.

Instead: `ConnectionShutdown` / `CallbackException` / consumer `Shutdown` → mark `Degraded`,
raise `Faulted`, let the supervisor back off (exponential, jittered, capped) and reconcile.

### 1.9 Connection and channel topology inside the provider

- **One `IConnection` per `DataSource`.** Not per subscription - a connection is the expensive
  object; channels are cheap.
- **One `IModel`/`IChannel` per subscription.** Required for per-subscription prefetch, and
  channels are *not* thread-safe, so sharing one across consumers is a data race waiting for load.
- **One dedicated publish channel per (connection, endpoint) for egress**, because
  `confirm_select` is a channel-level mode and mixing confirmed publishes with consumer acks on
  one channel makes the confirm bookkeeping ambiguous.
- Never publish from a consumer's channel.

### 1.10 Resource footprint - RabbitMQ

**The dominant term is unacknowledged messages, and it is entirely under our control:**

```
peak inbound memory ≈ Σ over subscriptions ( prefetchCount × avg message size )
```

A prefetch of 500 on a queue of 2 MB payloads is 1 GB of managed memory on one node, from one
config field. Hence: **`prefetchCount` is a required field in the descriptor with a sane default
(10-20) and an explicit upper bound**, and the UI shows the computed worst case
(`prefetch × observed **max** size`) next to it. That single affordance prevents most of the
plausible OOMs.

Calibrate against the measured baseline in §5.2 of the architecture doc: real payloads run ~5 KB
average with a ~1 MB maximum, and ingest acks in ~330 ms. At those sizes prefetch is cheap
(20 × 5 KB = 100 KB) and the bound should be set by the **maximum** payload, not the average -
prefetch 500 × 1 MB is 500 MB, prefetch 20 × 1 MB is 20 MB. Don't be timid with prefetch for
ingest; the downstream work-group queue is the shock absorber, not the broker.

Per-connection and per-channel costs (**verify** against the pinned version):

| Item | Rough cost | Notes |
|---|---|---|
| `IConnection` | tens of KB managed + socket buffers; frame buffers scale with negotiated `frame_max` (128 KiB default) | broker side also pays ~100 KB+ per connection |
| TLS | + `SslStream` buffers, order of 32-64 KB per connection | plus handshake CPU at connect/reconnect |
| `IModel` per subscription | small - hundreds of bytes to low KB | not a scaling concern |
| **Threads (client 6.8.1)** | **one dedicated socket read loop per connection**, plus a heartbeat timer; consumer callbacks dispatched on the async work service | 25 data sources ⇒ ~25 dedicated threads before any work happens |

CPU is negligible at rest - heartbeats every 60 s and frame parsing proportional to throughput.
The two real CPU events are TLS handshakes during reconnect storms (bounded by the backoff in
§1.8) and JSON deserialization in Bitween's own pipeline, which dwarfs the AMQP cost.

**Client version decision.** SW.Bus pins `RabbitMQ.Client 6.8.1` (`IModel`,
`DispatchConsumersAsync`). The provider loads in its **own `AssemblyLoadContext`** (§4.1), so it
can independently target **`RabbitMQ.Client` 7.x** - `IChannel`, fully async, task-based I/O
instead of a thread per connection, and a better allocation profile. Two managed versions
coexisting in one process is exactly what the ALC isolation buys, and there is no native
dependency to conflict. **Recommendation: build the provider on 7.x** and treat the thread
savings as the headline reason. If 7.x proves troublesome, 6.8.1 works - but then budget one
thread per data source and lower `MaxDataSourceConnectionsPerNode` accordingly.

### 1.11 Test-connection stages

Map to the staged result from §11.2 - each stage is a distinct RabbitMQ failure the operator
fixes differently:

| Stage | Check |
|---|---|
| `dns` / `tcp` | resolve + connect to each host in `hosts`, report which succeeded |
| `tls` | handshake; report cert subject, expiry, and the specific policy error |
| `auth` | `CreateConnection` - distinguish `ACCESS_REFUSED` (credentials) from vhost-not-found |
| `authorize` | probe the three permissions separately: `read` (passive-declare the queue), `write` (publish to a nonexistent routing key on a topic exchange with `mandatory:false`), `configure` (declare a temporary auto-delete queue, then delete) - and report which are held. This is what tells an operator that `declareMode: create` will fail *before* they save. |
| `topology` | `assert`/`create` dry run: passive-declare everything the endpoint references |

Also report negotiated `frame_max`, `channel_max`, server version, and detected plugins
(delayed message, consistent hash) - all cheap, all useful, all invisible otherwise.

### 1.12 Cluster discovery via the management plugin

**Difficulty: low, and the payoff is the best demo surface in the whole feature.** The management
plugin is a plain HTTP+JSON API (`:15672`, `:15671` for TLS) with HTTP basic auth. A typed client
over the eight endpoints below is roughly a day's work; the interesting design is entirely in
degradation and scale, not in the calls.

Endpoints that carry the whole experience (**verify** shapes against the cluster's version - the
management API is stable but has grown fields across 3.8 → 4.x):

| Endpoint | What it unlocks |
|---|---|
| `GET /api/overview` | cluster name, RabbitMQ + Erlang version, **`exchange_types`** (see below), listeners, rates mode |
| `GET /api/nodes` | per-node memory/disk alarms, fd/socket usage, partition status - "is this cluster healthy" |
| `GET /api/vhosts` | vhost picker instead of a free-text field |
| `GET /api/exchanges/{vhost}` · `GET /api/queues/{vhost}` | the browsable topology, with live depth/rates per queue |
| `GET /api/bindings/{vhost}` | the routing graph (and the routing preview, below) |
| `GET /api/whoami` + `GET /api/permissions/{vhost}/{user}` | the configure/write/read regex triple for *this* user |
| `GET /api/definitions/{vhost}` | the **entire topology in the exact shape §1.3 already models** |
| `GET /api/aliveness-test/{vhost}` | broker-side end-to-end check for the health panel |

#### The five features worth building

1. **Build the exchange-type dropdown from the live cluster.** `/api/overview` returns
   `exchange_types` - what *this* broker actually supports, including plugin types like
   `x-consistent-hash` and `x-delayed-message`. This is the perfect answer to "don't be
   opinionated": no hardcoded list, no guessing about plugins, and it self-updates when the
   client installs a plugin. Same trick replaces the separate delayed-plugin probe in §1.7.

2. **Import instead of retype.** `GET /api/definitions/{vhost}` returns exchanges, queues, and
   bindings in the structure §1.3 deliberately mirrors - so "select these three queues and two
   exchanges → save as this endpoint's topology" is a filter over a JSON document, not a
   translation layer. This is the single biggest reason the definitions-shaped topology model was
   the right choice.

3. **Drift and conflict detection - the highest-value item.** With live definitions, the
   `TopologyDiff` from `ITopologyManager.Plan` stops being a guess: show *exactly* what `Apply`
   would create, what already matches, and - critically - **what exists with different
   arguments**. Argument mismatch is the failure that returns `PRECONDITION_FAILED` and **kills
   the channel** (§1.4); catching it in a diff before saving turns the nastiest RabbitMQ failure
   mode into a UI warning.

4. **Preflight warnings computed from real numbers, not guesses.** `GET /api/queues/{vhost}/{name}`
   returns `messages`, `message_bytes`, `consumers`, `consumer_details`, `consumer_utilisation`,
   and rates. Two warnings fall straight out:
   - **Memory:** average message size is `message_bytes / messages`, so the prefetch estimate in
     §1.10 becomes `prefetchCount × actual observed average` instead of a hand-waved number.
   - **Competing consumers:** if the queue already has consumers, say so plainly - Bitween will
     compete with them for messages, or be silently starved if the queue has
     `x-single-active-consumer`. This is a real trap that is invisible without discovery, and
     "why does Bitween only get half the messages" is otherwise a day of debugging.

5. **Routing preview, computed locally.** There is no route-simulation endpoint, but with the
   bindings in hand it is ~40 lines: for `direct` compare routing keys, for `topic` match `*`/`#`,
   for `fanout` take all. Then the outbound form can say *"routing key `order.created.jo` reaches
   `ORDERS.INBOUND`, `ORDERS.AUDIT`"* - or, far more usefully, **"reaches no queue; this message
   will be silently discarded"**, which is exactly the failure `mandatory`/confirms exists to
   catch (§1.6). Be honest about the limit: `headers`, `x-consistent-hash`, and other plugin
   exchanges can't be simulated - show "cannot preview for this exchange type" rather than a
   wrong answer.

#### Optional, high value, needs a policy decision

`POST /api/queues/{vhost}/{name}/get` peeks messages without consuming (with
`ackmode=reject_requeue_true`). That would let the Information-type screen **seed promoted
properties from a real message** - a genuinely great onboarding flow. But it reads the client's
production payloads into Bitween's UI, so it needs: an explicit permission, an audit entry, a
`count=1` cap, requeue mode forced, and never running automatically on page load. Worth building;
worth not building casually.

#### What makes it hard (none of it is the code)

1. **Management access is a different grant from AMQP access.** The HTTP API requires a user with
   a management *tag* (`monitoring` / `management` / `administrator`); AMQP `read`/`write`/
   `configure` permissions grant nothing there. Many clients will hand over AMQP credentials and
   no management user at all. So: `managementUrl` stays **optional** (§1.2),
   `CanBrowseTopology` is a **per-connection** capability, and every discovery feature degrades to
   the AMQP-only path - you can't *list*, but `declareMode: assert` can still *verify* a name via
   passive declare. Design the UI so discovery is an accelerator, never a prerequisite.
2. **The HTTP port is a separate firewall hole.** 5672 open does not imply 15672 open. Report this
   as its own test-connection stage so "discovery unavailable" is distinguishable from "wrong
   password".
3. **Scale.** `GET /api/queues` on a cluster with thousands of queues returns megabytes and costs
   the broker real work; `/api/bindings` is worse. Use the list endpoints' pagination and column
   projection (`page`, `page_size`, `name`, `use_regex`, `columns=`) from the first commit, never
   fetch-all-then-filter-in-JS, and always scope by vhost. Also honour the user's `read` regex
   when presenting results, so the picker doesn't offer queues they can't consume.
4. **Stats can be absent.** Metrics collection can be disabled or lag on a busy cluster; treat
   depth/rate fields as nullable and show "unavailable" instead of `0`, which reads as "empty
   queue" and is a much worse lie.
5. **Where it runs.** Discovery must execute on a node that can reach the management port - same
   argument as test-connection in §11.2 of the architecture doc. Route it through the supervisor,
   not the API node that happens to serve the request.

#### Sizing

| Piece | Effort |
|---|---|
| Typed management client (8 endpoints, pagination, auth, timeouts) | ~1 day |
| Browser UI: vhost → exchanges/queues tree, live columns, "use as endpoint" | 2-3 days |
| Definitions import + diff/drift panel | 1-2 days |
| Routing preview + preflight warnings | ~1 day |
| Message peek (incl. permission + audit) | ~1 day |

Roughly **a week and a half on top of the provider**, entirely additive - every piece can ship
after the provider works, and the provider is fully functional with none of it. Note the v2 UI has
no graph/diagram library (only `lucide-react` icons), so render the binding graph as an indented
list or a small hand-rolled SVG rather than adding a dependency for it.

### 1.13 Build order

| Step | Deliverable |
|---|---|
| 1 | Project `SW.Bitween.DataSources.RabbitMq`, `IDataSourceProvider` + descriptor (connection fields, topology schema, inbound/outbound binding), no I/O |
| 2 | `Connect` + staged health check (§1.11) + capability probe (plugins, server version) |
| 3 | `Subscribe`: channel per subscription, prefetch, `AsyncEventingBasicConsumer` (or 7.x async consumer) → `InboundMessage`; ack/nack from `AckDecision`; `ProviderMetadata` carries deliveryTag, exchange, routingKey, redelivered, headers |
| 4 | Fault plumbing: shutdown/callback events → `Faulted` + `State`, no client auto-recovery |
| 5 | `ITopologyManager`: `Plan` (diff against live definitions when the management API is available, passive declares otherwise), `Apply` (idempotent), `Browse` (management API when configured) |
| 6 | `Publish`: dedicated channel, confirms, `mandatory` + `basic.return`, templated routing key and headers |
| 7 | Integration tests (Testcontainers RabbitMQ, with and without the delayed plugin - SW.Bus's `PluginAvailableTests`/`PluginUnavailableTests` are the pattern): all four core exchange types + consistent-hash, e2e binding, quorum and stream queues, `declareMode` × permission matrix, `PRECONDITION_FAILED` on argument mismatch, kill-the-broker reconnect, prefetch honoured |

---

## Part 2 - Kafka provider: connection and resource shape

Deliberately scoped to what the user asked: what is hard about *connecting*, and what it costs in
memory and CPU. Semantics (offsets, groups, ordering) are covered in the architecture doc.

### 2.1 Configuration: pass-through is the correct answer

Do **not** model librdkafka's ~200 properties as fields. First-class the handful Bitween's
behaviour depends on, and expose the rest as a validated pass-through map:

```jsonc
{
  "bootstrapServers": "b1:9092,b2:9092",
  "securityProtocol": "sasl_ssl",         // plaintext | ssl | sasl_plaintext | sasl_ssl
  "saslMechanism": "scram-sha-512",       // plain | scram-sha-256/512 | gssapi | oauthbearer
  "saslUsername": "…", "saslPassword": "…",   // secret
  "ssl": { "caLocation": null, "certificateLocation": null, "keyLocation": null,
           "keyPassword": null, "endpointIdentificationAlgorithm": "https" },
  "clientId": "bitween-{node}",
  "config": { "socket.keepalive.enable": "true", "…": "…" }   // free-form librdkafka pass-through
}
```

Guard the pass-through with a **deny list**, not an allow list: reject keys that would break the
ack model (`enable.auto.commit` - Bitween commits after persistence) and keys the provider owns
(`group.id`, `bootstrap.servers`, anything in the first-class set). Everything else goes through,
because the next client will need `sasl.oauthbearer.token.endpoint.url` or a broker-version
override and should not need a Bitween release to get it.

Event Hubs note: its Kafka endpoint is `sasl_ssl` + `PLAIN` with username `$ConnectionString`
and the connection string as password. That is a *configuration* of this provider, which is the
whole point of the §8.2 sequencing - try it before writing an Event Hubs provider.

### 2.2 Connection challenges

1. **A "connection" is not a connection.** `Confluent.Kafka` wraps native **librdkafka**; a
   consumer handle opens a socket to *every* broker it learns about, not just the bootstrap. Your
   connection count is a function of the client's cluster size, which you don't control.
2. **Failures are asynchronous and log-shaped.** There is no `Connect()` to await. A wrong
   password surfaces as an error *event* (`ErrorCode.SaslAuthenticationFailed`) some time after
   construction. So test-connection must be implemented as **`AdminClient.GetMetadata(timeout)`**
   with an error-handler subscription, not as "did the constructor throw". Budget a real timeout
   (10 s) and treat "no metadata yet" as failure with the collected error text.
3. **Handle disposal is slow.** Closing a consumer performs a leave-group round trip and can take
   seconds. The supervisor's reconcile must dispose off the critical path, or a config save
   appears to hang.
4. **Rebalances make reconcile expensive** - and this is the real interaction with the cluster
   design. Every stop/start of a Kafka subscription triggers a consumer-group rebalance that
   pauses *all* members. A flapping node or an over-eager reconcile becomes a rebalance storm.
   Mitigations, all cheap, all needed:
   - **debounce reconcile per data source** (e.g. coalesce for 5-10 s) instead of acting on every
     broadcast;
   - set **`group.instance.id`** from the stable `NodeId` (static group membership) so a restart
     inside `session.timeout.ms` does **not** rebalance;
   - prefer `partition.assignment.strategy=cooperative-sticky` so a join doesn't stop the world;
   - **`Placement = All` is the right default for Kafka** - the consumer group already does the
     distribution, and forcing `Leader` both wastes nodes and makes every leadership change a
     rebalance.
5. **One handle per (data source, group), not per topic.** A single consumer can subscribe to
   many topics; a handle per topic multiplies threads and sockets for nothing.

### 2.3 Memory - the headline risk

librdkafka prefetches **per partition**, and the defaults are large (**verify** against the pinned
version):

| Property | Default | Meaning |
|---|---|---|
| `queued.max.messages.kbytes` | 65536 (**64 MiB**) | **per partition** local queue cap |
| `queued.min.messages` | 100000 | messages to keep queued per partition |
| `fetch.message.max.bytes` | 1048576 (1 MiB) | per-partition fetch request size |
| `fetch.max.bytes` | 52428800 (50 MiB) | per fetch response, across partitions |
| `receive.message.max.bytes` | 100000000 | hard per-response ceiling |

Worst case on defaults: **partitions × 64 MiB**. A 30-partition topic is ~2 GB of prefetch buffer
for one subscription - on a node whose container limit might be 1 GB. This is not a hypothetical;
it is the default configuration.

So: **`queued.max.messages.kbytes` and `queued.min.messages` are required descriptor fields with
low defaults** (start at 1024-4096 KiB and 1000), and the UI must show
`partitions × queued.max.messages.kbytes` as the reserved worst case. The provider should query
partition count at subscribe time and refuse - or loudly warn - when the product exceeds a
configured share of the node's `ContainerMemoryLimitBytes` (§10.4).

**And it is native memory.** librdkafka allocates outside the .NET heap, so
`GC.GetTotalMemory()` and Gen2 counts show nothing while the process grows.
`Process.WorkingSet64` is the only metric in §10.2 that catches Kafka pressure - worth calling
out explicitly on the Nodes page, or an operator will conclude the node is healthy right up to
the OOM kill.

### 2.4 CPU and threads

- **Threads per consumer handle:** 1 main/coordinator thread + **1 per broker connection** +
  internal timers. A 3-broker cluster ⇒ roughly 5 native threads *per handle*. Ten Kafka
  subscriptions sharing one handle: still ~5. Ten separate handles: ~50. This is the argument for
  §2.2 item 5, in numbers.
- **These are native threads, not thread-pool threads.** Decompression (lz4/snappy/zstd) and CRC
  validation happen on them, so Kafka CPU load is **invisible to `ThreadPoolQueueLength`** -
  the one §10.2 metric that catches RabbitMQ-side starvation. Process CPU% is the signal for
  Kafka nodes. Both metrics are needed; neither is sufficient alone.
- Compression is the main steady-state CPU cost. `zstd` on a busy topic is materially more
  expensive than `lz4`; expose `compression.codec` for egress and default to `lz4`.
- Practical budget: assume a Kafka data source costs roughly **an order of magnitude more
  memory and several times more threads** than a RabbitMQ one. `MaxDataSourceConnectionsPerNode`
  (§10.4) should therefore be **weighted per provider**, not a flat count - a descriptor-declared
  `ResourceWeight` (rabbitmq: 1, kafka: 8, pulsar: 6) that the supervisor sums against a budget.

### 2.5 Discovery, the Kafka equivalent - different in kind

Since Kafka is the less familiar one, the plain-language version: **Kafka has no management plugin
and needs none.** The equivalent of RabbitMQ's HTTP admin API is the **Admin API carried over the
same binary protocol on the same port with the same credentials** (`AdminClient` in
`Confluent.Kafka`). That is strictly *easier* than RabbitMQ: no second port, no second firewall
hole, no separate management user, no pagination.

What you can discover, and what it maps to:

| Kafka call | Gives you | RabbitMQ analogue |
|---|---|---|
| `GetMetadata` | brokers, cluster id, **topics with partition counts**, replicas, leaders | `/api/overview` + `/api/queues` |
| `DescribeConfigs` | per-topic config: retention, `cleanup.policy`, `min.insync.replicas` | queue `arguments` |
| `ListConsumerGroups` / `DescribeConsumerGroups` | existing groups, their members and assignments | queue `consumer_details` |
| `ListConsumerGroupOffsets` + `QueryWatermarkOffsets` | committed offset vs high watermark ⇒ **lag** | queue depth |
| `DescribeCluster` | controller, broker endpoints | `/api/nodes` |
| `DescribeAcls` | permissions, when the user may read them | `/api/permissions` |

**The structural difference to be clear about: there is nothing to browse.** Kafka has no
exchanges, no bindings, and no routing - a topic is a flat log, and "routing" is just the
producer's choice of topic plus a partition key. So the binding graph, the routing preview, and
the definitions import have **no Kafka analogue at all**. The equivalent smart view is a flatter
thing: topics × partitions × lag × retention, plus which consumer groups already read each topic.

Two Kafka-specific warnings that are worth as much as the RabbitMQ ones:

1. **Group-id collision.** In RabbitMQ, an existing consumer on the queue means Bitween *competes*
   for messages. The Kafka mirror is sharper: consumer groups are independent, so reading a topic
   another group already reads is harmless - but reusing a **`group.id` someone else is using**
   silently steals their partitions. `ListConsumerGroups` makes this checkable at configuration
   time, and it should be a hard validation warning.
2. **Topic auto-creation.** If the broker allows it and `allow.auto.create.topics` is true, a typo
   in a topic name silently *creates* an empty topic and the subscription sits there consuming
   nothing forever. Default that setting to **false** in the provider and validate the topic
   exists via metadata instead.

Effort: **~2 days** for the discovery client plus a topics/groups/lag panel - less work than
RabbitMQ's, with less to show, because there is genuinely less structure in Kafka to reveal.

### 2.6 Minimal build order (see also Part 3 for the test environment)

| Step | Deliverable |
|---|---|
| 1 | Descriptor + config validation (first-class fields, deny-listed pass-through), no I/O |
| 2 | `AdminClient.GetMetadata` staged health check with error-event capture; report cluster id, broker count, topic/partition counts |
| 3 | Consumer handle per (data source, group); subscribe many topics; manual commit after persistence; `ProviderMetadata` = topic, partition, offset, timestamp, headers |
| 4 | Resource guards: required queue-size fields, partition-count × buffer preflight against the node budget, `ResourceWeight` |
| 5 | Reconcile hygiene: debounce, `group.instance.id` from `NodeId`, cooperative-sticky, async disposal |
| 6 | Lag reporting for §10.3 via committed vs high-watermark |
| 7 | Producer for egress: `lz4`, `acks=all`, `enable.idempotence=true`, delivery-report callbacks feeding the outbox |
| 8 | Integration tests on Redpanda (fast, Kafka-protocol compatible): consume/commit/restart-resume, rebalance on second node, buffer cap honoured, Event Hubs config path validated against a real namespace |

---

## Part 3 - Local dev and integration testing

### 3.1 The actual CI situation

CI is **GitHub Actions** (`.github/workflows/bitween-api-cicd-gateway.yml`), delegating to the
org-wide reusable workflow `simplify9/.github/.github/workflows/reusable-service-cicd.yml@main`.
`azure-pipelines.yml` is not the active pipeline. The repo is **public**, so GitHub-hosted
`ubuntu-latest` gives **4 vCPU / 16 GB RAM / 14 GB SSD** - roughly double a private-repo runner,
and generous for this workload. Resource pressure is therefore *not* the deciding factor;
**startup seconds are**, because they are paid on every run.

**Two findings that matter more than any container sizing:**

1. **Integration tests do not run in CI today.** The workflow passes
   `test-projects: 'SW.Bitween.UnitTests/SW.Bitween.UnitTests.csproj'` - `SW.Bitween.IntegrationTests`
   is never executed by any pipeline. Every container-cost estimate below is therefore
   *hypothetical* until someone decides to run them.
2. **There is no PR gate.** Triggers are `push` on `releases/**` plus `workflow_dispatch`, so
   pushes to `v2` - the branch this feature is being built on - run nothing at all.

So the real decision is not "which Kafka image" but **"do we add a PR workflow that runs the
integration suite?"** Recommendation: add a separate `pr-tests.yml` on `pull_request` +
`push: [v2]` running unit **and** integration tests. Without it, every broker provider ships with
tests that only ever run on a developer's laptop - which for a feature whose whole risk surface is
live connections and reconnection behaviour is the wrong trade. The reusable release workflow
should stay as-is; this is an additive, low-risk second workflow.

Today's suite (`SW.Bitween.IntegrationTests/Fixtures/BitweenFixture.cs:33-34`) starts
`PostgreSqlBuilder` + `RabbitMqBuilder` once per collection (`[CollectionDefinition("Bitween")]`),
so the pattern for adding brokers already exists and already scales the right way.

Side note, relevant to §1.12: the deploy step already injects
`Bitween__RabbitMqManagementUrl` / `Username` / `Password` as secrets, so **a management-API
connection is already a first-class configured dependency in production** (it backs the `Ops`
queue-health pages). The `DataSource` design simply moves that from one per install to one per
connection.

### 3.2 Is Kafka dockerized? Yes - and there are lighter protocol-compatible options

| Option | Image | RSS (approx) | Cold start | Verdict |
|---|---|---|---|---|
| **Redpanda** (C++, no JVM, no ZK) | `redpandadata/redpanda` | **~150-250 MB** tuned | **~3-5 s** | **Recommended for CI.** Kafka-protocol compatible, single binary |
| Apache Kafka, KRaft mode (JVM) | `apache/kafka` | ~600-800 MB with `-Xmx512m` | ~15-25 s | Most faithful reference. Keep as an opt-in "conformance" job |
| Apache Kafka native image | `apache/kafka-native` | ~150-250 MB | ~1-3 s | Very promising - GraalVM build, dev/test targeted. **(verify maturity for the version you pin)** |
| Confluent | `confluentinc/cp-kafka` / `confluent-local` | ~800 MB-1 GB | ~20-30 s | Heaviest; no advantage here |
| Azure Event Hubs emulator | `mcr.microsoft.com/azure-messaging/eventhubs-emulator` | ~1-1.5 GB (**needs an Azure SQL Edge sidecar**) | ~30 s+ | **Don't use for CI.** Two containers, and **Kafka-endpoint support is a documented limitation** (verify against the current release) |

**Kafka no longer needs ZooKeeper.** KRaft mode has been production-ready since 3.3, so any modern
image is a single container - the two-container Kafka+ZK setup people remember is obsolete.

**Redpanda needs explicit flags or it will eat the agent.** It is thread-per-core by design and
defaults to claiming all cores and a large memory reservation, which even on a 4-vCPU runner
starves the test host. Non-negotiable CI flags:

```
--overprovisioned --smp 1 --memory 512M --reserve-memory 0M --node-id 0 --check=false
```

`Testcontainers.Redpanda` sets sane defaults, but verify these are applied - this single item is
the difference between a 4-second start and a pegged agent.

**Event Hubs strategy (consistent with §8.2 of the architecture doc):** test the *protocol* against
Redpanda, and validate the Event Hubs **configuration path** once against a real namespace as a
manual/nightly step. Do not try to emulate Event Hubs in the PR suite.

### 3.3 How the community handles this

**Testcontainers is the answer, and it is what you already do.** Java, .NET, Go, and Node all
converge on it; the .NET modules `Testcontainers.Redpanda` and `Testcontainers.Kafka` exist
alongside the `PostgreSql`/`RabbitMq` ones already in use (bump all Testcontainers packages
together - the repo is on 3.10.0).

Worth knowing: **.NET has no embedded Kafka.** Java has `spring-kafka-test` /
`EmbeddedKafkaCluster` for in-process brokers; there is no equivalent for .NET, so a container is
the only honest option. That is a real difference from RabbitMQ testing, where SW.Bus's existing
suite already proves the container path is fine.

The four practices that keep this cheap - and the traps they avoid:

1. **One broker for the whole suite, not per test class.** This is *the* mistake: a broker per
   fixture turns a 2-minute suite into 20. The existing collection fixture already does it right;
   keep new brokers in the same fixture.
2. **Isolate with unique names, not fresh containers.** Per-test `topic-{guid}` / `group-{guid}`
   (and per-test queue names for RabbitMQ) gives full isolation at zero startup cost. Never
   restart a broker to get a clean slate.
3. **Pre-create topics via `AdminClient`; never rely on auto-create.** Auto-create hides the exact
   typo bug §2.5 warns about, and produces flaky "consumer sees nothing" failures.
4. **`.WithReuse(true)` for the local dev loop** so an inner loop doesn't pay startup on every
   run. Leave it off in CI, where a clean agent is the point.

### 3.4 CI budget

| Suite | Containers | RAM | Added cold start |
|---|---|---|---|
| Today | Postgres + RabbitMQ | ~200-350 MB | ~10-20 s |
| + external RabbitMQ provider | reuse the same RabbitMQ - **but see §3.5** | +0 | +0 |
| + Kafka provider (Redpanda tuned) | +1 | ~+200 MB | ~+5 s |
| + MQTT (`eclipse-mosquitto`) | +1 | **~+15 MB** | **<1 s** |
| + Pulsar standalone | +1 | **~+1-2 GB** | **~+30-60 s** |

Everything except Pulsar fits the 16 GB / 4-vCPU runner easily; the full set including Redpanda
and Mosquitto lands around 600 MB and adds well under 10 seconds of startup.

**Pulsar is the outlier and needs a planning decision now**, because it is tier-1 for the Tuya
demo (§8.2). Pulsar standalone is a JVM cluster in one container - BookKeeper plus broker plus
ZooKeeper-equivalent - and on a shared agent its startup alone can exceed the current entire
suite. Recommendation: **put Pulsar (and later IBM MQ) tests in a separate opt-in pipeline job**
that runs nightly or on-label, not in the PR suite. Keep the PR suite at
Postgres + RabbitMQ + Redpanda + Mosquitto.

### 3.5 Two concrete gotchas in the current setup

1. **The RabbitMQ discovery feature needs a different image - and SW-Bus already shows how.**
   There is **nothing to install and no plugin-enabling step to automate**: the official
   `rabbitmq:*-management` image variants ship with `rabbitmq_management` **pre-enabled**. It is a
   one-line image override, not a provisioning problem. SW-Bus's own integration tests already do
   exactly this:

   | SW-Bus test | Image | What it proves |
   |---|---|---|
   | `PluginUnavailableTests.cs:19` | `rabbitmq:3.13-management` | official `-management` variant = management API available out of the box |
   | `PluginAvailableTests.cs:20` | `heidiks/rabbitmq-delayed-message-exchange:3.13.0-management` | even a **non-bundled community plugin** (delayed message) is solved by a prebuilt image - no `.ez` download, no `rabbitmq-plugins enable`, no custom Dockerfile |

   So Bitween's fixture is simply the odd one out: `new RabbitMqBuilder().Build()` takes the
   module's default image, which has **no** management plugin, so every §1.12 endpoint
   (`/api/overview`, `/api/definitions`, `/api/queues`) would return nothing. Fix:
   `.WithImage("rabbitmq:4-management")` and expose `15672`. Budget ~+50-100 MB and a slightly
   slower readiness probe.

   **Switch the shared fixture rather than adding a second container** - the management API is also
   the most convenient way to assert topology in *every* RabbitMQ test, not just the discovery ones.

   Two caveats worth a decision:
   - `heidiks/…` is a **third-party image**. Fine for a test-only dependency, but pin it by digest
     rather than tag, or replace it with a three-line in-repo Dockerfile
     (`FROM rabbitmq:4-management` + `COPY` the `.ez` + `rabbitmq-plugins enable`) built through
     Testcontainers' `ImageFromDockerfileBuilder`. For the *provider*, the delayed-plugin path only
     matters if we choose to expose `SupportsRequeueDelay` (§1.7); the outbox/`DelayedRetry` path
     doesn't need it.
   - SW-Bus pins **3.13**; the official images are on 4.x. Align Bitween's fixture with whatever the
     discovery code is developed against, since management-API response fields have grown across
     3.8 → 4.x (§1.12).
2. **The test process itself is a memory factor for Kafka.** `librdkafka` buffers are native and
   per-partition (§2.3), so a test that subscribes to a multi-partition topic on defaults can
   allocate hundreds of MB inside the test host. Set tiny buffers in test config
   (`queued.max.messages.kbytes=1024`, `queued.min.messages=100`) - and add one test that asserts
   the provider's preflight guard **rejects** an oversized configuration, so the guard rail from
   §2.4 is covered rather than assumed.

### 3.6 Local dev environment

A `docker-compose.dev.yml` worth committing, because developing the discovery and Kafka features
without UIs is miserable:

| Service | Why |
|---|---|
| `postgres` | app + Quartz store |
| `rabbitmq:4-management` (5672 + **15672**) | the management UI is both the dev tool *and* the thing §1.12 talks to |
| `redpanda` (tuned flags) | Kafka endpoint |
| `redpanda-console` | topics/groups/lag browser - the closest analogue to RabbitMQ's management UI, and effectively required to develop the Kafka provider |
| `eclipse-mosquitto` | ~15 MB, add it now for the MQTT phase |

Two notes: the compose file should use the *same* image tags as the Testcontainers fixtures so dev
and CI don't diverge on broker version, and Redpanda Console is dev-only - never a dependency of
the test suite.

### 3.7 Answering the "is it easy" question directly

**Kafka's local/test story is easier than its runtime story.** Getting a broker up is a solved
problem: one tuned Redpanda container, ~5 seconds, ~200 MB, using the exact Testcontainers pattern
this repo already runs. The genuinely hard parts of Kafka are the ones in §2.2-2.4 - asynchronous
connection failures, rebalances interacting with reconcile, and per-partition native buffers - and
notably **none of those are made easier or harder by the choice of container**. So the test
environment should not influence the provider sequencing: pick Redpanda, spend the saved effort on
the rebalance and buffer-guard tests, which is where the real risk lives.
