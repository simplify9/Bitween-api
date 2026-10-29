# Retries and alerts

## Manual retry

The Exchanges page offers retry to members with `exchanges.operate`, for one exchange, a selection, or every exchange matching the current filters.

```http
POST /api/xchanges/{id}/retry
Content-Type: application/json

{ "reason": "Partner fixed their endpoint", "reset": true }
```

- A retry is a new exchange with the original input, and `retryFor` set to the original's id.
- **Each exchange can be retried once.** A second retry of the same exchange is refused with `ALREADY_RETRIED`, so you continue from its newest attempt. The database enforces it too, so two retries of one exchange at the same moment, by two people or by a person and a retry policy, keep one; the other is refused with the same code. The attempts form a chain, shown in the exchange drawer with each attempt named by its promoted properties, and returned by `GET /api/xchanges/retrytree?id=`.
- With `reset: false`, the retry reuses the mapper, handler and properties stored on the original exchange. This works even when the subscription has since been deleted.
- With `reset: true`, shown in the UI as *Re-resolve adapter properties*, the retry uses the subscription's current configuration and resolves partner and global tokens again.
- Retrying an exchange that has an automatic retry waiting is refused. Use **Run now** on the waiting retry instead.
- Retry policies ignore manual retries. A manual retry never spends a budget and never starts an automatic chain, and its result says so.

### Bulk retry

`POST /api/xchanges/bulkretry` retries either a list of `ids`, or everything a search `filter` matches minus `excludeIds`. `POST /api/xchanges/bulkretrypreview` returns the same plan without running it, and the UI shows that plan before you confirm.

- One retry request carries out at most 500 exchanges. The preview works out up to 5,000 and lists the attempts that will run in `targets`; the UI sends them back as `ids` in rounds of 500 and shows how far it has got. If a round fails, the rounds before it are done and the rest are not retried. Past 5,000, narrow the filter.
- An exchange that was already retried is replaced by the newest attempt of its chain. Two selections that lead to the same attempt are retried once.
- The plan skips, with a reason, exchanges that no longer exist, that succeeded, that have an automatic retry waiting, whose subscription is gone when `reset` is on, or whose input file cannot be read.
- Exchanges with no result yet are included, so work stranded by an outage can be recovered. Make sure nothing is still processing before retrying it.
- The **Latest attempt only** filter, `LatestOnly:1:true`, shows only the end of each chain, which is usually what needs retrying.

## Retry policies

A retry policy decides whether a failed exchange is retried automatically, when, and how many times.

A subscription uses either a **shared policy**, managed on the Retry policies page, or an **inline policy** stored on the subscription itself. Setting one clears the other.

### Groups

A policy is a list of groups. When an exchange fails, the first group that applies decides.

