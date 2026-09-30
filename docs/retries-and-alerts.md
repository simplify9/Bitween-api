# Retries and alerts

## Manual retry

The Exchanges page offers retry to members with `exchanges.operate`, for one exchange, a selection, or every exchange matching the current filters.

```http
POST /api/xchanges/{id}/retry
Content-Type: application/json

{ "reason": "Partner fixed their endpoint", "reset": true }
```

- A retry is a new exchange with the original input, and `retryFor` set to the original's id.
- **Each exchange can be retried once.** A second retry of the same exchange is refused with `ALREADY_RETRIED`, so you continue from its newest attempt. The attempts form a chain, shown in the exchange drawer with each attempt named by its promoted properties, and returned by `GET /api/xchanges/retrytree?id=`.
- With `reset: false`, the retry reuses the mapper, handler and properties stored on the original exchange. This works even when the subscription has since been deleted.
- With `reset: true`, shown in the UI as *Re-resolve adapter properties*, the retry uses the subscription's current configuration and resolves partner and global tokens again.
- Retrying an exchange that has an automatic retry waiting is refused. Use **Run now** on the waiting retry instead.
- Retry policies ignore manual retries. A manual retry never spends a budget and never starts an automatic chain, and its result says so.

### Bulk retry

`POST /api/xchanges/bulkretry` retries either a list of `ids`, or everything a search `filter` matches minus `excludeIds`. `POST /api/xchanges/bulkretrypreview` returns the same plan without running it, and the UI shows that plan before you confirm.

