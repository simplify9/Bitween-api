# External bus providers

A `BusGateway` can now be fed by an external broker instead of the internal bus, through a
resident serverless adapter.

**Nothing existing changes.** `BusGateway.DataSourceId` is nullable and null still means the
internal bus, so every gateway already in a database behaves exactly as before. The migration
(`ExternalBusDataSources`) is additive: three columns and one table.

## The shape

```
external broker  ->  resident adapter        (owns the connection, one process)
                 ->  BusProviderEventSink    (resolves the gateway, persists the Xchange)
                 ->  XchangeService.SubmitFilterXchange
                 ->  filter -> mapper -> handler -> auto-retry     (unchanged)
                 ->  ack returns  ->  adapter acknowledges its broker
```

Past the sink, ingress from a broker and ingress from the API are the same thing. Filtering,
mapping, work-group routing and the audit trail are not reimplemented.

**The adapter does not acknowledge its broker until Bitween has persisted.** A rejection means
requeue, not loss - a Bitween outage stops draining the customer's queue rather than dropping
their messages. A crash between persisting and acknowledging means redelivery, which is why every
event carries a dedupe key.

## What lives where

| | |
|---|---|
| `DataSource` | How to reach the system: endpoint, credentials, health. One per broker. |
| `BusGateway.DataSourceId` | Which broker feeds this gateway. Null = internal bus. |
| `BusGateway.Endpoint` | Which queue or topic on it. |
| `BusGateway.EndpointProperties` | Intended for per-endpoint overrides such as prefetch. Stored and passed to the adapter, but neither adapter reads them yet. |
| `BusGateway.DocumentId` + routes | Unchanged - what the message *means* and what runs. |

One data source serves many gateways, exactly as one connection serves many queues.

## Turning it on

```json
"Bitween": { "BusProvidersEnabled": true, "BusProviderMaxInFlight": 16 }
```

Leases use Bitween's own RabbitMQ, so `ConnectionStrings:RabbitMQ` must be set on every node that runs providers.

**Safe on every node.** Each data source is owned through a lease, so exactly one node consumes it
and the rest stand by. Still opt-in, but for a different reason than before: it opens outbound
connections to third-party brokers, and that should be a decision rather than a default.

## Placement: the bus provides the lock, the database provides the fence

Two nodes consuming one queue is duplicate processing - the failure this whole design exists to
prevent. So ownership is granted per data source, not globally: whichever node wins each race owns
that source, so connection load spreads without anyone scheduling it, and one node leaving does not
move everything at once.

**The lock** is a RabbitMQ exclusive queue, `bitween.lease.datasource.{id}`. An exclusive queue
belongs to a single connection, so declaring it succeeds for exactly one node and fails for every
other - and the broker releases it the moment that connection dies. Liveness for free: no lease
renewal to get wrong, no clock to trust.

**The fence** is a monotonic `term` in `cluster_lease`, because the lock alone is not enough. A
node can be paused long enough - a stop-the-world GC, a partition that heals - for its queue to be
released and reclaimed while it still believes it owns the resource, and RabbitMQ has no counter
that would reveal it. Acquiring bumps the term; a holder whose term is no longer current has been
superseded and stops immediately.

Three details that are load-bearing:

* **Recovery is off on the election connection.** A recovered connection silently re-declares the
  exclusive queue, so a node that lost ownership during an outage would take it back *without
  bumping the term* - two owners, neither aware.
* **Releasing DELETES the queue.** An exclusive queue belongs to the connection, not the channel,
  so closing the channel releases nothing. Without the delete, mutual exclusion and crash failover
  both work while GRACEFUL handover silently never completes - ownership sits with a shut-down
  node until its connection finally drops, which is precisely what a rolling restart does.
* **Losing a lease stops the adapter WITHOUT draining.** Another node may already be consuming, so
  finishing in-flight work risks handling the same messages twice.

`ILeaderElection` exists so the mechanism can be replaced when the internal bus is no longer
RabbitMQ - not so two implementations can be maintained at once.

## Egress: a delivery publishes

A bus adapter is declared `[AdapterKind("bus")]` **and** `[AdapterKind("handler")]`, so it appears
in a subscription's delivery picker. The delivery's own properties say where to send:

| Property | Applies to | Meaning |
|---|---|---|
| `Endpoint` | both | RabbitMQ: the queue name. SQS: the full queue URL. |
| `Exchange`, `RoutingKey` | RabbitMQ | Publish through an exchange instead of straight to a queue. |
| `GroupId`, `DeduplicationId` | SQS | FIFO queues only; a FIFO queue rejects a send with no group. |

Not restricted to the endpoints the data source consumes - the usual case for egress is a queue
Bitween does not drain.

### Why this needed more than a method

`Publish` existed from the start and nothing could reach it: Bitween's pipeline calls `Handle` on a
handler, and neither adapter had one. So an integration could drain a customer's queue and had no
way to answer on it.

Adding `Handle` is half of it. The other half is that **a broker data source is exclusive** - one
node holds the connection so the queue is drained once - while **a delivery runs on whichever node
picked the message up.** Publishing would therefore fail on every node but the owner.

