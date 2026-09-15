# Deployment

## Container image

The `Dockerfile` builds the admin UI in a Node 22 stage, publishes `SW.Bitween.Web` with the .NET 10 SDK, and runs it on the ASP.NET 10 runtime image. The final image also carries the .NET 6 shared runtime, copied from the ASP.NET 6 image.

```bash
docker build -t bitween:local .

docker run -p 8080:8080 \
  -e Bitween__DatabaseType=PgSql \
  -e ConnectionStrings__BitweenDb="Host=db;Database=bitween;Username=bitween;Password=..." \
  -e ConnectionStrings__RabbitMQ="amqp://user:password@rabbitmq:5672/" \
  -e Token__Key="..." -e Token__Issuer=bitween -e Token__Audience=bitween \
  -e Bitween__StorageProvider=S3 \
  -e CloudFiles__AccessKeyId=... -e CloudFiles__SecretAccessKey=... \
  -e CloudFiles__ServiceUrl=https://s3.example.com -e CloudFiles__BucketName=bitween \
  bitween:local
```

The container listens on port 8080. Released images are published to `docker.io/simplify9/bitween`.

## Kubernetes with Helm

The chart in `charts/default` creates a Deployment, a Service, a Secret, and either an ingress-nginx Ingress or a Gateway API HTTPRoute.

```bash
helm install bitween ./charts/default \
  --set db="Host=db;Database=bitween;Username=bitween;Password=..." \
  --set dbType=pgsql \
  --set global.bus.rabbitUrl="amqp://user:password@rabbitmq:5672/" \
  --set global.token.key="..." \
  --set global.token.issuer=bitween \
  --set global.token.audience=bitween \
  --set storageProvider=S3 \
  --set global.cloudFiles.accessKeyId=... \
  --set global.cloudFiles.secretAccessKey=... \
  --set global.cloudFiles.serviceUrl=https://s3.example.com \
  --set global.cloudFiles.bucketName=bitween \
  --set secrets.Bitween__SettingsEncryptionKey="..." \
  --set ingress.hosts[0]=bitween.example.com
```

Things to know about the chart:

- The image tag is the chart version, so a chart must be packaged with the version of an image that exists.
- The Service forwards port 80 to container port 8080.
- The Ingress exposes only `/api` and `/swagger` by default. Add `/` to `ingress.paths`, or route `/` through the Gateway API, to serve the admin UI and `/blank.html` from the same host.
- Probes are off by default. The template declares container port 80 while the app listens on 8080, so check the probe port before turning probes on.
- The `rabbitmq` values are not used by any template.
- `charts/default/README.md` explains the two routing modes.

See [Configuration](configuration.md#helm-values) for how each value becomes an environment variable.

## Databases

Set `Bitween:DatabaseType` and `ConnectionStrings:BitweenDb`.

| Provider | Value | Notes |
|---|---|---|
| PostgreSQL | `PgSql` | Used by the Helm chart and the integration tests. Tables live in the `infolink` schema with snake_case columns. The connection string must contain `Host=` or `Server=`. |
| SQL Server | `MsSql` | |
| MySQL | `MySql` | The default when unset. Targets MySQL 8. |

Migrations run automatically when the service starts. A failed migration stops startup and is logged with the targeted host and database. The scheduler's tables are created in the same database.

### Azure managed identity

Set `Bitween__UseAzureManagedIdentity=true` and leave the password out of the connection string.

- **Azure SQL.** Bitween appends `Authentication=Active Directory Default` to the connection string.
- **Azure Database for PostgreSQL.** Bitween requests an Entra ID token and refreshes it every 50 minutes, for both the application and the scheduler.

For a user-assigned identity, set `Bitween__AzureManagedIdentityClientId` or `AZURE_CLIENT_ID`. MySQL has no managed identity support.

## Object storage

Set `Bitween:StorageProvider` and the matching `CloudFiles` keys. See [Configuration](configuration.md#storage).

- With the default `temp30/` document prefix, the S3 and Oracle storage libraries expire exchange files after 30 days. Choose `temp1/`, `temp7/` or `temp365/` for other retention, or a prefix outside those to keep files.
- For Azure Blob, configure retention on the storage account.
- `Local` writes files to disk and only starts in Development.

## RabbitMQ

- Bitween declares its queues at startup, and again whenever work groups or bus-enabled information types change.
- Enable the management plugin and set the three `Bitween:RabbitMqManagement*` keys to get queue health.
- Give each deployment that shares a broker its own `Bitween:QueuePrefix`.

## Data sources

Broker and database connections run only on nodes with `Bitween__BusProvidersEnabled=true`, and they need `ConnectionStrings__RabbitMQ` for leases.

- Install the adapter packages, `bitween.bus.*` and `bitween.db.*`, under `{Bitween:AdapterPath}` like custom adapters. The pipelines in this repository do not publish them.
- The chart has no dedicated values, so pass the settings through `environmentVariables`.
- Database pools are held on every enabled node, so size `MaxPoolSize` against the number of replicas.

See [Data sources](data-sources.md).

## Scaling

Run several replicas behind a load balancer for throughput and availability, and tune prefetch and priority per work group. Read the [replica notes](architecture.md#running-more-than-one-replica) about the scheduler first.

## Upgrading

- Migrations apply at startup, so back up the database before deploying a new version.
- Values already in the `Settings` table are not replaced by new configuration. Change them on the Settings page.
- When upgrading from a version without work groups, set `Bitween__ConsumeLegacyEventMessages=true` until the old event queues are empty.

## CI/CD

| Pipeline | Trigger | What it does |
|---|---|---|
| `.github/workflows/bitween-api-cicd-gateway.yml` | Push to `releases/r10.0`, or manual | Runs unit and integration tests, pushes the image to Docker Hub, publishes the chart to GHCR and `charts.sf9.io`, pushes `SimplyWorks.Bitween.Sdk` to NuGet, tags the repository, and deploys the playground environment |
| `.github/workflows/critical-vuln-check.yml` | Pull requests to release and develop branches | Fails while critical Dependabot alerts are open |
| `.github/workflows/dependabot-auto-merge.yml` | Dependabot pull requests | Auto-merges patch updates |
| `azure-pipelines.yml` | `releases/*` | The earlier Azure DevOps pipeline, still in the repository |

Releases are versioned `10.0.x`.