- A bulk retry covers at most 500 exchanges. A larger selection is refused, so narrow the filter.
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
| `alertMode`, `alertChannelId` | Where the budget-exhausted alert goes: the [notification channel](#notification-channels) it is sent through. |

For `Error`, matchers look at the full exception text. For `BadResult`, they look at the response body.

### Matchers

| `type` | Works on | Fields | Matches when |
|---|---|---|---|
| `contains` | Error, BadResult | `value`, `caseSensitive` (default false) | The text contains `value` |
| `regex` | Error, BadResult | `pattern`, `flags` (default `"i"`; `""` is case-sensitive) | The .NET regular expression matches within 200 ms |
| `exceptionType` | Error | `value`, `includeInner` (default true) | An exception type named in the text equals `value`, short or fully qualified. With `includeInner` off, only the outermost exception counts. |
| `jsonPath` | BadResult | `path`, `op`, `value` | The JSON body meets the condition. `path` uses dots and `[index]`. `op` is `Eq`, `Neq`, `Contains`, `Regex`, `Exists` or `NotExists`, ignoring case. Invalid JSON never matches. |

There is no status-code matcher. To match an HTTP status, use `contains` or `regex` on the exception text or body.

### Delay strategies

The index is 0 for the first retry.

| `type` | Fields | Delay |
|---|---|---|
| `fixed` | `delayMs` | Always `delayMs` |
| `linear` | `initialDelayMs`, `incrementMs` | `initialDelayMs + index × incrementMs` |
| `exponential` | `initialDelayMs`, `multiplier` (default 2), `maxDelayMs` (default 30000) | `initialDelayMs × multiplier^index`, capped at `maxDelayMs` |

### Budgets

A budget sets two limits.

- **`maxAttemptsPerError`** caps retries of one message. The count is the length of the exchange's retry chain.
- **`maxAttemptsTotal`** is a total shared by every message of one subscription in one group. It is not a rolling window. It only resets when someone resets it or when the subscription next succeeds.

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
8. Otherwise a retry is scheduled after the delay.

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

## Scheduled retries

An automatic retry waits as a *delayed retry* until its time comes.

- The retry job looks for due retries every minute by default. The **Retry poll schedule** setting changes this, and saving it reschedules the job at once.
- A due retry creates a new exchange from the original input with the subscription's **current** configuration, then deletes the waiting record. If the new exchange fails too, it is evaluated with the next attempt number.
- When the original exchange, its subscription or its input file is gone, or the exchange has already been retried, the waiting record is dropped and the reason is written on the original result.
- A retry that throws is logged and removed, so one bad record cannot block the rest.

The Scheduled retries page lists waiting retries. **Run now**, `POST /api/delayedretries/{exchangeId}/runnow`, runs one straight away and needs `exchanges.operate`.

## Budget-exhausted alerts

When a group's shared total runs out, Bitween sends one alert through a [notification channel](#notification-channels). The most specific level that decides wins.

| Level | Where it is set |
|---|---|
| One subscription in one group | **Route alert** in the policy page's usage panel |
| Group | The group's alert settings |
| Policy | The policy-wide alert card. Inline policies have no policy level. |

At each level, `Silent` stops the alert, `Send` with a channel sends it there, and `Inherit` defers to the level above. `Send` without a channel also defers. When no level decides, nobody is alerted.

Alerts set up before channels existed carried their own handler at each level. On the first start after upgrading, each different handler setup becomes one channel named *Retry alert: …*, and every level that used it points at that channel.

The channel's handler receives a JSON document with the exchange id, subscription id and name, information type name, correlation id, policy name, group name, total budget, blocked reason, exception and time.

Each delivery is recorded as a notification named *Retry budget alert*. Once one delivery succeeds, a redelivered event does not send a second alert.

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

## Notification channels

A notification channel is where a notification goes and everything needed to deliver it. It is a handler adapter, built in or custom, with its properties: the mail relay and its login, the recipients, a webhook URL. Channels are set up once in **Settings › Notification channels**, and every place that notifies picks one by name:

- a subscription's own notifications, below;
- a retry policy's budget-exhausted alerts, at the policy, group or single-subscription level.

A channel is complete. The places that use it only choose when to send, so every use of a channel sends exactly what it holds. For different recipients, make another channel.

- Names are unique.
- Secret properties come back masked. Saving the mask keeps the stored value, but only while the handler stays the same. After a handler change, type the secret again.
- A mail channel with a password and `UseTls` off is refused.
- A channel that anything still uses cannot be deleted. The Settings list shows who uses each one.
- Channel properties do not resolve partner or global tokens.

| Action | Endpoint | Permission |
|---|---|---|
| List channels, with who uses each | `GET /api/notificationchannels` | `notifiers.view` |
| Channel names for pickers | `GET /api/notificationchannels?lookup=true` | signed in |
| One channel | `GET /api/notificationchannels/{id}` | `notifiers.view` |
| Create | `POST /api/notificationchannels` `{ name, handlerId, handlerProperties }` | `notifiers.create` |
| Change | `POST /api/notificationchannels/{id}` | `notifiers.edit` |
| Delete | `DELETE /api/notificationchannels/{id}` | `notifiers.delete` |

## Subscription notifications

A subscription's **Notifications** card sends a notification when its exchanges finish. Each row picks a channel and the outcomes it sends on, so failures can go to one team and successes to another.

| Outcome | Sends when |
|---|---|
| Fails | The exchange threw |
| Bad result | The exchange finished, but its response was flagged bad |
| Succeeds | The exchange finished with a good response |

In the API these are the subscription's `notifications`, a list of `{ channelId, onFailure, onBadResult, onSuccess }`. Leaving `notifications` out of an update keeps what is stored, and an empty list clears it. A row with no outcome, or a channel listed twice, is refused.

The handler receives a JSON document describing the result, with PascalCase fields: `Id`, `Success`, `Exception`, `StartedOn`, `FinishedOn`, `OutputBad`, `ResponseBad`, `SubscriptionId`, `SubscriptionName`, `DocumentId`, `DocumentName` and `CorrelationId`. Its properties also get `xchangeid`.

Each send is recorded with the channel's name, its outcome and any error, and `GET /api/notifications` searches them. Notifications run on each work group's `-Result` queue, separate from exchange processing.

## Legacy notifiers

Notifiers made before channels still run, alongside the subscriptions' own notifications. They can be viewed, changed and deleted from the link in **Settings › Notification channels**, but the page no longer offers new ones. A subscription shows the legacy notifiers that watch it.

A legacy notifier watches a list of subscriptions and sends through its own handler when an outcome it cares about happens. One that watches nothing never sends. Its handler gets the same JSON document as a channel does.