Exclusivity is about *consuming*, not about connecting. When the owned instance is not on this
node, `ResidentAdapterRuntime` opens a **send-only** connection instead: built from the data
source's own settings, with `Consume=false`, no `Endpoints`, and its own pool key so it can never
be confused with the consuming instance. It sends and never subscribes, so nothing is processed
twice, and a delivery works wherever it lands. This is the same `Consume=false` the connection
test uses, for the same reason.

### The one thing it cannot do

A subscription has **one** `dataSourceId`, applied to every slot. So a single subscription cannot
read from a database and publish to a broker - both slots would resolve to the same connection.
Chain two subscriptions with `ResponseSubscriptionId` for that shape.

## The two providers

### `SW.Bitween.Adapters.Bus.RabbitMq`

An external RabbitMQ - someone else's broker, not Bitween's own. Consumes with `autoAck: false`,
acks only after Bitween persists, nacks with requeue on rejection. `DeclareMode` is `none`,
`assert` (passive declare, fail loudly) or `create`; `assert` is the default because silently
creating queues on a customer's broker is not our call.

Dedupe key is the broker's message id - **not** the delivery tag, which is per
channel and restarts at 1 on every reconnect.

Also supports `Publish` - and is a **handler**, so a subscription's delivery can publish through
it. See *Egress* below.

### `SW.Bitween.Adapters.Bus.Sqs`

SQS is polled, not pushed, and has no ack - only delete. Same contract by a different mechanism:

```
receive -> persist -> ONLY THEN DeleteMessage
```

A rejection resets visibility to 0 so it retries in seconds rather than waiting out the timeout.
**`VisibilityTimeoutSeconds` must exceed how long Bitween takes to persist**, or a message is
redelivered while the first copy is still being handled - `TestConnection` warns below ~30s.

Credentials are optional: leave them blank to use the ambient chain (instance profile, IRSA,
environment), which is the right answer on AWS.

**Why this one matters beyond SQS itself:** the Amazon Selling Partner API delivers notifications
by publishing to an SQS queue *you* own - you create the queue, grant SP-API permission to send to
it, then subscribe notification types to that destination. So this adapter is the transport for
SP-API notifications, and `UnwrapSellingPartnerNotification` handles the envelope they arrive in,
promoting `notificationType` and the metadata into headers so a Bitween document schema does not
have to carry Amazon's wrapper.

The SP-API *request/response* calls are ordinary HTTPS and belong in a mapper or handler, not
here. This covers the push half.

## Deduplication

At-least-once delivery is what persist-then-acknowledge buys: a crash between committing the
Xchange and acknowledging the broker redelivers **by design**. Duplicates are normal, not
exceptional, so something has to recognise them.

Each adapter supplies a key - the broker message id for RabbitMQ, the SP-API notification id for
SQS (stable across a redelivery, where the SQS `MessageId` is not), the SQS `MessageId` otherwise. A RabbitMQ message with no message id gets no key and is never deduplicated. The
host prefixes the `DataSourceId` and records it in `inbound_message`.

**The key is the primary key, and the database is the arbiter.** A duplicate is detected by the
insert *failing*, never by a lookup succeeding - "check whether it exists, then insert" is
check-then-act and races, so two concurrent deliveries of one key would both miss and both
persist. `Concurrent_deliveries_of_one_key_produce_exactly_one_Xchange` fires eight at once
precisely to prove the constraint is doing the work.

**It commits with the Xchange, in one transaction.** The sink adds the row to the same `DbContext`
the Xchange is written through, so `SubmitFilterXchange`'s existing save commits both. Splitting
them gives two failure modes and the worse one is silent: a dedupe row committing while the
Xchange fails would suppress that message for ever.

A duplicate is **accepted**, never rejected - rejecting would nack and redeliver a message that is
by definition already handled, and the queue would never drain. An event with no key is never
deduplicated, because collapsing unidentified messages would lose data.

`DataSource.DeduplicationWindowDays` (default 30, zero to disable) sets how long a key is
remembered, and `InboundMessagePruneJob` forgets them nightly. Forgetting **too early** is the
dangerous direction: a redelivery after the key is gone is processed as a fresh message. That
window is a property of the customer's broker - its message TTL, dead-letter replay, someone
re-driving a queue by hand - which is why it sits on the data source rather than in configuration.

## A trap in the PostgreSQL context

`SW.Bitween.PgSql.BitweenDbContext` does **not** call `base.OnModelCreating` - it redeclares the
model. Anything configured only in `SW.Bitween.Api`'s context is inert on the primary provider.

`DataSource` reached the model regardless, by convention, through the `BusGateway.DataSource`
navigation - which is why it worked while its intended configuration (unique index on `Name`,
explicit lengths) was silently never applied. `InboundMessage` has no such navigation and simply
did not exist until it was declared here.

Anything added to the model needs configuring in the provider contexts, not just the Api one.

## Not done yet

- ~~**CRUD API and UI** for `DataSource`.~~ Done - data sources are under Configuration, and the
  form is generated from the adapter's own `[AdapterSetting]` attributes.
- **Secret protection at rest.** `SecretProperties` names the fields; wiring it to
  `SettingsProtector` is outstanding, so treat credentials in `DataSource.Properties` as
  plaintext until that lands.
- **Re-applying `DataSource`'s intended configuration** in the provider contexts - the unique
  index on `Name` is currently missing, per the trap above.
