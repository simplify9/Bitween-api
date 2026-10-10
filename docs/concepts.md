# Concepts

This page defines the words Bitween uses. The admin UI and the code sometimes name the same thing differently, so both names are given.

## Information type

An information type describes one kind of business document, such as a purchase order or a shipment update. The code calls it a **Document**.

| Field | Meaning |
|---|---|
| Name, code | A display name and an optional unique short code. |
| Format | `Json` or `Xml`. Decides how promoted properties and match expressions read the payload. |
| Promoted properties | Named paths into the payload, such as `orderId` pointing at `order.id` in JSON or an XPath in XML. Their values are extracted from every exchange, shown in lists in the type's order and searchable. The first one names archived exchanges. Point them at short values, such as an order number: a value longer than 500 characters is stored cut. |
| Available on the bus, bus message type name | When enabled, Bitween consumes RabbitMQ messages published under this name and treats each one as a document of this type. The name cannot contain spaces and must be unique ignoring case. |
| Disregard unfiltered messages | When on, documents arriving on the bus are filtered straight away. Only matching subscriptions get exchanges, and no document-level exchange is recorded. |
| Duplicate interval | Stored and shown, but not enforced by the current code. |

The information type with id `10001` is reserved for aggregation output.

## Partner

A partner is an external party you exchange data with.

- **Properties** are key/value pairs that adapters reference with `{{partner.KEY}}`, such as a partner's API base URL or account number.
- **API keys** are named credentials. A caller sends one in the key header (`partnerkey` unless renamed), as a bearer token or as Basic auth, to call an API gateway or the legacy exchange endpoint.
- **Login server identity** is the value a login server's tokens carry for this partner. API gateways that take tokens instead of keys use it to know who is calling.
- The built-in **SYSTEM** partner, id 1, cannot be deleted and cannot own subscriptions. Its key can post documents of any type straight into the filter. See [Security](security.md#partners-and-api-keys).

## Subscription

A subscription is a configured pipeline. It says what starts it, which adapters run and what happens to the response. The UI groups subscriptions by how they start.

| UI name | Type in code | Started by | Partner on the subscription |
|---|---|---|---|
| API gateway subscription | `GatewayApiCall` | A partner calling an API gateway it is attached to | Must be empty. The caller supplies the partner. |
| Bus gateway subscription | `BusGateway` | A bus gateway route whose filter matches a message | Must be empty. The route can supply a partner. |
| Scheduled job | `Receiving` | Its schedule, which runs a receiver adapter | Optional |
| Aggregation | `Aggregation` | Its schedule, which collects finished exchanges of another subscription | Required |
| Internal (legacy) | `Internal` | Any document of its information type whose payload passes its match expression | Required |
| API call (legacy) | `ApiCall` | Its own partner posting to `POST /api/xchanges/{informationType}` | Required |

A subscription holds:

- **Adapters.** A receiver, validator, mapper and handler, each optional depending on the type, each with its own properties. See [Adapters](adapters.md).
- **Data source.** A broker or database connection that the subscription's adapters run through. See [Data sources](data-sources.md).
- **Match expression.** A filter over the payload, used by internal subscriptions. Bus gateway routes carry their own.
- **Schedules** for scheduled jobs and aggregations. See [Scheduling](scheduling.md).
- **Response routing.** A bus message type name to publish the handler's response under. Older subscriptions may instead feed the response straight into another subscription.
- **Retry policy.** Either a shared policy or an inline one. See [Retries and alerts](retries-and-alerts.md).
- **Work group** and **category.**
- **State.** Inactive, paused, the running flag, consecutive failures and the last exception.

New subscriptions are created inactive unless the request explicitly asks otherwise.

## Exchange

An exchange is one message travelling through one pipeline. The code calls it an **Xchange**. Every exchange has:

- An id, a 32-character GUID.
- Its information type, plus its subscription and partner when known.
- Up to three stored files: **input** is what arrived, **output** is what the mapper produced, and **response** is what the handler returned.
- **References**, free-text tags such as `partnerkey: acme-prod`.
- A **correlation id** shared by exchanges of one flow, such as a request and the exchange created from its response.
- **Retry for**, the id of the exchange it retries. Each exchange can be retried once, so retries form a chain, and later retries continue from its newest attempt.
- A snapshot of the mapper and handler ids and properties, taken when it was created.

An exchange without a subscription is a *document-level* exchange. Processing it fans the document out to matching subscriptions.

## Result

When processing ends, Bitween writes an **exchange result** with the same id.

| Status in the UI | Condition |
|---|---|
| Processing | No result yet |
| Success | No exception, and the response was not flagged bad |
| Bad response | No exception, but the handler flagged its response as bad data, as the HTTP handler does for a 4xx reply |
| Failed | An exception was thrown |

A result also records which retry group matched, the attempt number and, when no retry was scheduled, the reason.

## Work group

A work group is a processing lane. Each work group gets one RabbitMQ queue for exchanges and another for results, with its own prefetch and priority. Subscriptions without a work group share the *Ungrouped* lane. Use work groups to stop a slow or busy integration from delaying the others.

The queue name is built from the work group's id and its bus message name. Changing the bus message name moves the lane to a new queue.

## Global value set

A global value set is a named dictionary shared by all adapters. Adapters reference a value with `{{globals.SETID.KEY}}`. The set id is chosen at creation and cannot change.

## Gateways

- An **API gateway** is an HTTP endpoint at `/api/gateway/{urlName}/sync` or `/async`. Each attached partner is paired with the subscription that runs when that partner calls.
- A **bus gateway** listens for one information type, either on Bitween's internal bus or on a queue of a customer's broker. Each of its **routes** has an optional match expression, an optional partner and a bus gateway subscription to run.

## Other terms

| Term | Meaning |
|---|---|
| On-hold exchange | A message routed by content to a paused subscription. It becomes a real exchange when the subscription is resumed. |
| Delayed retry | An automatic retry waiting for its time. Its id is the failed exchange's id. |
| Receive attempt | One run of a scheduled job or aggregation and its outcome: received data, nothing new, or failed. |
| Notifier | A handler adapter that runs after exchanges of chosen subscriptions succeed, return a bad response or fail. |
| Retry policy | Rules that decide whether and when a failed exchange is retried. |
| Category | A code and description used to group subscriptions. Picked on the subscription's page, beside its work group; **All subscriptions** filters by it and its **Categories** button adds, renames and deletes them (one in use can't be deleted). |
| Data source | A connection to a customer's broker or database, held open by a resident adapter. See [Data sources](data-sources.md). |
| Statement | A named piece of SQL a database data source is allowed to run. See [Databases](databases.md). |
| Resident adapter | An adapter that runs as a long-lived process instead of one process per call. |
| Retry chain | An exchange and the retries that followed it, one after another. |
