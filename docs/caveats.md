# Known limitations

These behaviours were found while documenting Bitween from its source code, and re-checked against the code on 11 September 2026. Some are deliberate trade-offs explained in code comments. Others look like gaps worth fixing. Each entry says where to look.

## Security

| Issue | Where |
|---|---|
| The SYSTEM partner's API key is seeded with a fixed value. (The seeded administrator's password is also published, but that account now grants nothing until the password is changed.) | `Data/BitweenDbContext.cs` |
| Microsoft ID tokens are checked for signature and lifetime but not issuer or audience. A valid token from any tenant or app signs in the Bitween account with the same email. The tenant setting only affects the browser popup. | `Extensions/AccountExtensions.cs` |
| Creating an exchange by hand, previewing a rules-based mapping and generating a partner key check no permission. Any signed-in member can call them. | `Resources/Xchanges/Create.cs`, `Resources/MappingPreviews/Preview.cs`, `Resources/Partners/GenerateKey.cs` |
| `GET /api/bitweendocs` needs no sign-in and reads any storage key it is given. | `Resources/BitweenDocs/Get.cs` |
| Partner API keys, and data source settings including passwords, are stored in plain text. A comment on `DataSource` says otherwise. | `Domain/Partner/ApiCredential.cs`, `Domain/DataSources/DataSource.cs` |
| Saving or testing an Oracle statement runs `DBMS_SQL.PARSE`, which executes DDL. | `SW.Bitween.Adapters.Db.Oracle` |
| A subscription using a database or bus adapter without a bound data source returns its connection settings unmasked. | `Services/AdapterSecretProperties.cs` |
| Password hashes created before September 2026 use PBKDF2-SHA1 with 10,000 iterations and are never upgraded on sign-in. The seeded administrator's hash is one of them. | `Services/SecurePasswordHasher.cs` |
| Denied requests return 401 rather than 403. | `Extensions/RequestContextExtensions.cs` |
| An administrator setting another member's password is only held to an 8-character minimum. | `Resources/Accounts/SetPassword.cs` |
| The Helm chart's default token key is committed in `values.yaml`. | `charts/default/values.yaml` |

## Pipeline and retries

| Issue | Where |
|---|---|
| Sync API gateway calls and the legacy `waitresponse` wait compare the back-off step with the requested wait, not elapsed time. The step caps at 8 seconds, so a wait of 8 or more, including the gateway default of 120, never times out. | `Controllers/GatewayController.cs`, `Resources/Xchanges/Update.cs` |
| Pausing only holds messages routed by content, to internal subscriptions and bus gateway routes. Other entry points still create exchanges on a paused subscription. | `Services/XchangeService.cs` |
| Validators only run for API gateways and the legacy API call endpoint. | `Services/XchangeService.cs` |
| Exchanges created by fan-out do not carry the parent's references. | `XchangeService.CreateXchangesForHits` |
| A retry without reset keeps the response subscription but drops the response message type name. | `Domain/Xchange/Xchange.cs` |
| Mappers other than the rules-based mapper fail on payloads that are not JSON, and overwrite payload keys named `__partner__` or `__globals__`. | `XchangeService.RunMapper` |
| An API gateway attached to an inactive subscription returns 404. | `Controllers/GatewayController.cs` |
| The one-retry-per-exchange rule has no unique index behind it, so two retries committed at the same moment both succeed. | `XchangeService.EnsureNotAlreadyRetried` |
| Retrying an id that does not exist fails with an unhandled error instead of a validation message. | `Resources/Xchanges/Retry.cs` |
| Bulk retry includes exchanges that have no result yet, so work still in flight can run twice. | `Resources/Xchanges/BulkRetryPlanner.cs` |
| The dashboard's *Failures to act on* tile says it covers 14 days, but counts failures of any age. | `ClientApp/src/pages/dashboard/DashboardPage.tsx` |
| The information type's duplicate interval is stored but never enforced. | `Domain/Document/Document.cs` |
| An exchange's delivered time is never recorded. | `Domain/XchangeDelivery.cs` |
| Exchanges, results and notifications have no cleanup job, while their files expire from storage. | `Services` |
| Cached configuration can stay stale on other instances for up to 10 minutes if the revoke broadcast fails. Adapter descriptions are cached per node and never revoked. | `Services/Caching/InMemoryInfolinkCache.cs`, `Services/ServerlessAdapterDescriber.cs` |

## Adapters

