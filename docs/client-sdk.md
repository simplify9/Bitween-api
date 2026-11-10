# Client SDK

The client SDK lets your own program work with a Bitween as the admin UI does, without anyone opening
it. It covers:

- **Exchanges**, as the Exchanges and Scheduled retries pages do: search them, read their files,
  follow their retry chains, retry one or many, and export their files.
- **Configuration**: partners with their settings, secrets and keys; subscriptions, switched on and
  off and paused; API gateways and the partners attached to them; bus gateways and their routes;
  notification channels. Information types can be read.
- **Sending**: `BitweenGateway` calls an API gateway as a partner, with one of the partner's keys.

A program that runs an integration for each of its own customers can set each one up, send through
it and follow what happened. See [One integration per customer](#one-integration-per-customer).

It comes for .NET (`SimplyWorks.Bitween.Client` on NuGet) and for JavaScript and
TypeScript (`@simplyworks/bitween-client` on npm), with the same calls in each.

It calls the same API the admin UI calls, and answers with the same rows. Each release of Bitween
from `releases/r10.0` publishes both packages at Bitween's version; use the version of the Bitween you
call, or an earlier one.

## A service account

The SDK signs in as a **service account** (Team > Service accounts in Bitween). Create one, give it a
role, and issue it a token. The token, starting `bwt_`, is shown once.

| To | The role needs |
|---|---|
| Search exchanges, read their files and retry chains, see scheduled retries, check and export | `exchanges.view` |
| Retry, bulk retry, and run a scheduled retry now | `exchanges.operate` (without `exchanges.view` too, a retry can't answer with the new attempt's id) |
| Partners | `partners.view`, `.create`, `.edit`, `.delete` |
| Subscriptions | `subscriptions.view`, `.create`, `.edit`, `.delete`; `subscriptions.operate` to pause and resume |
| Information types | `documents.view` |
| API gateways and their partners | `api-gateways.view`, `.create`, `.edit`, `.delete` |
| Bus gateways and their routes | `bus-gateways.view`, `.create`, `.edit`, `.delete` |
| Notification channels | `notifiers.view`, `.create`, `.edit`, `.delete`; `notifiers.edit` to send a test |

Give the account only what the program does. Roles apply to the whole Bitween: an account that may
view exchanges views every partner's.

The SDK trades the token for a session on its first call, and again whenever the session runs out or
is refused, so the token is all you hand it.

Keep the token on your server. A browser app calls your own back end, and your back end calls
Bitween: a token in browser code can be read by anyone using the page, and does all its role allows.

## .NET

```sh
dotnet add package SimplyWorks.Bitween.Client
```

It needs .NET 10.

```csharp
using SW.Bitween.Client;

using var bitween = new BitweenClient("https://bitween.example.com", token);

var failed = await bitween.Exchanges.SearchAsync(new ExchangeQuery
{
    Status = ExchangeStatus.Failed,
    From = DateTimeOffset.UtcNow.AddDays(-1),
    PartnerIds = [7],
});

foreach (var exchange in failed.Result)
    Console.WriteLine($"{exchange.Id} {exchange.DocumentName}: {exchange.Exception}");

var first = failed.Result.FirstOrDefault();
if (first != null)
{
    var input = await bitween.Exchanges.ReadFileAsync(first.InputKey);
    var retryId = await bitween.Exchanges.RetryAsync(first.Id);
}
```

Keep one `BitweenClient` for the life of the program, as a singleton in dependency injection: it can be
used from many threads at once, and they share one session. To send through an `HttpClient` of your
own, pass it with the Bitween's address as its `BaseAddress`: `new BitweenClient(httpClient, token)`.

The rows, plans and configuration are the API's own types, from `SimplyWorks.Bitween.Sdk`, in
`SW.Bitween.Model`: `XchangeRow`, `XchangeRetryTree`, `XchangeBulkRetryPlan`, `XchangeFilesExportCheck`
and `DelayedRetryRow`; `PartnerCreate`/`PartnerUpdate`, `SubscriptionCreate`/`SubscriptionUpdate`/`SubscriptionGet`,
`ApiGatewayCreate`/`ApiGatewayRow`, `BusGatewayCreate`/`BusGatewayRow`/`BusGatewayRouteCreate`, and
`NotificationChannelCreate`/`NotificationChannelGet`. Match expressions are `OneOfSpec`, `NotOneOfSpec`,
`AndSpec` and `OrSpec`; the client writes and reads them as the API does.

## JavaScript and TypeScript

```sh
npm install @simplyworks/bitween-client
```

It needs Node 22 or later, and has no dependencies.

```js
const { BitweenClient } = require("@simplyworks/bitween-client");

const bitween = new BitweenClient({ url: "https://bitween.example.com", token: process.env.BITWEEN_TOKEN });

const failed = await bitween.exchanges.search({ status: "failed", from: new Date(Date.now() - 86400e3), partnerIds: [7] });
const [first] = failed.result;
if (first) {
  const input = await bitween.exchanges.readFile(first.inputKey);
  const retryId = await bitween.exchanges.retry(first.id);
}
```

Types come with it. Rows have the API's camelCase names, and times are ISO strings. Configuration is
sent and read as the API's JSON, so a match expression is `{ type: "one_of", path, values }`, or `and`
/ `or` with `left` and `right`.

## What it does

| .NET | JavaScript | Does |
|---|---|---|
| `Exchanges.SearchAsync(query, page, size, count)` | `exchanges.search(query, { page, size, count })` | A page of the exchanges the query matches, newest first, and `totalCount`. Pages count from 0; 25 a page unless asked. The count stops at 10,001, meaning more than 10,000; `count: false` leaves it out, as 0, and answers sooner. |
| `Exchanges.GetAsync(id)` | `exchanges.get(id)` | One exchange, or null |
| `Exchanges.ReadFileAsync(key)` | `exchanges.readFile(key)` | A file's text, by the row's `inputKey`, `outputKey` or `responseKey` |
| `Exchanges.GetRetryTreeAsync(id)` | `exchanges.retryTree(id)` | Every attempt in the retry chain, oldest first |
| `Exchanges.RetryAsync(id, reset)` | `exchanges.retry(id, { reset })` | Runs it again from its input, and answers with the new attempt's id: null when it can't look that up, because the account may retry but not view or the lookup failed. The retry is made either way. `reset` uses the subscription's current settings. |
| `Exchanges.PreviewBulkRetryAsync(selection)` | `exchanges.previewBulkRetry(selection)` | What a bulk retry would do, changing nothing |
| `Exchanges.BulkRetryAsync(selection)` | `exchanges.bulkRetry(selection)` | Retries a selection, and says what it did |
| `Exchanges.CheckExportAsync(selection)` | `exchanges.checkExport(selection)` | How many the export takes, and how many are older than storage keeps files |
| `Exchanges.ExportAsync(selection)` | `exchanges.export(selection)` | The files as one zip, a folder for each exchange, as a stream |
| `ScheduledRetries.SearchAsync(query, page, size)` | `scheduledRetries.search(query, { page, size })` | The automatic retries waiting to run, soonest first |
| `ScheduledRetries.RunNowAsync(id)` | `scheduledRetries.runNow(id)` | Runs an exchange's booked automatic retry now |

### Configuration

Each area has the same five calls, and a few of its own. `Get` answers null when there is none by that
id. `Update` replaces what it had, so read, change and save: secrets come back as `__private__` and
keys masked, and sending those back keeps the stored values.

| .NET | JavaScript | Does |
|---|---|---|
| `SearchAsync(query, page, size)` | `search(query, { page, size })` | A page, and `totalCount`. `NameContains` (case-sensitive on PostgreSQL) and raw `Filters`. |
| `GetAsync(id)` | `get(id)` | One, or null |
| `CreateAsync(model)` | `create(body)` | Creates one and answers with its id |
| `UpdateAsync(id, model)` | `update(id, body)` | Saves it, replacing what it had |
| `DeleteAsync(id)` | `delete(id)` | Deletes it. A subscription, or a channel, still in use is refused. |
| `Partners.NewKeyAsync()` | `partners.newKey()` | A new random key, stored only once saved on a partner as `{ Key = name, Value = key }` |
| `Subscriptions.ActivateAsync(id)`, `DeactivateAsync(id)` | `subscriptions.activate(id)`, `deactivate(id)` | Switches it on or off, and answers whether it changed. A new subscription is off unless created with `Inactive = false`. |
| `Subscriptions.PauseAsync(id)`, `ResumeAsync(id)` | `subscriptions.pause(id)`, `resume(id)` | Pauses or resumes it, and answers whether it changed. The API toggles, so the client reads first; two callers at once can undo each other. |
| `ApiGateways.AttachPartnerAsync(id, attachment)` | `apiGateways.attachPartner(id, attachment)` | Attaches a partner to run a `GatewayApiCall` subscription |
| `ApiGateways.UpdatePartnerAsync(id, partnerId, subscriptionId)` | `apiGateways.updatePartner(...)` | Points it at another subscription |
| `ApiGateways.DetachPartnerAsync(id, partnerId)` | `apiGateways.detachPartner(id, partnerId)` | Detaches it |
| `BusGateways.AddRouteAsync(id, route)` | `busGateways.addRoute(id, route)` | Adds a route and answers with its id |
| `BusGateways.UpdateRouteAsync(id, route)` | `busGateways.updateRoute(id, route)` | Replaces a route's subscription, partner and expression |
| `BusGateways.RemoveRouteAsync(id, routeId)` | `busGateways.removeRoute(id, routeId)` | Removes it |
| `NotificationChannels.TestAsync(id)` | `notificationChannels.test(id)` | Sends one made-up notification and answers `{ sent, error }` |

`SubscriptionQuery` adds `Type`, `InformationTypeId` and `PartnerId`. Information types
(`InformationTypes`) are read only: `SearchAsync` and `GetAsync`.

### Sending to a gateway

`BitweenGateway` calls an API gateway as a partner does, with one of the partner's keys. It doesn't
sign in: the key goes on each call.

```csharp
using var gateway = new BitweenGateway("https://bitween.example.com", partnerKey);

var accepted = await gateway.SendAsync("invoices", json, idempotencyKey: invoice.Number);
var answer = await gateway.SendAndWaitAsync("invoices", json, waitSeconds: 30);
if (answer.Outcome == GatewayOutcome.Delivered) Console.WriteLine(answer.Body);
```

```js
const { BitweenGateway } = require("@simplyworks/bitween-client");
const gateway = new BitweenGateway({ url: "https://bitween.example.com", partnerKey });
const { exchangeId } = await gateway.send("invoices", invoice, { idempotencyKey: invoice.number });
```

`SendAsync` answers once Bitween has stored the call, with its exchange id. With an `idempotencyKey`, a
repeat of a call that may not have arrived is answered with the first one's exchange (`Replayed`).
`SendAndWaitAsync` waits for the delivery and answers with its outcome and the exchange id:

| Outcome | Means |
|---|---|
| `Delivered` | Delivered; `Body` is the destination's response, if it gave one |
| `BadResponse` | Delivered, and the response was marked bad; `Body` is that response |
| `Failed` | Failed, and won't be retried; `Error` says why |
| `Retrying` | Failed, and Bitween will deliver it again by `NextAttemptOn`. Don't send it again. |
| `Held` | The destination has failed past its retry budget; Bitween keeps the message. Don't send it again. |
| `StillRunning` | The wait ended first. Follow it by `ExchangeId` with `Exchanges.GetAsync`. |

A gateway set to answer failures the `Legacy` way says only `400` with nothing in it, read as `Failed`.
An unknown gateway, a key it doesn't take, a gateway switched off and a payload its information type's
schema refuses (`SCHEMA_MISMATCH`) throw.

### One integration per customer

A program that integrates each of its customers with a destination of the customer's choosing keeps
one partner per customer, holding that customer's settings and secrets, and one subscription per
customer, with the adapter for its destination.

- **Through a bus gateway**: the program publishes one message type, and the gateway has one route per
  customer, matching the customer in the message (`OneOfSpec("customer.id", ["42"])`) and running that
  customer's subscription as their partner. Nothing carries a key.
- **Through an API gateway**: each customer's partner is attached to the gateway with its own
  subscription, and the program sends with that partner's key.

Either way, the exchange search's `PartnerIds` and `SubscriptionIds` show one customer's deliveries,
and a notification channel on the subscriptions tells the program when one finishes or fails.

### Queries

A query filters the way the Exchanges page does. What is left unset matches any; everything set has
to hold.

| Field | Matches |
|---|---|
| `Status` / `status` | `Processing`, `Success`, `BadResponse`, `Failed` (`processing`, `success`, `badResponse`, `failed`) |
| `From`, `To` / `from`, `to` | Started at or after, at or before |
| `PartnerIds`, `SubscriptionIds`, `InformationTypeIds` | Any of these |
| `Ids` / `ids` | These exchanges, with the retry of each and those each rolled up, as the page's id search does |
| `RetryFor` / `retryFor` | The retry of this exchange |
| `CorrelationId` / `correlationId` | This correlation id |
| `LatestOnly` / `latestOnly` | Only the newest attempt of each retry chain |
| `ErrorContains` / `errorContains` | The error contains the text, ignoring case |
| `PromotedProperties` / `promotedProperties` | By key: the value contains the text, or with no text (null or blank), the key was promoted |
| `Filters` / `filters` | Filters in Bitween's own `Field:Rule:Value` form, for the rest. See [Exchanges](api-reference.md#exchanges). |

A scheduled retry query takes `SubscriptionId`, `InformationTypeId`, `ErrorContains`, and `From` and
`To` for when it is due.

### Selections

Bulk retry and export take a selection: exchanges by id, or every exchange a query matches, however
many pages that is, with some left out. A query with an empty list of ids, partners, subscriptions or information types
is refused: an empty list adds no filter, so it would widen the selection rather than narrow it.

```csharp
ExchangeSelection.Of(["a1b2...", "c3d4..."])
ExchangeSelection.Matching(new ExchangeQuery { Status = ExchangeStatus.Failed, PartnerIds = [7] }, except: ["a1b2..."])
```

```js
{ ids: ["a1b2...", "c3d4..."] }
{ matching: { status: "failed", partnerIds: [7] }, except: ["a1b2..."] }
```

A bulk retry retries each chain once, from its newest attempt, and skips what succeeded or has an
automatic retry booked, saying why. It carries out at most 500 at a time: past that the plan comes back
with `overLimit` set and nothing done. To retry more, take `targets` from the preview, which plans up to
500,000, and retry them by id, 500 at a time. An export takes at most 500 exchanges.

## Errors

A refusal throws `BitweenClientException` in .NET and `BitweenClientError` in JavaScript. Both carry
the HTTP status, Bitween's reasons by code, and the first code alone.

| Code or status | Means |
|---|---|
| `ALREADY_RETRIED` | An exchange is retried at most once. Retry its newest attempt, which the row's `hasRetry` and the retry chain show. |
| `AUTO_RETRY_SCHEDULED` | An automatic retry is booked for it. Run that now instead. |
| `TOO_MANY` | An export of more than 500 |
| `DATABASE_TIMED_OUT` (504) | The query took longer than the database allows. Ask for less, such as a shorter time range. |
| 400 at sign-in | The token is unknown, expired or revoked, or its account is disabled |
| 403 | The account's roles don't have the permission the call needs |