| Field | Meaning |
|---|---|
| `name`, `notes` | |
| `priority` | Lower numbers are checked first. |
| `enabled` | Disabled groups are skipped. |
| `appliesTo` | `Error` for an exception, `BadResult` for a response flagged bad, or both. |
| `matchers` | Conditions. Any one of them selects the group. |
| `action` | `Allow` retries within the budget. `Block` stops retries for the failures it matches. |
| `budget` | Required for `Allow`. Holds the per-message limit, the shared total and the delay strategy. |
| `alertMode`, `alertChannelId` | Where the budget-exhausted alert goes: `Send` through the [notification channel](#notification-channels) `alertChannelId` names, `Silent`, or `Inherit` the policy's. |

For `Error`, matchers look at the full exception text. For `BadResult`, they look at the response body.

A shared policy's own `alertChannelId` is its policy-wide alert channel. Each group opens on a page of its own from the policy page. Saving a group there, or removing one from the policy page, writes the policy at once. The policy page's save bar covers only the policy's name and its policy-wide alert.

### Matchers

| `type` | Works on | Fields | Matches when |
|---|---|---|---|
| `contains` | Error, BadResult | `value`, `caseSensitive` (default false) | The text contains `value` |
| `regex` | Error, BadResult | `pattern`, `flags` (default `"i"`; `""` is case-sensitive) | The .NET regular expression matches within 200 ms |
| `exceptionType` | Error | `value`, `includeInner` (default true) | An exception type named in the text equals `value`, short or fully qualified. With `includeInner` off, only the outermost exception counts. |
| `jsonPath` | BadResult | `path`, `op`, `value` | The JSON body meets the condition. `path` uses dots and `[index]`. `op` is `Eq`, `Neq`, `Contains`, `Regex`, `Exists` or `NotExists`, ignoring case. Invalid JSON never matches. |

There is no status-code matcher. To match an HTTP status, use `contains` or `regex` on the exception text or body. The HTTP handler fails a delivery with `{code}: {name}: {body}`, as in `503: ServiceUnavailable: ...`, so `"503:"` matches one status and `"^5\\d\\d:"` (as a regex) any server error. Older versions carried only the name (`ServiceUnavailable: ...`); that text is still there, so matchers written against it keep working.

### When a destination is paused

The built-in HTTP, SMTP, SFTP and FTP handlers stop calling a destination for a while once it has failed several times in a row ([When a destination keeps failing](adapters.md#when-a-destination-keeps-failing)). Deliveries to it then fail at once with `503: ServiceUnavailable: circuit open for {destination} (...)`, as a `CircuitOpenException`, and go through the retry policy like any other failure.

- A group that matches `"503:"` retries them. A group that matches only the original failure, such as `exceptionType` `TimeoutException` or `HttpRequestException`, does not, unless it also matches `"503:"` or `CircuitOpenException`.
- Each one is an attempt, and counts against `maxAttemptsPerError` and `maxAttemptsTotal`. A delay shorter than the cool-down (`Bitween:CircuitBreakerOpenSeconds`, 60 seconds by default) spends attempts on a destination that is still paused, so give such groups a first delay of at least the cool-down.
- They count towards a subscription's **Auto-pause** like any other failure, and arrive faster than the timeouts they replace.

### Delay strategies

The index is 0 for the first retry.

| `type` | Fields | Delay |
|---|---|---|
| `fixed` | `delayMs` | Always `delayMs` |
| `linear` | `initialDelayMs`, `incrementMs` | `initialDelayMs + index × incrementMs` |
| `exponential` | `initialDelayMs`, `multiplier` (default 2), `maxDelayMs` (default 30000) | `initialDelayMs × multiplier^index`, capped at `maxDelayMs` |

### When an adapter says when to try again

An adapter can say how long to wait: a partner answered 429 Too Many Requests with a `Retry-After`, or a quota resets at a known time. It does so by failing with an error whose message starts with `{{retryAfter:SECONDS}}`. The SDKs write it for you: `throw new RetryLaterException(TimeSpan.FromSeconds(30), "...")` in .NET, `raise sw.RetryLater(30, "...")` in Python, `throw new sw.RetryLater(30, "...")` in JavaScript. Any error whose message starts that way says the same, on any SDK version. The built-in HTTP handler says it whenever a partner's 408, 429 or 5xx answer carries a `Retry-After`; its error text is unchanged, so matchers keep working, with the wait carried inside it.

It changes only when a retry runs, never whether one does:

- the policy decides as before: the group, the block, both budgets;
- a retry it allows runs no sooner than the wait asked for, and up to a tenth later, so a crowd told the same thing doesn't come back at once; a shorter wait than the policy's delay changes nothing;
- the wait is cut to **Longest wait an adapter can ask for** (`Bitween:MaxRetryAfterSeconds`, six hours by default), so no partner can park retries for days; 0 ignores what adapters ask.

Only the start of an exception's message is read, right after its type name, so a partner's answer quoted later in an error asks for nothing.

### Budgets

A budget sets two limits.

- **`maxAttemptsPerError`** caps retries of one message. The count is the length of the exchange's retry chain.
- **`maxAttemptsTotal`** is a total shared by every message of one subscription in one group. It is not a rolling window. It only resets when someone resets it or when the subscription next succeeds. **Left out, it is 0, and the group retries nothing**, as stored policies always have; `null` means no shared ceiling. `bitween apply` refuses a budget that leaves it out.

When a subscription succeeds, each of its budgets that was already exhausted before that exchange started is released. Partly spent budgets are left alone.

### Example group

```json
{
  "name": "Transient network errors",
  "priority": 1,
  "enabled": true,
  "appliesTo": ["Error"],
  "matchers": [
    { "type": "exceptionType", "value": "HttpRequestException" },
    { "type": "contains", "value": "503:" }
  ],
  "action": "Allow",
  "budget": {
    "maxAttemptsPerError": 5,
    "maxAttemptsTotal": 500,
    "delayStrategy": { "type": "exponential", "initialDelayMs": 30000, "multiplier": 2, "maxDelayMs": 1800000 }
  },
  "alertMode": "Inherit"
}
```

### How a failure is evaluated

1. Manual retries and exchanges without a subscription are skipped.
2. A failure that already has a waiting retry is not evaluated again, so a redelivered bus message cannot spend the budget twice.
3. Bitween picks the first enabled group, by priority, whose `appliesTo` includes the result type and whose matchers match. A group with no matchers matches everything.
4. If no group matched, there is no retry.
5. If the group blocks, there is no retry.
6. If the message reached `maxAttemptsPerError`, there is no retry. This is checked first, so it does not spend the shared total.
7. If the shared total is spent, there is no retry. When this failure is the one that used up the total, an alert is raised.
8. Otherwise a retry is scheduled after the delay, give or take a tenth, or later when the adapter [said when to try again](#when-an-adapter-says-when-to-try-again).

When no retry is scheduled, the exchange result records one of these reasons.

- No matching group (default block)
- Group '…' explicitly blocks this error
- Group '…' allows retries but has no budget, so none can be scheduled
- Per-message cap reached (n) in group '…'
- Group total cap reached (n) for group '…'
- This attempt was started by hand, so the retry policy left it alone and its budget is untouched.
- The scheduled retry was dropped: the subscription it belonged to no longer exists.
- The scheduled retry was dropped: the input file could not be read.
- The scheduled retry was dropped: this exchange had already been retried, as ….

### Saving rules

A policy is refused when a group:

- has no result type or no matchers;
- has matchers that cannot apply to its result types;
- allows retries without a budget;
- sends alerts without a notification channel, or names a channel that does not exist;
- still carries its own `alertHandlerId`. Alerts go through channels now.

Deleting a policy is refused while a subscription uses it. Removing a group deletes its usage counters and alert overrides.

### Testing a policy

`POST /api/retrypolicies/test` runs groups against sample content without touching any budget.

```json
{
  "groups": [ ],
  "resultType": "Error",
  "content": "System.Net.Http.HttpRequestException: Connection refused",
  "attemptsToSimulate": 5
}
```

It returns each simulated attempt with the matched group, whether it would retry, the delay and the reason, and stops at the first refusal. The policy page's test panel runs the unsaved draft.

### Suggesting a policy with AI

**Suggest with AI**, beside the retry policy on a subscription's page, proposes a policy from that subscription's own failures in the last 30 days, using the provider under [Settings, AI](configuration.md#ai). It sends the provider only:

- each kind of error (the newest 2,000 failures, grouped by their first line, the 15 most frequent) as one line with its values masked: quoted text, numbers of four or more digits, email addresses, addresses (`<url>`) and anything that looks like a credential or token (`<secret>`) are replaced;
- how often each happened, how often it was retried and how often the retry succeeded;
- the subscription's kind and adapters, and the current policy's groups without their alert settings.

Documents and response bodies are never sent; bad responses are only counted. Failures from before the upgrade that started recording each result's subscription (2026-10-14) aren't seen, so on an upgraded install the first suggestions learn only from failures since. The suggestion is checked as a saved policy would be, alerts are left to you, and it is then run on the real errors on the server: **What changes** shows, for each kind of error, what the current policy does with it, as if none of its groups' retries were used up yet, and what the suggestion would. **See what was sent** shows exactly the message that went; the fixed instructions sent before it are the same on every call and hold nothing of yours.

Nothing changes until **Save as a new policy**, which creates the policy and picks it for the subscription; saving the subscription starts using it. Asking needs `subscriptions.view` and `retry-policies.create`, and is recorded as a `suggest-retry-policy` operator action before anything is sent. `POST /api/retrysuggestions { subscriptionId, days }` is the call behind it.

## Scheduled retries

An automatic retry waits as a *delayed retry* until its time comes.

- The retry job looks for due retries every minute by default. The **Retry poll schedule** setting changes this, and saving it reschedules the job at once.
- A due retry creates a new exchange from the original input with the subscription's **current** configuration, then deletes the waiting record. If the new exchange fails too, it is evaluated with the next attempt number.
- When the original exchange, its subscription or its input file is gone, or the exchange has already been retried, the waiting record is dropped and the reason is written on the original result.
- A retry that throws is logged and removed, so one bad record cannot block the rest.

The Scheduled retries page lists waiting retries. **Run now**, `POST /api/delayedretries/{exchangeId}/runnow`, runs one straight away and needs `exchanges.operate`.

## Budget-exhausted alerts

When a group's shared total runs out, Bitween sends one alert through a notification channel. The most specific level that decides wins.

| Level | Where it is set |
|---|---|
| One subscription in one group | **Route alert** in the policy page's usage panel |
| Group | The group's page |
| Policy | The policy-wide alert card. Inline policies have no policy level. |

At each level, `Silent` stops the alert, `Send` sends it through the level's channel, and `Inherit` defers to the level above. Saving `Send` without a channel is refused. When no level decides, nobody is alerted.

The channel's adapter receives a JSON document with the exchange id, subscription id and name, information type name, correlation id, policy name, group name, total budget, blocked reason, exception and time. Email, Teams and Slack channels send a message Bitween writes from it instead.

Alerts saved with their own handler before channels existed are moved onto channels the first time this version starts. Each distinct handler and settings becomes one channel named *Retry alert: ...*, shared by every level that had the same one.

Each delivery is recorded as a notification named *Retry budget alert*. Once one delivery succeeds, a redelivered event does not send a second alert. It is tried and guarded as described in [Delivery](#delivery), but the quiet window holds back only a subscription's own notifications, never an alert.

### Usage

The policy page's usage panel shows, per subscription and group, the attempts used against the total, whether it is exhausted, the last failure, where the alert goes and whether it was delivered. From there you can reset a counter, route one subscription's alert elsewhere, or list the failures the group handled. A subscription's overview shows a banner once a budget has been spent, with its own reset button.

| Action | Endpoint | Permission |
|---|---|---|
| Usage for a policy | `POST /api/retrypolicies/{id}/usage` | `retry-policies.view` |
| Failures a group handled | `POST /api/retrypolicies/{id}/attempts` | `retry-policies.view` |
| Reset usage for a policy | `POST /api/retrypolicies/{id}/resetusage` | `retry-policies.edit` |
| Route one subscription's alert | `POST /api/retrypolicies/{id}/savealertoverride` | `retry-policies.edit` |
| Usage for one subscription | `POST /api/subscriptions/{id}/retryusage` | `subscriptions.view` |
| Reset usage for one subscription | `POST /api/subscriptions/{id}/resetretryusage` | `subscriptions.operate` |

## Auto-pause alerts

A subscription with **Auto-pause** set pauses itself after that many failures in a row. Each result records how many failures in a row it makes, and a failure counts from the subscription's latest result. Exchanges finishing at the same moment can read the same latest result, so under heavy concurrent failure a pause can come one or two failures after the number set. Bitween then sends one alert, recorded as a notification named *Auto-pause alert*, through:

- its shared retry policy's policy-wide alert channel;
- each of its [notifications](#subscription-notifications) ticked **Fails**.

A channel that is both hears once, and a redelivered event doesn't send again to a channel that already heard. With neither, the pause is logged and shown on the subscription's page, and nothing is sent. An Email, Teams or Slack channel sends a message Bitween writes. Any other adapter receives a JSON document with `Kind` (`SubscriptionAutoPaused`), `SubscriptionId`, `SubscriptionName`, `Failures`, `XchangeId`, `Exception` and `OccurredOn`.

## Notification channels

A notification channel is where a notification goes and what is needed to deliver it. Channels are listed in **Settings > Notification channels**, each opens on a page of its own, and every place that notifies picks one by name:

- a subscription's [notifications](#subscription-notifications);
- a retry policy's [budget-exhausted alerts](#budget-exhausted-alerts), at the policy, group or single-subscription level;
- [auto-pause alerts](#auto-pause-alerts).

A picker's **New channel** opens the new channel page. Saving the channel brings you back with it picked, and with any unsaved edits on the page you left still in place. A picked channel's page can be opened the same way.

### Channel types

| Type | Asks for | Sends |
|---|---|---|
| Email | `To`, and optionally `Cc` and `Bcc` | An email through the server in [Settings > Mail server](configuration.md#mail-server) |
| Microsoft Teams | `WebhookUrl`, a Teams Workflows webhook address | An Adaptive Card to the Teams channel |
| Slack | `WebhookUrl`, an incoming webhook address | A Block Kit message to the Slack channel |
| Another adapter | Any handler adapter, built in or custom, and its properties | The notification as JSON, described with each kind of notification |

Email, Teams and Slack send a message Bitween writes: what happened, the facts that place it, the error without its stack trace, and a link to the exchange, which has the whole of it. The link is built on the instance's public address, `Bitween:PublicUrl`. Without one, only a test message sent from the page has a link. Their adapters, `NativeEmailMessageHandler`, `NativeTeamsMessageHandler` and `NativeSlackMessageHandler`, are only offered as channel types, never in an adapter picker. Emails, like the member invites and password resets that share their layout, carry the Bitween logo inside the email, so it shows without loading anything, and are drawn in the instance's brand colour (**Settings, Brand & theme**, primary colour).

Microsoft retired the Office 365 *Incoming Webhook* connector for Teams in 2026. In Teams, add the **Post to a channel when a webhook request is received** workflow to the channel and use the address it gives you.

- A channel is complete. The places that use it only choose when to send. For different recipients, make another channel.
- Names are unique.
- What the adapter can't send without, such as an Email channel's recipients or a webhook address, must be filled in.
- Teams and Slack webhook addresses must be https. They are secret, because anyone who has one can post to the channel.
- Secret properties come back masked. Saving the mask keeps the stored value only while the adapter and where it connects stay the same. Otherwise, type the secret again.
- A channel on the SMTP adapter with a password and `UseTls` off is refused.
- Properties can hold `{{globals.SET.KEY}}` and `{{partner.KEY}}`, filled in on each send. Partner values come from the partner of the exchange being reported, so one Email channel can write to each partner's own contact. A test message has no partner.
- A channel that anything still uses can't be deleted. Its page lists who uses it.
- `bitween apply` can keep channels in a [configuration file](cli.md#configuration-as-code).

### Delivery

- Each send is tried up to three times, two and then four seconds apart.
- Email, Teams and Slack sends share the built-in handlers' [circuit breaker](adapters.md#when-a-destination-keeps-failing). While the mail server or webhook host keeps failing, sends to it fail at once.
- **Quiet window.** After a channel sends for a subscription and an outcome, more of the same within `Bitween:NotifierQuietMinutes` (5 by default) are held back: recorded, not sent. The next message says how many were held back (`SuppressedSinceLast`), so an outage reads as one message. `0` sends every one. Legacy notifiers work the same way.
- A result event delivered twice does not notify twice.
- Each send is recorded with its channel, whether it went or was held back, and any error. The alerts for a subscription going quiet and coming back are logged instead.

### Testing and history

**Send a test message** on the channel's page sends a made-up notification through the channel as saved, tried once, and shows whether it went or why not. An Email, Teams or Slack channel says it is a test. Any other adapter gets a notification shaped like a failure, with the subscription name *Test notification*, so its templates are tested too. Tests are not recorded.

**Recent sends** on the same page lists every send through the channel, newest first: the subscription it reported, whether it was a retry or auto-pause alert, whether it went, failed or was held back, and why any failed.

| Action | Endpoint | Permission |
|---|---|---|
| List channels, with who uses each | `GET /api/notificationchannels` | `notifiers.view` |
| Channel names for pickers | `GET /api/notificationchannels?lookup=true` | signed in |
| One channel | `GET /api/notificationchannels/{id}` | `notifiers.view` |
| Create | `POST /api/notificationchannels` `{ name, handlerId, handlerProperties }` | `notifiers.create` |
| Change | `POST /api/notificationchannels/{id}` | `notifiers.edit` |
| Delete | `DELETE /api/notificationchannels/{id}` | `notifiers.delete` |
| Send a test message | `POST /api/notificationchannels/{id}/test`, answers `{ sent, error }` | `notifiers.edit` |
| Sends through one channel | `GET /api/notifications?filter=ChannelId:1:{id}` | `notifiers.view` |

## Subscription notifications

A subscription's **Notifications** card sends through channels when its exchanges finish or it goes quiet. Each row picks a channel and what it sends on, so failures can go to one team and successes to another. The card is on the subscription's page and on every create page.

| Tick | Sends when |
|---|---|
| Fails | An exchange failed |
| Bad result | An exchange finished, but its response was flagged bad |
| Succeeds | An exchange finished with a good response |
| Goes quiet | The subscription [expects traffic](entry-points.md#when-traffic-stops) and none came, and again when exchanges are back |

A row ticked **Fails** also hears when the subscription [pauses itself](#auto-pause-alerts). **Goes quiet** can be a row's only tick.

In the API these are the subscription's `notifications`, each `{ channelId, onFailure, onBadResult, onSuccess, onQuiet }`. Leaving `notifications` out of an update keeps what is stored, and an empty list clears it. A row with nothing ticked, a channel listed twice or a channel that doesn't exist is refused.

An Email, Teams or Slack channel sends a message Bitween writes. Any other adapter receives a JSON document describing the result, with PascalCase fields: `Id`, `Success`, `Exception`, `StartedOn`, `FinishedOn`, `OutputBad`, `ResponseBad`, `SubscriptionId`, `SubscriptionName`, `DocumentId`, `DocumentName`, `CorrelationId`, `SuppressedSinceLast` and `Event`. Its properties also get `xchangeid`. `Event` is null for an exchange's result, and `gone-quiet` or `back` when the subscription goes quiet or comes back.

Notifications run on each work group's `-Result` queue, separate from exchange processing. `GET /api/notifications` searches what was sent. Each row names its channel, the subscription it reported, and whether it was held back.

An exchange goes on the `-Result` queue only when one of the subscription's notifications or an active legacy notifier covers its outcome, decided as its result is saved. A notification added or changed while an exchange is running is therefore not sent for that exchange; the next one hears.

## Legacy notifiers

Notifiers made before channels still run, alongside the subscriptions' own notifications. **Settings > Notification channels** links to their page, where they can be viewed, changed and deleted. The page no longer offers new ones, though `POST /api/notifiers` still makes one.

| Field | Meaning |
|---|---|
| Name | |
| Handler and properties | Any handler adapter, such as `NativeSmtpHandler` or `NativeHttpHandler` |
| Run on successful result | Fires for successes with a good response |
| Run on bad result | Fires for successes whose response was flagged bad |
| Run on failed result | Fires for failures, and when a subscription it watches goes quiet or comes back |
| Watches | The subscriptions it listens to. A notifier that watches nothing never fires. |
| Inactive | Switches it off |

Its handler receives the same JSON document as a channel on another adapter, with the same quiet window and up to three tries. Partner and global tokens are not resolved in its properties. Its secret properties come back masked, as a channel's do.
