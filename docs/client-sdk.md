# Client SDK

The client SDK lets your own program work with a Bitween's exchanges, as the Exchanges and Scheduled
retries pages do: search them, read their files, follow their retry chains, retry one or many, and
export their files. It comes for .NET (`SimplyWorks.Bitween.Client` on NuGet) and for JavaScript and
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

The rows and plans are the API's own types, from `SimplyWorks.Bitween.Sdk`, in `SW.Bitween.Model`:
`XchangeRow`, `XchangeRetryTree`, `XchangeBulkRetryPlan`, `XchangeFilesExportCheck` and `DelayedRetryRow`.

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

Types come with it. Rows have the API's camelCase names, and times are ISO strings.

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
many pages that is, with some left out. A query with an empty id list is refused rather than read as no ids at all,
which would match every exchange.

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
