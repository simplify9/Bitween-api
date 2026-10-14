# Entry points

Every exchange starts at one of these entry points, and all of them lead into the same [pipeline](exchange-pipeline.md).

| Entry point | Subscription type | Runs the validator | Honours pause |
|---|---|---|---|
| API gateway | `GatewayApiCall` | Yes | No |
| Bus gateway route | `BusGateway` | No | Yes |
| Bus message to an internal subscription | `Internal` | No | Yes |
| Scheduled job | `Receiving` | No | No |
| Aggregation | `Aggregation` | No | No |
| Legacy API call | `ApiCall` | Yes | No |
| Exchange created by hand | Any | No | No |

## API gateways

An API gateway gives partners one HTTP endpoint. Each attached partner is paired with the API gateway subscription that runs when that partner calls, so two partners can call the same URL and run different pipelines.

### Setting one up

1. Create the gateway with a name and a URL name. The URL name must be lowercase letters and digits, optionally separated by single `-` or `_` characters.
2. Attach a partner, and pick or create an API gateway subscription for it.
3. Give the partner a way to prove who it is: an API key on its partner page, or its login server identity if the gateway takes tokens. See [Authentication](#authentication).
4. Activate the subscription.

### Calling a gateway

```bash
curl -X POST "https://bitween.example.com/api/gateway/orders/async" \
  -H "partnerkey: <partner API key>" \
  -H "Content-Type: application/json" \
  -d '{"order": {"id": "SO-1001"}}'
```

The body is read as text and stored as the input file, named `{urlName}.json`. The exchange gets the reference `partnerkey: <key name>`, or `jwt: <identity>` on a gateway that takes tokens.

Bitween checks each call in this order.

| Check | Response when it fails |
|---|---|
| A gateway with this URL name exists | 404 |
| The caller proves who it is the way the gateway asks. See [Authentication](#authentication) | 401 |
| That partner is attached to this gateway | 401 |
| The gateway is active | 503, saying the gateway is deactivated |
| The attached subscription is active | 404 |
| The subscription's validator accepts the body | Validation error |

A payload its information type drops as a [duplicate](concepts.md#duplicates) is answered with the
exchange it repeats, and the header `Bitween-Duplicate-Of`.

Deactivation is checked after authorization on purpose, so only attached partners learn that the gateway exists.

### Authentication

Each gateway decides how callers prove who they are, in its **Authentication** card. A partner can't pick another way.

**API keys** are the default. The partner sends a key issued on its partner page, in any one of these.

| Where | What |
|---|---|
| The key header | The key |
| `partnerkey` header | The key. Always accepted, whatever the key header is called |
| `Authorization: Bearer` | The key |
| Basic auth | Username: the key's name. Password: the key |

The key header is `partnerkey` unless renamed. Set the name for every gateway in **Settings → API behavior**, or give one gateway its own in its Authentication card. A gateway's own name replaces the system-wide one there. Renaming it cuts off partners sending the previous name, so the gateway page asks first.

A key whose name contains a colon can't be sent as Basic auth.

**Tokens from a login server (JWT).** The partner sends `Authorization: Bearer <token>`, signed by the login server the gateway trusts. Bitween checks that:

- the signature matches a public key published at `{login server}/.well-known/openid-configuration`
- `iss` is the gateway's login server and `aud` is its audience
- `exp` has not passed

The claim named in the gateway's partner claim, `sub` unless set, must equal a partner's **login server identity**, set on the partner page. Identities are unique.

- A gateway that takes tokens refuses API keys, and a gateway that takes keys refuses tokens.
- The login server must be an `https://` address. `http://localhost` is allowed for trying it out locally.
- If the login server can't be reached, calls get 401 and the log says why. Bitween waits at most 10 seconds for it.
- Keys are cached. A key the login server stops publishing is refused as soon as Bitween reads the new list.

### Async and sync

`/async` returns `202 Accepted` with the exchange id as soon as the exchange is stored.

`/sync` waits for the result and then responds as follows.

| Outcome | Response |
|---|---|
| Success with no response body | `200` with the exchange id |
| Success with a response | `200` with the response body and its content type |
| Bad response | `400` with the response body |
| Failure | Depends on the gateway's **When a delivery fails** setting, below |
| Wait ended before a result | `202` with the exchange id |

What a failure answers is set per gateway, under **When a delivery fails** (`failureAnswer` in the API and in `bitween apply`).

| Setting | Failure, a retry scheduled | Failure, the message couldn't be mapped | Failure, no retry |
|---|---|---|---|
| `Accurate` | `202` with `{ exchangeId, status: "retrying", nextAttemptOn }` | `422` with `{ exchangeId, status: "failed", error }` | `502` with `{ exchangeId, status: "failed", error }` |
| `Legacy` | `400` with no body | `400` with no body | `400` with no body |

Gateways created before the setting existed answer `Legacy`, as they always did; new ones answer `Accurate` unless created with `Legacy`. Legacy tells the caller its request was wrong even when the destination was down and Bitween will deliver the message by retry minutes later, so a caller that acts on the 400 (cancels the order, sends a corrected one) and then sees the message delivered anyway is working from a false answer. With `Accurate`, a `202` means Bitween has the message and will deliver it again: sending it again would make a second one.

The call is answered as soon as the exchange's result is saved: the worker that saved it tells the node holding the call, directly or by a broadcast over RabbitMQ, and the answer comes from that signal. As a backstop, for a signal lost to a restart or a worker on an earlier version that sends none, Bitween also checks the database 2 seconds after the call and every 5 seconds after that. A call that repeats an earlier one, by its `Idempotency-Key` or as a dropped duplicate, gets that exchange's result, checked at once and then polled after 1, 1, 2, 3 and 5 seconds, then every 8.

`Wait-Period` sets the longest wait in seconds: 120 when absent, and never more than `Bitween:MaxResponseWaitSeconds` (300). When it ends first, the answer is `202` with the exchange id.

## Bus gateways

A bus gateway receives documents published to Bitween's internal RabbitMQ bus, or read from a queue on a customer's own RabbitMQ or Amazon SQS broker. This section covers the internal bus. For a customer's broker, see [External brokers](external-brokers.md); routes behave the same way.

### Setting one up

1. On the information type, turn on **Available on the message bus** and set the **bus message type name**. Every instance starts consuming that message type.
2. Create a bus gateway for the information type. Its information type cannot be changed later.
3. Add routes. Each route has:
   - an optional **match expression** over the payload, where none means every message matches;
   - an optional **partner**, whose properties fill `{{partner.KEY}}` tokens;
   - a **bus gateway subscription** of the same information type to run.

Every matching route runs, so one message can start several pipelines. Deactivating a gateway stops its routes from matching without deleting them.

### Publishing a message

Publish the raw document as the message body under the bus message type name through the SimplyWorks bus, for example with `IPublish.Publish("order-created", json)` from another SimplyWorks service. The bus request context's correlation id becomes the exchange's correlation id.

### What happens to the message

The message becomes a document-level exchange. The filter then creates one exchange per matching route and per matching internal subscription. When the information type has **Disregard unfiltered messages** on, the filter runs straight away instead, and only the matching exchanges are stored.

### Chaining

Setting a subscription's response message type name publishes its handler's response to the bus. When that name belongs to a bus-enabled information type, the response arrives as a new document that bus gateway routes can pick up. The Flow map page draws these chains and flags loops.

The route's subscription can read values from the original document, the input of the exchange that delivered, as `{{source.PATH}}`, the way a response subscription can. See [Response routing](exchange-pipeline.md#6-response-routing).

## Scheduled jobs

A scheduled job, a `Receiving` subscription, pulls data with a receiver adapter on a schedule. To poll a database, see [Databases](databases.md#receiving-rows). Each run does the following.

1. Skips the run if the job is already running on any instance.
2. Moves the next run time forward, whatever happens next.
3. Starts the receiver with its properties, after resolving `{{globals...}}` tokens. Partner tokens are not resolved for receivers.
4. Lists the available items. For each item it reads the item, creates an exchange, then deletes or moves the item at the source.
5. Records a receive attempt: *Received*, *No new data* or *Failed*, with the ids of the exchanges created.
6. Resets or increments the subscription's consecutive failure count.

If one item fails, the run stops and is recorded as failed, together with the exchanges it had already created. An item is deleted only after its exchange exists, so a failure never loses an item. An item can be received twice if deleting it at the source fails.

**Receive now** runs the job once, immediately, without changing its schedule. See [Scheduling](scheduling.md) for schedules, run history and schedule health.

## Aggregations

An aggregation rolls finished exchanges of one subscription into a single exchange on a schedule. See [Aggregation](exchange-pipeline.md#aggregation). **Roll up now** runs it immediately.

## Internal subscriptions (legacy)

An internal subscription runs for every document of its information type whose payload passes its match expression. Documents reach it from the bus, from the SYSTEM partner posting to the legacy endpoint, or from an exchange created by hand for an information type. The UI marks this type as legacy. Bus gateways do the same job and add routes, partners and deactivation.

## API call subscriptions (legacy)

The original partner endpoint accepts a document for an information type and runs the calling partner's API call subscription for it.

```bash
curl -X POST "https://bitween.example.com/api/xchanges/purchase-order" \
  -H "partnerkey: <partner API key>" \
  -H "Content-Type: application/json" \
  -H "waitresponse: 30" \
  -d '{"order": {"id": "SO-1001"}}'
```

- The last path segment is the information type's id or name.
- The body must be a JSON object. Bitween adds the request's context values to it as a `_ExternalRequestContext` string field before storing it.
- The partner may have at most one subscription for the information type.
- When the key belongs to the SYSTEM partner and no subscription exists, the document goes to the filter as if it had arrived on the bus.

Without a `waitresponse` header, the call returns `200` with the exchange id at once. With one, it waits up to that many seconds, never more than `Bitween:MaxResponseWaitSeconds`, checking the database after 1, 1, 2, 3 and 5 seconds, then every 8. It gets no signal from the worker.

| Outcome | Response |
|---|---|
| Success with no response body | `200` when the **Accepted response status code** setting is 200, otherwise `202` |
| Success with a response | `200` with the response body, its content type, and a `location` header holding the exchange id |
| Bad response | An error status with the response body |
| Failure | A validation error, *Internal processing error.* |
| Wait ended | `202` with the exchange id |

The partner can read the outcome later with `GET /api/xchanges/{exchangeId}`, which returns whether it succeeded and a URL to the response file.

## When traffic stops

A partner whose system breaks, a carrier that moves its folder, a table that stops filling: none of these fails an exchange, because no exchange is created. A subscription can say how often it expects one, and Bitween sends a notification when none arrives.

On the subscription's page, **Expect traffic** takes:

- **Every** so many minutes, at least 5.
- **Days**, Sun to Sat. None picked means every day.
- **From** and **To**, local times of day, both or neither. Without them the whole day counts. A window can't run past midnight.
- **Time zone**, an IANA name such as `Asia/Riyadh`. Empty means UTC.

Every five minutes (`Bitween:QuietSubscriptionsCron`), Bitween checks each active subscription that isn't paused and has an expectation:

- **Gone quiet.** Inside the window, when no exchange of the subscription started in the last *every* minutes, and the window has been open at least that long, the subscription is marked **Quiet since** that moment. Whole days in a row count as one open stretch, so Sunday's silence carries into Monday. A window with hours opens afresh each day, so the last order of yesterday evening doesn't make it quiet at 08:01.
- **Back.** When an exchange starts after it was marked, the mark is cleared, inside the window or not.
- Outside the window nothing is raised.

Each change is sent once, to the subscription's notifiers that have **Run on failed result** on and to its notification channels ticked **Goes quiet**, as the same JSON document a failed exchange sends (see [Subscription notifications](retries-and-alerts.md#subscription-notifications)), with `Event` set as below. Email, Teams and Slack channels send a message Bitween writes from it instead.

| `Event` | `Success` | `Exception` |
|---|---|---|
| `gone-quiet` | `false` | `No exchanges since 2026-10-12 08:14 UTC; Orders from marketplace expects at least one every 15 minutes, Sun-Thu 08:00-22:00 Asia/Riyadh.` |
| `back` | `true` | `Exchanges arriving again after 47 minutes.` |

There is no exchange, so `Id` and `CorrelationId` are an id made for the alert, such as `gone-quiet-42-20261012082900`, and the handler's properties get no `xchangeid`. The sends are logged, not listed in the notification history. The subscription's page shows the mark, and the dashboard's insights list quiet subscriptions.

Removing the expectation clears the mark. Pausing or switching off the subscription stops new marks; one already set stays until traffic returns.

## Exchanges created by hand

In the admin UI, members with `exchanges.operate` can create an exchange from the Exchanges page. The API equivalent is `POST /api/xchanges`.

```json
{ "option": "SubscriberId", "subscriberId": 42, "data": "{\"order\":{\"id\":\"SO-1001\"}}" }
```

With `"option": "DocumentId"` and a `documentId`, the payload becomes a document-level exchange and fans out through the filter. The input file is named `manual.json`.