| Issue | Where |
|---|---|
| The HTTP handler's property descriptions disagree with its behaviour. `Bearer` uses `LoginPassword`; `OAuth2` is the client credentials grant; `Headers` are `Name:Value` pairs separated by commas; `CorrelationId` is sent as a header; `patch` sends a POST. | `NativeAdapters/HttpHandler` |
| The HTTP adapters add authentication headers to an `HttpClient` shared per origin, so headers can accumulate across subscriptions calling the same origin. | `NativeHttpHandler.cs`, `DynamicHttpProxy.cs` |
| The HTTP receiver makes one request per run, with no pagination. | `NativeHttpReceiver.cs` |
| The S3 receiver's folder prefix has no trailing slash. The S3 upload handler ignores the folder when a file name is set. | `NativeS3Receiver.cs`, `NativeS3UploadHandler.cs` |
| POP3 receivers use port 995 only, and read only the first attachment. | `Pop3Receiver`, `RebexPop3Receiver` |
| Property values that fail to convert fall back to the default silently. | `NativeAdapters/ReflectionExtensions.cs` |
| Notifier properties do not resolve partner or global tokens. | `XchangeService.NotifyResult` |

## Data sources

| Issue | Where |
|---|---|
| Leases are checked every 30 seconds, not per message, so an old owner can overlap with a new one for up to that long. | `Services/DataSources/BusProviderSupervisor.cs` |
| The lease queue name ignores `Bitween:QueuePrefix`, so deployments sharing one RabbitMQ virtual host can compete for leases. | `Services/Cluster/RabbitMqLeaderElection.cs` |
| Test, Discover, Stats, the live panel and statement checks run on whichever node serves the request, and report *not running here* elsewhere. | `Resources/DataSources` |
| PostgreSQL has no unique index on data source names. | `SW.Bitween.PgSql/BitweenDbContext.cs` |
| Deleting a data source that subscriptions are bound to fails with a raw database error. | `Resources/DataSources/Delete.cs` |
| Bitween does not check that a subscription's data source matches its adapter. | `Resources/Subscriptions/SubscriptionConfigurationApplier.cs` |
| Health shown for a data source nobody runs is whatever was last recorded. | `BusProviderSupervisor.WriteBackHealthAsync` |
| The Helm chart has no values for the data source settings. | `charts/default/values.yaml` |

## Databases

| Issue | Where |
|---|---|
| The Oracle `Schema` setting does not change name resolution. | `SW.Bitween.Adapters.Db.Oracle` |
| `timestamp+incrementing` has no tie-break column and behaves like `timestamp`. | `Adapters.Db.Core/DbReceiver.cs` |
| Database receiving has no deduplication. A row whose marking fails after its exchange was stored is received again. | `Adapters.Db.Core/DbReceiver.cs` |
| The cursor advances per accepted row, so a statement not ordered by the cursor column can skip rows. | `Adapters.Db.Core/DbReceiver.cs` |
| A delivery's `call` cannot declare output parameters, so Oracle REF CURSOR and PostgreSQL procedure rows are not returned. | `Adapters.Db.Core/DbResidentAdapterBase.cs` |
| Rows beyond `MaxRows` cannot be fetched by a delivery, and the open cursor holds a connection until it times out. | `Adapters.Db.Core/DbResidentAdapterBase.cs` |
| The database adapters do not declare the mapper role. | `SW.Bitween.Adapters.Db.*` |
| Every statement edit restarts the database's adapter. | `BusProviderSupervisor.cs` |

## External brokers

| Issue | Where |
|---|---|
| RabbitMQ messages without a `message-id` are never deduplicated. | `SW.Bitween.Adapters.Bus.RabbitMq/RabbitBusHandler.cs` |
| Per-endpoint settings on a bus gateway are stored but no adapter reads them. | `Domain/Gateway/BusGateway.cs` |
| RabbitMQ publishing uses no publisher confirms. | `RabbitBusHandler.cs` |
| A retried publish carries a different message id, so the customer's broker cannot deduplicate it. | `Domain/Xchange/Xchange.cs` |
| Messages on an endpoint no gateway claims are acknowledged and discarded. | `Services/DataSources/BusProviderEventSink.cs` |

## Scheduling

| Issue | Where |
|---|---|
| Deleting a subscription does not remove its Quartz triggers. | `Resources/Subscriptions/Delete.cs` |
| A killed run leaves the running flag set, and no API clears it. | `Services/RunFlagUpdater.cs` |
| Aggregation runs have no running flag. | `Services/AggregationJob.cs` |
| Quartz clustering is not guaranteed with the pinned scheduler packages, according to the startup code. | `SW.Bitween.Web/Startup.cs` |
| Consecutive failures never pause or deactivate a subscription. | `Domain/Subscription/Subscription.cs` |

## Deployment and tooling

| Issue | Where |
|---|---|
| `migratedb.sh` refers to projects from an older version and does not work. | `migratedb.sh` |
| The Helm deployment declares container port 80 while the app listens on 8080, and probes use the declared port. | `charts/default/templates/deployment.yaml` |
| The Dockerfile copies the .NET 6 shared runtime into the image without a stated reason. | `Dockerfile` |
| A startup migration failure is logged by parsing the connection string as PostgreSQL, which may throw for other providers and hide the original error. | `SW.Bitween.Web/Program.cs` |
| The Playwright configuration expects a launch profile on port 7155 that is not in the repository. | `ClientApp/playwright.config.ts` |
| No pipeline in this repository publishes the resident adapter packages. | `.github/workflows` |
