# API reference

## Conventions

- The base path is `/api`. Swagger UI is at `/swagger`, with the spec at `/api/swagger.json`.
- Admin endpoints need `Authorization: Bearer <jwt>` from [sign-in](security.md#sign-in), plus the permission listed.
- Partner endpoints need a partner key instead: the key header (`partnerkey` unless renamed, and `partnerkey` always works), `Authorization: Bearer <key>`, or Basic auth. An API gateway set to take tokens needs `Authorization: Bearer <token>` from its login server.
- Responses use camelCase property names. Requests are accepted in any casing. Enums are strings, and numbers are accepted too.
- Adapter and partner property sets are lists of `{ "key": "...", "value": "..." }`.

### Errors

| Status | Meaning |
|---|---|
| 400 | Validation failed. The body usually maps an error code to messages, such as `{ "ALREADY_RETRIED": ["..."] }`. |
| 401 | Not signed in, or the token has expired |
| 403 | Signed in, but missing the permission |
| 404 | Not found |

### Search queries

Endpoints marked *searchy* accept these parameters.

| Parameter | Example | Meaning |
|---|---|---|
| `filter` | `filter=DocumentId:1:3` | `Field:Rule:Value`, repeatable. The UI uses rule 1 for equals and 4 for contains. |
| `sort` | `sort=StartedOn:2` | `Field:Order`, where 2 is descending |
| `page`, `size` | `page=0&size=25` | Paging. Always send `size`. |
| `lookup` | `lookup=true` | Return an id-to-name map instead of rows |

They return `{ "result": [...], "totalCount": 123 }`.

## Partner endpoints

| Method and path | Description |
|---|---|
| `POST /api/gateway/{urlName}/async` | Submit to an API gateway. Returns 202 with the exchange id. |
| `POST /api/gateway/{urlName}/sync` | Submit and wait for the result. Optional `Wait-Period` header. |
| `POST /api/xchanges/{informationTypeIdOrName}` | Legacy API call. Optional `waitresponse` header. |
| `GET /api/xchanges/{exchangeId}` | Legacy result lookup for the calling partner's own exchanges |

See [Entry points](entry-points.md) for status codes.

## Session and account

| Method and path | Permission | Description |
|---|---|---|
| `POST /api/accounts/login` | none | Sign in with a username and password, a Microsoft token, an OpenID Connect code (`{ oidcCode, oidcState }`), or a refresh token (the refresh cookie, or `refreshToken` in the body). Returns `{ jwt }`. |
| `POST /api/accounts/oidcstart` | none | Starts an [OpenID Connect sign-in](security.md#openid-connect): returns `{ url }`, the provider's sign-in address, and sets the cookie the sign-in is finished with. |
| `POST /api/accounts/logout` | none | Deletes the refresh token and clears site data. A client without cookies sends `{ refreshToken }`. |
| `POST /api/accounts/cligrant` | signed in | Signs the bitween CLI in as the caller: `{ codeChallenge }`, the CLI's S256 PKCE challenge. Returns `{ code }`, good for two minutes. The admin UI's `/cli-login` page calls it after the member confirms. |
| `POST /api/accounts/clitoken` | none | The CLI trades that code for a session: `{ code, codeVerifier }`. Returns `{ jwt, refreshToken, email }`. |
| `POST /api/accounts/servicetoken` | none | A service account signs in: `{ token }`, one of its `bwt_` tokens. Returns `{ jwt, expiresIn, id, name }`, with no refresh token: trade the token again when the session ends. Shares the sign-in rate limit; unknown tokens count toward the address's lockout. |
| `GET /api/accounts/profile` | signed in | The current member, roles and permissions |
| `POST /api/accounts/changePassword` | signed in | Change your own password |
| `GET /api/settings/config` | none | Sign-in options and theme |
| `GET /api/settings/myversion` | signed in | API version |
| `GET /api/permissions` | signed in | The permission catalogue |

## Connections

All need the global values permissions: `global-values.view` to read, `.create`, `.edit` and `.delete` to change. Secrets come back as `__private__`.

| Method and path | Permission | Description |
|---|---|---|
| `GET /api/connections` | view | Every connection, with its status and the reference settings write |
| `GET /api/connections/{id}` | view | One connection |
| `POST /api/connections` | create | `{ name, grant, authorizeUrl, tokenUrl, clientId, clientSecret, scopes, authorizeParameters, tokenAuth, username, password }`; `grant` is `ClientCredentials`, `AuthorizationCode` or `Password` |
| `POST /api/connections/{id}` | edit | The same; a secret sent as `__private__`, or left out, is kept |
| `DELETE /api/connections/{id}` | delete | Refused while settings refer to it |
| `POST /api/connections/{id}/authorize` | edit | Returns `{ url }`, the provider's approval page, and sets the cookie the approval is finished with |
| `POST /api/connections/complete` | edit | `{ code, state }` from the provider's redirect to `/oauth-callback`. Returns `{ id, name }` |
| `POST /api/connections/{id}/test` | edit | Renews the token now and returns the connection |

## Exchanges

| Method and path | Permission | Description |
|---|---|---|
| `GET /api/xchanges` | `exchanges.view` or `dashboard.view`, or signed in with `lookup=true` | Searchy. See the filters below. A lookup returns at most 100 exchange ids, newest first, each with its information type's name. |
| `GET /api/xchanges/statuslist` | `exchanges.view` | Status values for filters |
| `GET /api/xchanges/retrytree?id=` | `exchanges.view` | The retry chain any attempt belongs to |
| `POST /api/xchanges` | signed in, no permission checked | Create an exchange by hand |
| `POST /api/xchanges/{id}/retry` | `exchanges.operate` | `{ reason, reset }` |
| `POST /api/xchanges/bulkretrypreview` | `exchanges.operate` | Plan a bulk retry without running it |
| `POST /api/xchanges/bulkretry` | `exchanges.operate` | Run a bulk retry |
| `POST /api/xchanges/export` | `exchanges.view` | The selected exchanges' files as one zip |
| `POST /api/xchanges/export/check` | `exchanges.view` | How many of the selected exchanges are older than storage keeps their files, asked before exporting |
| `GET /api/bitweendocs?documentKey=` | `exchanges.view` | Read one of an exchange's files. Other keys are refused. |
| `GET /api/files/{seal}/{key}` | none, the seal | An exchange file for a reader without a login, served as `text/plain` |
| `GET /api/delayedretries` | `exchanges.view` or `dashboard.view` | Searchy. Waiting automatic retries. |
| `POST /api/delayedretries/{id}/runnow` | `exchanges.operate` | Run a waiting retry now |

Exchange search filters, besides ordinary fields:

| Filter | Meaning |
|---|---|
| `StatusFilter:1:{n}` | 0 processing, 1 success, 2 bad response, 3 failed. Other values are refused. |
| `LatestOnly:1:true` | Only the newest attempt of each retry chain |

Bulk retry takes `{ ids, filter, excludeIds, reason, reset }`. `filter` is the same query-string fragment the search takes, such as `filter=StatusFilter:1:3&filter=LatestOnly:1:true`, and it replaces `ids` when present. The plan returned by both endpoints lists how many were selected and will be retried, attempts substituted with the end of their chain, and skipped exchanges with reasons. At most 500 exchanges can be retried at once.

Export takes `{ ids, filter, excludeIds }`, chosen the same way, and streams back a zip (`application/zip`) with a folder per exchange and a `missing.txt` listing files that storage no longer has. At most 500 exchanges go in one zip; a larger selection is refused with `TOO_MANY`. `export/check` takes the same body and returns `{ count, withoutFiles, keptDays }`: how many exchanges the zip would take, how many of them are older than the bucket's rule keeps files, and that rule's days when one covers them all.

The retry tree returns `{ rootId, nodes, truncated }`. Each node has its id, `retryFor`, promoted properties, times, status, exception, whether it was manual, and any scheduled retry or blocked reason. It stops at 100 levels or 500 attempts.

## Subscriptions

| Method and path | Permission |
|---|---|
| `GET /api/subscriptions` | `subscriptions.view`. Searchy. |
| `GET /api/subscriptions/{id}` | `subscriptions.view` |
| `POST /api/subscriptions` | `subscriptions.create` |
| `POST /api/subscriptions/{id}` | `subscriptions.edit`. Replaces the whole configuration. |
| `DELETE /api/subscriptions/{id}` | `subscriptions.delete`. Refused while a gateway, route, response link or aggregation points at it. |
| `POST /api/subscriptions/{id}/pause` | `subscriptions.operate`. Toggles pause. |
| `POST /api/subscriptions/{id}/receivenow` | `subscriptions.operate` |
| `POST /api/subscriptions/{id}/clearrunning` | `subscriptions.operate`. Clears a running flag a dead run left behind, so the next scheduled run starts. Refused with `STILL_RUNNING` while the job is running on the node that answers. Audited. Returns `{ id, cleared }`. |
| `POST /api/subscriptions/{id}/aggregatenow` | `subscriptions.operate` |
| `POST /api/subscriptions/{id}/savemapper` | `subscriptions.edit`. `{ mapperId, mapperProperties }` |
| `POST /api/subscriptions/{id}/retryusage` | `subscriptions.view` |
| `POST /api/subscriptions/{id}/resetretryusage` | `subscriptions.operate` |
| `GET /api/subscriptions/runs?subscriptionId=&limit=` | `subscriptions.view` |
| `GET /api/subscriptions/history?subscriptionId=` | `subscriptions.view`. Its versions, newest first: `[{ number, savedOn, savedBy, changed }]`, `changed` naming the settings that differ from the version before. |
| `GET /api/subscriptions/revision?subscriptionId=&number=` | `subscriptions.view`. `{ number, savedOn, configuration }`, the configuration in the shape `GET /api/subscriptions/{id}` answers, secrets as `__private__`. Restoring it is posting it to `POST /api/subscriptions/{id}`. |
| `GET /api/subscriptions/receiveattempts?subscriptionId=&outcome=&offset=&limit=` | `subscriptions.view` |
| `GET /api/subscriptions/bookmark?subscriptionId=` | `subscriptions.view`. `{ value, updatedOn }`: where a receiver that [keeps a bookmark](adapter-contract.md#receivers-that-keep-a-bookmark) last got to; `value` is null when there is none. |
| `POST /api/subscriptions/{id}/forgetbookmark` | `subscriptions.operate`. Forgets it, so the next run starts from the beginning. In the audit trail. |
| `GET /api/subscriptions/sourcepaths?subscriptionId=` | `exchanges.view`. The paths in the last document it received, with their values: what a response or bus gateway subscription it feeds can read. |
| `GET /api/subscriptions/lastruns` | `subscriptions.view` |
| `GET /api/subscriptions/schedulehealth` | `subscriptions.view` |
| `GET /api/subscriptioncategories` | `subscriptions.view` |
| `POST /api/subscriptioncategories` | `subscriptions.create` |
| `POST /api/subscriptioncategories/{id}` | `subscriptions.edit` |
| `POST /api/subscriptioncategories/{id}/delete` | `subscriptions.delete` |

A scheduled job that pulls files from S3:

```json
{
  "name": "Orders from S3",
  "type": "Receiving",
  "documentId": 3,
  "partnerId": 5,
  "receiverId": "NativeS3Receiver",
  "receiverProperties": [
    { "key": "ServiceUrl", "value": "https://s3.example.com" },
    { "key": "BucketName", "value": "incoming" },
    { "key": "AccessKeyId", "value": "{{globals.s3.accessKeyId}}" },
    { "key": "SecretAccessKey", "value": "{{globals.s3.secretAccessKey}}" }
  ],
  "handlerId": "NativeHttpHandler",
  "handlerProperties": [ { "key": "Url", "value": "{{partner.erpUrl}}/orders" } ],
  "schedules": [ { "recurrence": "Hourly", "days": 0, "hours": 0, "minutes": 15 } ],
  "retryPolicyId": 1,
  "inactive": false
}
```

A subscription bound to a data source also carries `dataSourceId`.

`expectTraffic` asks to be told when the subscription stops receiving exchanges. See [Entry points](entry-points.md#when-traffic-stops). It is replaced like the rest of the configuration, so an update without it removes it.

```json
"expectTraffic": { "everyMinutes": 15, "days": ["Sun", "Mon", "Tue", "Wed", "Thu"], "from": "08:00", "to": "22:00", "timeZone": "Asia/Riyadh" }
```

| Field | Rule |
|---|---|
| `everyMinutes` | Required, 5 to 10080. When a window has hours, no longer than the window. |
| `days` | `Sun` to `Sat`, any case; stored in week order. Null or empty means every day. |
| `from`, `to` | `HH:mm`, both or neither. `to` later than `from`; a window can't run past midnight. |
| `timeZone` | An IANA name such as `Asia/Riyadh`. Null means UTC. |

A value that breaks a rule is refused with a validation error whose message says which. `GET /api/subscriptions/{id}` and the search also return `quietSince`: when the subscription was found quiet, or null. It can't be set through the API.

## Adapters and mapping

| Method and path | Permission | Description |
|---|---|---|
| `GET /api/adapters/Catalog?prefix=` | `subscriptions.view` | Every adapter of one kind with its properties. `prefix` is `receivers`, `handlers`, `mappers` or `validators`. Includes adapters published only to the catalog. Each entry of `versionHistory` has `hasSource` and `runtime`. |
| `GET /api/adapters?prefix=` | `subscriptions.view` | Adapter ids of one kind |
| `GET /api/adapters/Versioned?prefix=` | `subscriptions.view` | Adapter ids with versions |
| `GET /api/adapters/{id}/GetStartupValues` | `subscriptions.view` | One adapter's properties |
| `GET /api/adapters/{id}/properties` | `subscriptions.view` | |
| `GET /api/adapters/{id}/Metadata` | `subscriptions.view` | A custom adapter's package metadata |
| `GET /api/adapters/source?adapterId=&version=` | `adapter-source.view` | The source files a published version carries, each with its SHA-256 |
| `GET /api/adapters/sourcefile?adapterId=&version=&path=` | `adapter-source.view` | One source file, checked against its manifest hash. Recorded in the audit trail. |
| `GET /api/adapters/versions?adapterId=` | `subscriptions.view` | Every published version of one adapter, with which is current and which are withdrawn |
| `POST /api/adapters/packages?version=&current=&releaseNotes=` | `adapter-source.operate` | Publish a package built by `bitween adapter build`. The body is the zip itself (`application/zip`), up to 200 MB. `version` is `major`, `minor`, `patch` or an exact version, the package's own when empty; `current=true` makes it current; `releaseNotes` replaces the package's own. Returns `{ adapterId, version, current, sha256 }`. A refusal is 400 with `{ field: [message] }`. |
| `POST /api/adapters/promote` | `adapter-source.operate` | Make a published version current: `{ adapterId, version }` |
| `GET /api/marketplace/search?q=&tier=` | `subscriptions.view` | What the registry set under Settings lists: `{ configured, disabled, registry, result: [{ card, installed }], totalCount }`. `configured` is false, and nothing is fetched, when the marketplace is off or has no address. `installed` says what this Bitween has of each: `{ installed, fromRegistry, current, versions }`. |
| `GET /api/marketplace/package?id=` | `subscriptions.view` | One registry package with its versions, the newest version's manifest, and what this Bitween has of it. |
| `POST /api/marketplace/install` | `adapter-source.operate` | Install a version from the registry: `{ id, version, current }`. Every signature is checked first; see [Adapters](adapters.md#installing-from-the-marketplace). Returns `{ adapterId, version, current, sha256, publisher }`. Audited as an `AdapterRelease` with the action `installed`. |
| `POST /api/adapters/withdraw` | `adapter-source.operate` | Take a published version out of use: `{ adapterId, version }`. It stays listed but can't be pinned or made current. |
| `GET /api/adapterdrafts?adapterId=` | `adapter-source.edit` | The editor's drafts, newest first, optionally for one adapter |
| `GET /api/adapterdrafts/{id}` | `adapter-source.edit` | A draft with its files |
| `POST /api/adapterdrafts` | `adapter-source.edit` | Start a draft: `{ name, language, kind, adapterId }`, where `language` is `python`, `node` or `typescript` and `adapterId` comes from the name when left out; or `{ fromAdapterId, fromVersion }`. Returns the draft's id. |
| `POST /api/adapterdrafts/{id}` | `adapter-source.edit` | Save its files: `{ files: { path: content }, baseHash }`. With `baseHash`, the draft's `filesHash` when it was opened, a save made after someone else's is refused with `DRAFT_CHANGED`; without it the save goes through. |
| `DELETE /api/adapterdrafts/{id}` | `adapter-source.edit` | Delete a draft |
| `POST /api/adapterdrafts/{id}/build` | `adapter-source.edit` | Build and check it: `{ settings, buildOnly }` |
| `POST /api/adapterdrafts/{id}/try` | `adapter-source.edit` | Call a command: `{ settings, command, input }` |
| `POST /api/adapterdrafts/{id}/publish` | `adapter-source.operate` | Publish a version, not made current: `{ version, releaseNotes, settings }`. `version` is `major`, `minor`, `patch` (the default) or an exact version. |
| `POST /api/mappingpreviews` | signed in, no permission checked | Preview rules-based mapping |
| `POST /api/mappers` | `subscriptions.edit` | Preview a legacy Scriban template |

Adapter descriptions are cached per node. Publishing, promoting or withdrawing through the API clears them on every node; a package published straight to storage shows its old properties for up to a minute.

Publishing a package, promoting, withdrawing, and publishing from a draft are recorded in the audit trail as `AdapterRelease` rows. Drafts are recorded as `AdapterDraft` rows, with a hash of their files rather than the files.

## Gateways

| Method and path | Permission |
|---|---|
| `GET /api/apigateways`, `GET /api/apigateways/{id}` | `api-gateways.view` |
| `GET /api/apigateways/{id}/rejections` | `api-gateways.view`. Calls to the gateway turned away since the answering node started, counted by reason (`not-authenticated`, `not-attached`, `gateway-off`, `no-subscription`, `rate-limited`, `unknown-gateway`), with the most recent: when, status and caller address. Kept in memory per node. |
| `GET /api/apigateways/attachments?apiGatewayId=&search=&offset=&limit=` | `api-gateways.view` |
| `POST /api/apigateways` | `api-gateways.create`. `{ name, urlName, inactive, authentication?, failureAnswer? }`. `authentication` is `{ method: "PartnerKey" \| "Jwt", keyHeader, issuer, audience, partnerClaim }`, and defaults to partner keys. An empty `keyHeader` uses the one in Settings. `failureAnswer` is `"Accurate"` (the default for a new gateway) or `"Legacy"`; see [Entry points](entry-points.md#async-and-sync). |
| `POST /api/apigateways/{id}` | `api-gateways.edit`. Same body. Leaving `authentication` or `failureAnswer` out keeps what the gateway has. |
| `DELETE /api/apigateways/{id}` | `api-gateways.delete` |
| `POST /api/apigateways/{id}/addpartner` | `api-gateways.edit`. `{ partnerId, subscriptionId }` or `{ partnerId, newIntegration }` |
| `POST /api/apigateways/{id}/updatepartner` | `api-gateways.edit` |
| `POST /api/apigateways/{id}/removepartner` | `api-gateways.edit` |
| `GET /api/busgateways`, `GET /api/busgateways/{id}` | `bus-gateways.view` |
| `POST /api/busgateways` | `bus-gateways.create`. `{ name, documentId, dataSourceId, endpoint }` |
| `POST /api/busgateways/{id}` | `bus-gateways.edit` |
| `DELETE /api/busgateways/{id}` | `bus-gateways.delete` |
| `POST /api/busgateways/{id}/addroute` | `bus-gateways.edit`. `{ subscriptionId or newIntegration, partnerId, matchExpression }` |
| `POST /api/busgateways/{id}/updateroute` | `bus-gateways.edit` |
| `POST /api/busgateways/{id}/removeroute` | `bus-gateways.edit` |

## Data sources

| Method and path | Permission |
|---|---|
| `GET /api/datasources` | `data-sources.view`, or signed in with `lookup=true`. Searchy. |
| `GET /api/datasources/Providers` | `data-sources.view` |
| `GET /api/datasources/{id}` | `data-sources.view` |
| `GET /api/datasources/{id}/telemetry` | `data-sources.view` |
| `POST /api/datasources` | `data-sources.create` |
| `POST /api/datasources/{id}` | `data-sources.edit` |
| `DELETE /api/datasources/{id}` | `data-sources.delete` |
| `POST /api/datasources/{id}/test` | `data-sources.operate` |
| `POST /api/datasources/{id}/inspect` | `data-sources.view`. `{ command: "Discover" \| "GetStats" \| "Describe", arguments }` |
| `GET /api/datasourcestatements`, `GET /api/datasourcestatements/{id}` | `data-source-statements.view` |
| `POST /api/datasourcestatements` | `data-source-statements.create` |
| `POST /api/datasourcestatements/{id}` | `data-source-statements.edit` |
| `DELETE /api/datasourcestatements/{id}` | `data-source-statements.delete` |
| `POST /api/datasourcestatements/{id}/usage` | `data-source-statements.view` |

See [Data sources](data-sources.md) and [Databases](databases.md).

## Configuration

| Method and path | Permission |
|---|---|
| `GET /api/documents`, `GET /api/documents/{id}`, `GET /api/documents/{id}/properties` | `documents.view` |
| `POST /api/documents` | `documents.create` |
| `POST /api/documents/{id}` | `documents.edit` |
| `DELETE /api/documents/{id}` | `documents.delete` |
| `GET /api/partners`, `GET /api/partners/{id}` | `partners.view`. Keys are masked. `{id}` also returns `keysLastUsedOn`: when a gateway last let a call through with each key, by name, to within five minutes. |
| `POST /api/partners` | `partners.create` |
| `POST /api/partners/{id}` | `partners.edit`. Replaces name, properties, login identity and API keys. |
| `DELETE /api/partners/{id}` | `partners.delete` |
| `GET /api/partners/generatekey` | signed in, no permission checked. Returns a random key as text; nothing is stored. |
| `GET /api/globaladaptervaluessets`, `GET /api/globaladaptervaluessets/{id}` | `global-values.view` |
| `POST /api/globaladaptervaluessets` | `global-values.create`. `{ id, name, values }` |
| `POST /api/globaladaptervaluessets/{id}` | `global-values.edit` |
| `POST /api/globaladaptervaluessets/{id}/delete` | `global-values.delete` |
| `GET /api/workgroups?name=&offset=&limit=` | `workgroups.view`. Includes live queue metrics. |
| `POST /api/workgroups` | `workgroups.create`. `{ name, busMessageName, options: { rabbitMqOptions: { prefetch, priority } } }` |
| `POST /api/workgroups/{id}` | `workgroups.edit` |
| `POST /api/workgroups/{id}/delete` | `workgroups.delete`. Refused while subscriptions use it. |
| `GET /api/retrypolicies`, `GET /api/retrypolicies/{id}` | `retry-policies.view` |
| `POST /api/retrypolicies` | `retry-policies.create` |
| `POST /api/retrypolicies/{id}` | `retry-policies.edit` |
| `DELETE /api/retrypolicies/{id}` | `retry-policies.delete` |
| `POST /api/retrypolicies/test` | `retry-policies.view` |
| `POST /api/retrypolicies/{id}/usage` | `retry-policies.view` |
| `POST /api/retrypolicies/{id}/attempts` | `retry-policies.view` |
| `POST /api/retrypolicies/{id}/resetusage` | `retry-policies.edit` |
| `POST /api/retrypolicies/{id}/savealertoverride` | `retry-policies.edit` |
| `GET /api/notificationchannels`, `GET /api/notificationchannels/{id}` | `notifiers.view`. Secret settings come back as `__private__`. |
| `GET /api/notificationchannels?lookup=true` | signed in. Ids and names only, for pickers. |
| `POST /api/notificationchannels` | `notifiers.create`. `{ name, handlerId, handlerProperties }` |
| `POST /api/notificationchannels/{id}` | `notifiers.edit`. A `__private__` value keeps the stored secret only while the adapter and where it connects stay the same. |
| `POST /api/notificationchannels/{id}/test` | `notifiers.edit`. Sends one made-up notification through the channel as saved. Returns `{ sent, error }`. |
| `DELETE /api/notificationchannels/{id}` | `notifiers.delete`. Refused while a subscription still sends through it. |
| `GET /api/notifiers`, `GET /api/notifiers/{id}` | `notifiers.view` |
| `POST /api/notifiers` | `notifiers.create` |
| `POST /api/notifiers/{id}` | `notifiers.edit` |
| `DELETE /api/notifiers/{id}` | `notifiers.delete` |
| `GET /api/notifications` | `notifiers.view`. Searchy. |

## Administration

| Method and path | Permission |
|---|---|
| `GET /api/accounts` | `users.view`. Each member carries `lastSignInOn` and `microsoftLinked`. |
| `POST /api/accounts` | `users.create` |
| `POST /api/accounts/{id}` | `users.edit`, or yourself for the display name |
| `POST /api/accounts/{id}/setRoles` | `users.edit` |
| `POST /api/accounts/{id}/setDisabled` | `users.edit` |
| `POST /api/accounts/{id}/setPassword` | `users.edit` |
| `POST /api/accounts/{id}/unlock` | `users.edit` |
| `POST /api/accounts/{id}/remove` | `users.delete` |
| `GET /api/serviceaccounts`, `GET /api/serviceaccounts/{id}` | `service-accounts.view`. Each with its roles and tokens (`shown` is the first 12 characters, never the token). Service accounts aren't in `GET /api/accounts`. |
| `POST /api/serviceaccounts` | `service-accounts.create`. `{ name, roleIds }`; only roles you hold. |
| `POST /api/serviceaccounts/{id}` | `service-accounts.edit`. `{ name, roleIds }` |
| `POST /api/serviceaccounts/{id}/setDisabled` | `service-accounts.edit`. Stops every token, and sessions already open. |
| `POST /api/serviceaccounts/{id}/issueToken` | `service-accounts.create`. `{ name, expiresInDays }` (1 to 365, 90 by default; two active at most). The answer's `token` is the only time it is shown. |
| `POST /api/serviceaccounts/{id}/revokeToken` | `service-accounts.edit`. `{ tokenId }` |
| `DELETE /api/serviceaccounts/{id}` | `service-accounts.delete`. Revokes its tokens. |
| `GET /api/roles`, `GET /api/roles/{id}` | `roles.view` |
| `POST /api/roles` | `roles.create` |
| `POST /api/roles/{id}` | `roles.edit` |
| `DELETE /api/roles/{id}` | `roles.delete` |
| `GET /api/settings` | `settings.view` |
| `GET /api/cluster/nodes` | `settings.view`. Every node as its heartbeat last described it: host, started, last seen, online (seen in the last 90 seconds), version, whether it runs data sources, its adapter runtimes, the leases it holds and the exclusive data sources it owns. |
| `GET /api/settings/about` | `settings.view`. This instance as the answering node sees it: version, node, database, storage, broker, the readiness checks run now, which adapter runtimes and pip/npm the node has, the adapter editor's limits, rate limits and other effective settings from configuration. Never a secret. |
| `POST /api/settings/{key}` | `settings.edit`. `{ value }` |
| `DELETE /api/settings/{key}` | `settings.edit`. Resets to the product default. |
| `GET /api/retention?refresh=` | `settings.view`. What the retention settings do; `refresh=true` reads the bucket's rules again. |
| `POST /api/retention/preview` | `settings.view`. The same for proposed values, without saving them. |
| `GET /api/audit?offset=&limit=&entityName=&entityKey=&userId=&correlationId=&from=&to=` | `audit.view` |

## Monitoring

| Method and path | Permission |
|---|---|
| `GET /api/ops/summary`, `consumers`, `queues`, `retries`, `deadletters`, `alerts`, `unattendedqueues` | `monitoring.view` or `dashboard.view` |
| `GET /api/ops/background` | `monitoring.view` or `dashboard.view`. The system jobs (automatic retries, exchange retention, receive attempt clean-up, broker deduplication clean-up): cron, whether scheduled, state, last and next run, running now on this node. And the outbox: messages waiting for the broker, the oldest, how many after a failed attempt with the last error, and how many sent in the last hour. |
| `GET /api/dashboard/MainInfo` | `dashboard.view` |
| `GET /api/dashboard/ChartsDataPoints` | `dashboard.view` |
| `GET /api/dashboard/XChangesAndSubscriptionsInfo` | `dashboard.view` |
| `GET /api/dashboard/retrysummary` | `dashboard.view`. Retries finished in the last 7 days, and the five failing chains with the most attempts. |
| `GET /health` | none |
