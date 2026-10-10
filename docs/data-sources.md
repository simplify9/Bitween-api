# Data sources

A data source is a connection to a system outside Bitween, held open by a long-running adapter process. There are two kinds today.

| Kind | Providers | Used for |
|---|---|---|
| Broker | RabbitMQ (`bitween.bus.rabbitmq`), Amazon SQS (`bitween.bus.sqs`) | Feeding bus gateways from a customer's broker, and publishing deliveries to it. See [External brokers](external-brokers.md). |
| Relational | PostgreSQL, MySQL, SQL Server, Oracle (`bitween.db.*`) | Reading rows into Bitween, and writing exchanges to tables or procedures. See [Databases](databases.md). |

Data sources are managed on **Configuration → Data sources**.

## Turning them on

Data sources need three things on the nodes that run them.

1. **`Bitween__BusProvidersEnabled=true`.** The name predates database sources, but the flag gates every data source. Nodes without it can still configure data sources in the UI, but cannot run them or any delivery that goes through one.
2. **`ConnectionStrings:RabbitMQ`.** Bitween's own RabbitMQ decides which node holds each connection. See [Placement](#placement).
3. **The adapter packages.** Each provider is a separate package, installed in storage under `{Bitween:AdapterPath}/{adapterId}` like a custom adapter, with the metadata that marks it as a resident adapter. The pipelines in this repository do not publish these packages.

| Key | Default | Meaning |
|---|---|---|
| `Bitween:BusProvidersEnabled` | `false` | Run data source adapters on this node |
| `Bitween:BusProviderMaxInFlight` | `16` | Unacknowledged messages one adapter may have in flight with Bitween |
| `Bitween:InboundMessagePruneCron` | `0 30 3 * * ?` | When expired deduplication keys are deleted |

The Helm chart has no dedicated values for these. Set them through `environmentVariables`.

## Resident adapters

Bitween now runs adapters in three ways, and picks the first that claims an adapter id.

| Runtime | Claims | Runs as |
|---|---|---|
| Native | Ids starting with `native` | A method call inside Bitween |
| Resident | Packages marked as resident | A long-lived process that keeps its connection or pool open between messages |
| Classic | Everything else | A new process for each call, as described in [Adapters](adapters.md#custom-adapters) |

Every stage of the pipeline goes through the same invoker, so any role (receiver, validator, mapper, handler, notifier) can use any runtime. A resident adapter is worth its extra complexity when opening a connection is expensive, as it is for brokers and database pools.

## Settings

| Field | Meaning |
|---|---|
| Name | Unique name |
| Provider | The adapter id. Its settings form is built from what the adapter declares, in the adapter's order. |
| Settings | Provider-specific connection settings. Secret settings are masked in the API. |
| Placement | `Auto`, `Exclusive` or `PerNode`: **Runs on** on the data source page. A save that leaves it out, through the API, keeps it as it was. |
| Inactive | Stops the adapter everywhere |
| Deduplication window (days) | Brokers only. How long inbound message keys are remembered. Default 30; 0 turns deduplication off. |
| Soft memory limit (MB) | Crossing it makes the host recycle the adapter between messages |
| Hard memory limit (MB) | The adapter process's heap limit |
| CPU limit (%) | Sustained CPU as a share of the whole node. One busy core on a 16-core node is about 6%. |
| CPU limit samples | How many consecutive heartbeats over the limit trip it |

A value of 0 for a ceiling means the host's default. The soft limit cannot exceed the hard limit, and the CPU limit cannot exceed 100.

Secrets are masked as `__private__` in responses, and sending the sentinel back keeps the stored value. A setting counts as secret if the adapter declares it, or if its name contains words such as password, secret, token, key or connection string. **Data source settings, including secrets, are stored unencrypted in the database.**

## Placement

| Placement | Behaviour |
|---|---|
| `Auto` | `Exclusive` for brokers, `PerNode` for everything else |
| `Exclusive` | Exactly one node runs the adapter at a time |
| `PerNode` | Every enabled node runs its own instance |

A supervisor on each enabled node reconciles every 30 seconds.

1. It drops any adapter whose lease it has lost, without draining.
2. For each active data source, it acquires or confirms the lease. For `Exclusive` sources, a node holds the lease by owning an exclusive queue named `bitween.lease.datasource.{id}` on Bitween's RabbitMQ, and a counter in the `ClusterLeases` table fences off previous holders.
3. It starts or restarts the adapter with the source's settings, its gateway endpoints and its statements. A change to any of these, or to a ceiling, restarts the adapter.
4. It stops adapters for sources that were deleted or deactivated, and releases their leases.
5. It writes the state, last heartbeat, last error, restart count and owning node back to the data source.

If the owning node crashes or loses its connection, the broker deletes the lease queue straight away, and another node takes over on its next reconcile, within 30 seconds. A node shutting down releases its leases first.

Changes made in the UI take effect on the next reconcile, not immediately. Restarts, back-off and quarantine after repeated crashes are handled by the `SimplyWorks.Serverless` host.

## Testing and inspecting

- **Test connection** (`data-sources.operate`) starts a throwaway instance with the saved settings, runs the adapter's own checks and stops it. The result lists each stage. When the adapter fails to start, the message shown is the adapter's own error, taken from its output.
- **Discover** and **Stats** ask the running instance on the node that serves the request. Discover is not offered for databases, which have a schema browser instead.
- The **Connection** panel shows who holds the connection, the last heartbeat, restarts and the last error. When the adapter runs on the node serving the page, a live panel refreshes every 3 seconds with process memory, CPU, threads, uptime and the adapter's own counters.

Test, Discover, Stats and the live panel all run on whichever replica served the request. Behind a load balancer they may report that the connection is held elsewhere.

## Deleting

A data source cannot be deleted while a bus gateway uses it. Deleting one that subscriptions are bound to also fails, but with a raw database error rather than a clear message. Deleting a data source deletes its deduplication keys and statements.

## Permissions

| Permission | Allows |
|---|---|
| `data-sources.view` | Browse data sources, their health, Discover and Stats |
| `data-sources.create`, `edit`, `delete` | Manage connections and credentials |
| `data-sources.operate` | Test a connection |
| `data-source-statements.view`, `create`, `edit`, `delete` | Manage the SQL a database source may run, without the right to change its credentials |

## API

| Method and path | Permission |
|---|---|
| `GET /api/datasources` | `data-sources.view`, or any signed-in member with `lookup=true` |
| `GET /api/datasources/Providers` | `data-sources.view` |
| `GET /api/datasources/{id}` | `data-sources.view` |
| `GET /api/datasources/{id}/telemetry` | `data-sources.view` |
| `POST /api/datasources` | `data-sources.create` |
| `POST /api/datasources/{id}` | `data-sources.edit` |
| `DELETE /api/datasources/{id}` | `data-sources.delete` |
| `POST /api/datasources/{id}/test` | `data-sources.operate` |
| `POST /api/datasources/{id}/inspect` | `data-sources.view`. Body `{ "command": "Discover" \| "GetStats" \| "Describe", "arguments": {} }` |

## Limits

- Health rows are written by the node running the adapter. A source that runs nowhere keeps its last recorded state.
- Leases are checked every 30 seconds, not per message. An old owner that stalls can overlap with the new one for up to that long.
- The lease queue name does not include `Bitween:QueuePrefix`. Two deployments sharing one RabbitMQ virtual host with the same data source ids compete for the same leases.
- On PostgreSQL there is no unique index on data source names. Uniqueness is only checked by the API before saving.
