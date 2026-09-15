# Configuration

Bitween reads standard .NET configuration: `appsettings.json`, `appsettings.{Environment}.json`, environment variables and command-line arguments. In environment variable names, replace `:` with a double underscore, as in `Bitween__DatabaseType`.

Some values are also **runtime settings** that administrators change in the UI. Once stored, a setting ignores configuration. See [Runtime settings](#runtime-settings).

## Required

| Key | Example |
|---|---|
| `ConnectionStrings:BitweenDb` | `Host=db;Port=5432;Database=bitween;Username=bitween;Password=...` |
| `ConnectionStrings:RabbitMQ` | `amqp://user:password@rabbitmq:5672/` |
| `Token:Key` | A random secret of at least 32 characters, unique to this deployment |
| `Token:Issuer`, `Token:Audience` | Values used to sign and validate JWTs |
| Storage settings | See [Storage](#storage) |

The service stops at startup with a clear error when the database connection string is missing.

### `Token:Key` must be your own

Whoever holds this key can mint a token for any identity, including one with no account behind
it. The service **refuses to start** when it is missing, or when it is still the sample value
`6547647654764764767657658658758765876532542` — which ships as the chart default and is published
in this repository, so a deployment that never overrode it was open to anyone who read it.

The check runs in every environment, including Development, so **a local checkout needs its own
key too**: put one in your gitignored `appsettings.Development.json` (or `appsettings.Local.json`).
Any long random string will do — `openssl rand -base64 48`.

Changing the key invalidates every token already issued, so everyone signs in again. That is the
intended response to a key you no longer trust.

## `Bitween` section

| Key | Default | Description |
|---|---|---|
| `DatabaseType` | `MySql` | `PgSql`, `MsSql` or `MySql`. Any other value means MySQL. |
| `AdminDatabaseName` | `defaultdb` | PostgreSQL maintenance database, used when the target database has to be created. |
| `UseAzureManagedIdentity` | `false` | Authenticate to Azure SQL or Azure Database for PostgreSQL with a managed identity. |
| `AzureManagedIdentityClientId` | | Client id of a user-assigned identity. Falls back to `AZURE_CLIENT_ID` or `MSI_CLIENT_ID`. |
| `StorageProvider` | `S3` | `S3`, `AS` for Azure Blob, `OC` for Oracle Cloud, or `Local`. `Local` is refused outside Development. |
| `DocumentPrefix` | `temp30/Bitweendocs` | Storage key prefix for exchange files. Changing it leaves existing files unreachable. |
| `AdapterPath` | `adapters` | Storage key prefix for custom adapter packages. |
| `ServerlessCommandTimeout` | `300` | Seconds a custom adapter may run. |
| `BusProvidersEnabled` | `false` | Run data source adapters, for brokers and databases, on this node. See [Data sources](data-sources.md). |
| `BusProviderMaxInFlight` | `16` | Unacknowledged messages one broker adapter may have in flight with Bitween. |
| `InboundMessagePruneCron` | `0 30 3 * * ?` | Quartz cron for deleting expired broker deduplication keys. |
| `QueuePrefix` | `bitween` | Part of every queue name. Give each deployment sharing a broker its own value. |
| `BusDefaultQueuePrefetch` | `12` | Default number of unacknowledged messages per consumer. Work groups can override it. |
| `RabbitMqManagementUrl`, `RabbitMqManagementUsername`, `RabbitMqManagementPassword` | | RabbitMQ management API, needed for queue health. |
| `ConsumeLegacyEventMessages` | `false` | Also drain the queues that versions before work groups published to. |
| `CorsOrigins` | empty | Browser origins allowed to call the API with credentials. Set as `Bitween__CorsOrigins__0`, `Bitween__CorsOrigins__1` and so on. |
| `AdminCredentials` | `admin:1234512345` | Break-glass `user:password`. Always override. |
| `SettingsEncryptionKey` | | Passphrase that encrypts secret settings in the database. |
| `ReceiveAttemptRetentionDays` | `30` | Days to keep receive attempts. |
| `ReceiveAttemptCleanupCron` | `0 0 3 * * ?` | Quartz cron for the receive attempt cleanup. |
| `AreXChangeFilesPrivate` | `false` | First value of the runtime setting. |
| `ApiCallSubscriptionResponseAcceptedStatusCode` | `202` | First value of the runtime setting. |
| `JwtExpiryMinutes` | `60` | First value of the runtime setting. |
| `MsalClientId`, `MsalTenantId`, `MsalRedirectUri` | | First values of the Microsoft sign-in settings. |
| `DisableEmailPasswordLogin` | `false` | First value of the runtime setting. |
| `RebexLicenseKey` | | First value of the runtime setting. |
| `RetryJobCron` | `0 * * * * ?` | First value of the runtime setting. |

## `Theme` section

`LoginLogo`, `BitweenLogo`, `BitweenText`, `LinkedinLink`, `GithubLink`, `BitweenHeaderIcon`, `WebsiteLink`, `CompanyName`, `AllRightsReserved`, `CopyRightsIcon`, `TabTitle`, `TabIcon`, `PrimaryColor` and `ShowFooter`. All of them are runtime settings, so configuration only supplies their first values. `BitweenIcon` still binds but no longer has any effect.

## Storage

Storage credentials live in the `CloudFiles` section and are read by the `SimplyWorks.CloudFiles` packages.

| Provider | Keys |
|---|---|
| `S3` | `AccessKeyId`, `SecretAccessKey`, `ServiceUrl`, `BucketName` |
| `AS` | `AccountName`, `AccessKeyId`, `SecretAccessKey`, `ServiceUrl`, `BucketName`, `Managed`, `ManagedIdentityClientId`, `PublicServiceUrl` |
| `OC` | `TenantId`, `FingerPrint`, `UserId`, `RSAKey`, `Region`, `NamespaceName`, `BucketName` |
| `Local` | `BucketName`, and optionally `StoragePath` |

## Logging

Logging lives in the `SwLogger` section.

| Key | Description |
|---|---|
| `LoggingLevel` | Minimum level. The shipped `appsettings.json` sets 2. |
| `ApplicationName` | Name recorded on log entries. |
| `ElasticsearchUrl`, `ElasticsearchUser`, `ElasticsearchPassword` | The Elasticsearch sink. |
| `ElasticsearchEnvironments` | Environments in which the Elasticsearch sink is active. |
| `ElasticsearchDeleteIndexAfterDays` | Retention in Elasticsearch. |

## Runtime settings

Settings are stored in the `Settings` table and edited on the Settings page.

- On first boot, every editable setting is copied from configuration into the table. After that the table wins, and changing configuration has no effect.
- A saved change applies immediately on the instance that saved it, and reaches other instances through the cache revoke broadcast.
- **Reset to default** writes the product default, not the configured value.
- Values captured at startup, such as the bus, storage, CORS and database settings, are shown read-only. Credentials only show whether they are set.

| Setting | Section | Editable | Default |
|---|---|---|---|
| `Bitween.AreXChangeFilesPrivate` | Documents & storage | Yes | `false` |
| `Bitween.DocumentPrefix` | Documents & storage | Read-only | `temp30/Bitweendocs` |
| `Bitween.ApiCallSubscriptionResponseAcceptedStatusCode` | API behavior | Yes | `202` |
| `Bitween.JwtExpiryMinutes` | API behavior | Yes | `60` |
| `Bitween.CorsOrigins` | API behavior | Read-only | empty |
| `Bitween.MsalClientId`, `MsalTenantId`, `MsalRedirectUri` | Single sign-on (Microsoft) | Yes | empty |
| `Bitween.DisableEmailPasswordLogin` | Single sign-on (Microsoft) | Yes | `false` |
| `Bitween.RebexLicenseKey` | Adapters | Yes, secret | empty |
| `Bitween.AdapterPath` | Adapters | Read-only | `adapters` |
| `Bitween.ServerlessCommandTimeout` | Adapters | Read-only | `300` |
| `Bitween.RetryJobCron` | Reliability & jobs | Yes | `0 * * * * ?` |
| `Bitween.QueuePrefix`, `BusDefaultQueuePrefetch`, `ConsumeLegacyEventMessages` | Messaging | Read-only | |
| `Bitween.RabbitMqManagementUrl`, `Username`, `Password` | Messaging | Shows whether set | |
| `Bitween.UseAzureManagedIdentity` | Database | Read-only | `false` |
| `Bitween.AzureManagedIdentityClientId` | Database | Shows whether set | |
| `Bitween.SettingsEncryptionKey` | Security | Shows whether set | |
| `Theme.PrimaryColor` | Brand & theme | Yes | `#e3311d` |
| `Theme.CompanyName`, `TabTitle`, `TabIcon`, `LoginLogo`, `BitweenLogo`, `BitweenHeaderIcon`, `BitweenText`, `ShowFooter`, `LinkedinLink`, `GithubLink`, `WebsiteLink`, `AllRightsReserved`, `CopyRightsIcon` | Brand & theme | Yes | Simplify9 branding |

The retry poll cron is validated before it is saved, and saving it reschedules the retry job.

`GET /api/settings/config` needs no sign-in. It returns the Microsoft sign-in values, whether email and password sign-in is disabled, whether RabbitMQ management is configured, and the theme. The sign-in page loads it.

## Helm values

The chart in `charts/default` maps values to environment variables. Values marked *secret* are delivered through a Kubernetes Secret.

| Value | Environment variable |
|---|---|
| `global.environment` | `ASPNETCORE_ENVIRONMENT` and `SwLogger__ElasticsearchEnvironments` |
| `db` *(secret)* | `ConnectionStrings__BitweenDb`, named by `dbConnectionStringName` |
| `dbType` | `Bitween__DatabaseType` |
| `adminDb` | `Bitween__AdminDatabaseName` |
| `useAzureManagedIdentity` | `Bitween__UseAzureManagedIdentity` |
| `documentPrefix` | `Bitween__DocumentPrefix` |
| `storageProvider` | `Bitween__StorageProvider` |
| `areXChangeFilesPrivate` | `Bitween__AreXChangeFilesPrivate` |
| `busDefaultQueuePrefetch` | `Bitween__BusDefaultQueuePrefetch` |
| `serverlessCommandTimeout` | `Bitween__ServerlessCommandTimeout` |
| `msalClientId`, `msalRedirectUri`, `msalTenantId` | `Bitween__MsalClientId`, `Bitween__MsalRedirectUri`, `Bitween__MsalTenantId` |
| `environmentVariables.*` | Passed through under their own names, such as the RabbitMQ management values |
| `global.bus.rabbitUrl` *(secret)* | `ConnectionStrings__RabbitMQ` |
| `global.token.key`, `issuer`, `audience` *(secret)* | `Token__Key`, `Token__Issuer`, `Token__Audience` |
| `global.cloudFiles.*` *(secret)* | `CloudFiles__AccessKeyId`, `SecretAccessKey`, `ServiceUrl`, `BucketName`, `TenantId`, `Fingerprint`, `UserId`, `RSAKey`, `Region`, `NamespaceName` |
| `global.logger.esUrl`, `esUser`, `esPassword` *(secret)* | `SwLogger__ElasticsearchUrl`, `SwLogger__ElasticsearchUser`, `SwLogger__ElasticsearchPassword` |
| `secrets.*` *(secret)* | Passed through under their own names, such as `Bitween__SettingsEncryptionKey` |

The chart also sets `SwLogger__ApplicationName` to the release name and `BitweenClient__BaseUrl` to the in-cluster service address.

The chart's default `global.token.key` is a fixed value committed to the repository. Always override it.
