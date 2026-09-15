# API reference

## Conventions

- The base path is `/api`. Swagger UI is at `/swagger`, with the spec at `/api/swagger.json`.
- Admin endpoints need `Authorization: Bearer <jwt>` from [sign-in](security.md#sign-in), plus the permission listed.
- Partner endpoints need the `partnerkey` header instead.
- Responses use camelCase property names. Requests are accepted in any casing. Enums are strings, and numbers are accepted too.
- Adapter and partner property sets are lists of `{ "key": "...", "value": "..." }`.

### Errors

| Status | Meaning |
|---|---|
| 400 | Validation failed. The body usually maps an error code to messages, such as `{ "ALREADY_RETRIED": ["..."] }`. |
| 401 | Not signed in, or missing the permission |
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
| `POST /api/accounts/login` | none | Sign in with a username and password, a Microsoft token, or the refresh cookie. Returns `{ jwt }`. |
| `POST /api/accounts/logout` | none | Deletes the refresh token and clears site data |
| `GET /api/accounts/profile` | signed in | The current member, roles and permissions |
| `POST /api/accounts/changePassword` | signed in | Change your own password |
| `GET /api/settings/config` | none | Sign-in options and theme |
| `GET /api/settings/myversion` | signed in | API version |
| `GET /api/permissions` | signed in | The permission catalogue |

## Exchanges

| Method and path | Permission | Description |
|---|---|---|
| `GET /api/xchanges` | `exchanges.view` or `dashboard.view` | Searchy. See the filters below. |
| `GET /api/xchanges/statuslist` | `exchanges.view` | Status values for filters |
| `GET /api/xchanges/retrytree?id=` | `exchanges.view` | The retry chain any attempt belongs to |
| `POST /api/xchanges` | signed in, no permission checked | Create an exchange by hand |
| `POST /api/xchanges/{id}/retry` | `exchanges.operate` | `{ reason, reset }` |
| `POST /api/xchanges/bulkretrypreview` | `exchanges.operate` | Plan a bulk retry without running it |
| `POST /api/xchanges/bulkretry` | `exchanges.operate` | Run a bulk retry |
| `GET /api/bitweendocs?documentKey=` | none | Read a stored file |
| `GET /api/delayedretries` | `exchanges.view` or `dashboard.view` | Searchy. Waiting automatic retries. |
| `POST /api/delayedretries/{id}/runnow` | `exchanges.operate` | Run a waiting retry now |

Exchange search filters, besides ordinary fields:

| Filter | Meaning |
|---|---|
| `StatusFilter:1:{n}` | 0 processing, 1 success, 2 bad response, 3 failed. Other values are refused. |
| `LatestOnly:1:true` | Only the newest attempt of each retry chain |

Bulk retry takes `{ ids, filter, excludeIds, reason, reset }`. `filter` is the same query-string fragment the search takes, such as `filter=StatusFilter:1:3&filter=LatestOnly:1:true`, and it replaces `ids` when present. The plan returned by both endpoints lists how many were selected and will be retried, attempts substituted with the end of their chain, and skipped exchanges with reasons. At most 500 exchanges can be retried at once.

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
| `POST /api/subscriptions/{id}/aggregatenow` | `subscriptions.operate` |
| `POST /api/subscriptions/{id}/savemapper` | `subscriptions.edit`. `{ mapperId, mapperProperties }` |
| `POST /api/subscriptions/{id}/retryusage` | `subscriptions.view` |
| `POST /api/subscriptions/{id}/resetretryusage` | `subscriptions.operate` |
| `GET /api/subscriptions/runs?subscriptionId=&limit=` | `subscriptions.view` |
| `GET /api/subscriptions/receiveattempts?subscriptionId=&outcome=&offset=&limit=` | `subscriptions.view` |
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

## Adapters and mapping

| Method and path | Permission | Description |
|---|---|---|
| `GET /api/adapters/Catalog?prefix=` | `subscriptions.view` | Every adapter of one kind with its properties. `prefix` is `receivers`, `handlers`, `mappers` or `validators`. |
| `GET /api/adapters?prefix=` | `subscriptions.view` | Adapter ids of one kind |
| `GET /api/adapters/Versioned?prefix=` | `subscriptions.view` | Adapter ids with versions |
| `GET /api/adapters/{id}/GetStartupValues` | `subscriptions.view` | One adapter's properties |
| `GET /api/adapters/{id}/properties` | `subscriptions.view` | |
| `GET /api/adapters/{id}/Metadata` | `subscriptions.view` | A custom adapter's package metadata |
| `POST /api/mappingpreviews` | signed in, no permission checked | Preview rules-based mapping |
| `POST /api/mappers` | `subscriptions.edit` | Preview a legacy Scriban template |

Adapter descriptions are cached per node, so a newly uploaded package version can show old properties for a while.

## Gateways

| Method and path | Permission |
|---|---|
| `GET /api/apigateways`, `GET /api/apigateways/{id}` | `api-gateways.view` |
| `GET /api/apigateways/attachments?apiGatewayId=&search=&offset=&limit=` | `api-gateways.view` |
| `POST /api/apigateways` | `api-gateways.create`. `{ name, urlName, inactive }` |
| `POST /api/apigateways/{id}` | `api-gateways.edit` |
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
| `GET /api/partners`, `GET /api/partners/{id}` | `partners.view`. Keys are masked. |
| `POST /api/partners` | `partners.create` |
| `POST /api/partners/{id}` | `partners.edit`. Replaces name, properties and API keys. |
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
| `GET /api/notifiers`, `GET /api/notifiers/{id}` | `notifiers.view` |
| `POST /api/notifiers` | `notifiers.create` |
| `POST /api/notifiers/{id}` | `notifiers.edit` |
| `DELETE /api/notifiers/{id}` | `notifiers.delete` |
| `GET /api/notifications` | `notifiers.view`. Searchy. |

## Administration

| Method and path | Permission |
|---|---|
| `GET /api/accounts` | `users.view` |
| `POST /api/accounts` | `users.create` |
| `POST /api/accounts/{id}` | `users.edit`, or yourself for the display name |
| `POST /api/accounts/{id}/setRoles` | `users.edit` |
| `POST /api/accounts/{id}/setDisabled` | `users.edit` |
| `POST /api/accounts/{id}/setPassword` | `users.edit` |
| `POST /api/accounts/{id}/unlock` | `users.edit` |
| `POST /api/accounts/{id}/remove` | `users.delete` |
| `GET /api/roles`, `GET /api/roles/{id}` | `roles.view` |
| `POST /api/roles` | `roles.create` |
| `POST /api/roles/{id}` | `roles.edit` |
| `DELETE /api/roles/{id}` | `roles.delete` |
| `GET /api/settings` | `settings.view` |
| `POST /api/settings/{key}` | `settings.edit`. `{ value }` |
| `DELETE /api/settings/{key}` | `settings.edit`. Resets to the product default. |
| `GET /api/audit?offset=&limit=&entityName=&entityKey=&userId=&correlationId=&from=&to=` | `audit.view` |

## Monitoring

| Method and path | Permission |
|---|---|
| `GET /api/ops/summary`, `consumers`, `queues`, `retries`, `deadletters`, `alerts`, `unattendedqueues` | `monitoring.view` or `dashboard.view` |
| `GET /api/dashboard/MainInfo` | `dashboard.view` |
| `GET /api/dashboard/ChartsDataPoints` | `dashboard.view` |
| `GET /api/dashboard/XChangesAndSubscriptionsInfo` | `dashboard.view` |
| `GET /api/dashboard/retrysummary` | `dashboard.view`. Retries finished in the last 7 days, and the five failing chains with the most attempts. |
| `GET /health` | none |
