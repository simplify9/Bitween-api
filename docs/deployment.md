# Deployment

## Container image

The `Dockerfile` builds the admin UI in a Node 22 stage, publishes `SW.Bitween.Web` with the .NET 10 SDK, and runs it on the ASP.NET 10 runtime image. The final image also carries:

- the .NET 6 shared runtime, copied from the ASP.NET 6 image
- `python3`, Ubuntu 24.04's Python 3.12, with pip, installed with apt, for Python adapters and the adapter editor
- `node` and npm from `node:22-bookworm-slim`, for JavaScript and TypeScript adapters and the adapter editor

Published adapters bring their own dependencies inside their packages. pip and npm are used only by the
adapter editor, when `Bitween:AdapterEditorDependencies` lets it fetch a draft's packages. The container runs as the image's non-root user, which owns only `/app/adapters`, the cache of downloaded adapter packages.

```bash
docker build -t bitween:local .

docker run -p 8080:8080 \
  -e Bitween__DatabaseType=PgSql \
  -e ConnectionStrings__BitweenDb="Host=db;Database=bitween;Username=bitween;Password=..." \
  -e ConnectionStrings__RabbitMQ="amqp://user:password@rabbitmq:5672/" \
  -e Token__Key="..." -e Token__Issuer=bitween -e Token__Audience=bitween \
  -e Bitween__InitialAdminPassword="..." \
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
  --set secrets.Bitween__InitialAdminPassword="..." \
  --set ingress.hosts[0]=bitween.example.com
```

Things to know about the chart:

- The image tag is the chart version, so a chart must be packaged with the version of an image that exists.
- The Service forwards port 80 to container port 8080.
- The Ingress exposes `/api`, `/swagger` and `/` by default, so the admin UI and `/blank.html` are served from the same host as the API. A deployment that overrides `ingress.paths` replaces the whole list, so it has to include `/` itself.
- Probes are on by default: a startup probe and a liveness probe on `/health/live`, which only checks that the process answers, and a readiness probe on `/health/ready`, which also checks the database and that the bus is connected to RabbitMQ. A pod whose broker is down starts and stays not ready until the bus connects; it does not restart. The startup probe allows 40 minutes for migrations (`probes.startupFailureThreshold`).
- A stopping pod lets the exchanges it is running finish for up to 30 seconds before it closes its queues, so they are acknowledged instead of run again elsewhere. The app waits up to 45 seconds to stop, and the chart gives the pod 60 (`terminationGracePeriodSeconds`); keep that value above 45.
- The `rabbitmq` values are not used by any template.
- `charts/default/README.md` explains the two routing modes.

See [Configuration](configuration.md#helm-values) for how each value becomes an environment variable.

The chart runs Bitween with a read-only root file system (`securityContext.readOnlyRootFilesystem`, on by
default), so nothing in the image can be changed from inside the container. Bitween writes only to `/tmp` and
to its cache of downloaded adapter packages, `/app/adapters`, which the chart mounts as `emptyDir` scratch
space: created empty with the pod and gone with it, as the container's own layer was, and not persistent
volumes. `scratch.tmpSizeLimit` and `scratch.adapterCacheSizeLimit` cap them, as `2Gi`.

## Databases

Set `Bitween:DatabaseType` and `ConnectionStrings:BitweenDb`.

| Provider | Value | Notes |
|---|---|---|
| PostgreSQL | `PgSql` | Used by the Helm chart and the integration tests. Tables live in the `infolink` schema with snake_case columns. The connection string must contain `Host=` or `Server=`. |
| SQL Server | `MsSql` | |
| MySQL | `MySql` | The default when unset. Targets MySQL 8. The connection is encrypted: `SslMode=Required` unless the connection string sets `SslMode` itself, so for a server without TLS set `SslMode=Preferred` or `None` there. |

Migrations run automatically when the service starts. A failed migration stops startup and is logged with the targeted host and database. The scheduler's tables are created in the same database.

### Azure managed identity

Set `Bitween__UseAzureManagedIdentity=true` and leave the password out of the connection string.

- **Azure SQL.** Bitween appends `Authentication=Active Directory Default` to the connection string.
- **Azure Database for PostgreSQL.** Bitween requests an Entra ID token and refreshes it every 50 minutes, for both the application and the scheduler.

For a user-assigned identity, set `Bitween__AzureManagedIdentityClientId` or `AZURE_CLIENT_ID`. MySQL has no managed identity support.

## Object storage

Set `Bitween:StorageProvider` and the matching `CloudFiles` keys. See [Configuration](configuration.md#storage).

- With the default `temp30/` document prefix, the storage libraries expire exchange files after 30 days. Choose `temp1/`, `temp7/` or `temp365/` for other retention, or a prefix outside those to keep files. The prefix can be changed later on the Settings page.
- For Azure Blob, set `CloudFiles__SubscriptionId` and `CloudFiles__ResourceGroupName`, and give the identity a role that can manage the account's lifecycle policy. The rules are then created at startup. Without them the rules aren't created, and the Settings page can't show any set up by hand. See [Configuration](configuration.md#storage).
- Keep the bucket private: Bitween reads and writes with its own credentials and serves file links itself. On Azure it switches its container to private at startup; with a managed identity, give it a role that can change the container's access level, or set it to Private yourself.
- The chart sets Bitween's public address from its first host name (`ingress.hosts`, or `gateway.hostnames`), `https` unless the Ingress has no TLS for it. Exchange file links are built on it, so a partner handed a roll-up's links can open them. Without a host, roll-ups link through the instance's own address, which only adapters running in Bitween can open.
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

Run several replicas behind a load balancer for throughput and availability, and tune prefetch, parallel exchanges and priority per work group. Read the [replica notes](architecture.md#running-more-than-one-replica) about the scheduler first.

## When Bitween won't start

Bitween checks its configuration and its database before it serves anything. When something is wrong it logs one entry, `Bitween can't start until these are fixed:`, with a numbered list of what to change, and exits. Fix everything on the list and redeploy.

- Without `ConnectionStrings:BitweenDb` or `ConnectionStrings:RabbitMQ`, the list is written to the console and holds only the configuration problems; the database is checked on the next start.
- A failed migration is logged on its own, as `Database migration failed on startup against {Host}:{Port}/{Database}`.

## Upgrading

- Migrations apply at startup, so back up the database before deploying a new version.
- Upgrading to this version needs a short downtime: stop every pod of the old version before deploying the new one.
- Values already in the `Settings` table are not replaced by new configuration. Change them on the Settings page.
- When upgrading from a version without work groups, set `Bitween__ConsumeLegacyEventMessages=true` until the old event queues are empty.

### Upgrading to parallel exchanges

Before this version every lane ran one exchange at a time per node, whatever its prefetch. Now a lane runs up to its **Parallel exchanges** at once, and a lane that leaves it empty takes its prefetch: 12 by default. That is what the documentation always described, but not what deployments saw, so on upgrade:

- **Partners receive concurrent calls.** A partner that got one call at a time from each node can get up to the lane's parallel exchanges at once. One that limits its callers answers 429 and the circuit breaker opens, which is handled, but it is new for that partner.
- **Order within a lane is not kept above 1.** Two exchanges for the same order sent close together, a create and an update, can now run in either order on one node. Order was never kept across nodes.
- **More of everything at once.** Each running exchange can hold a database connection and its payload in memory; five lanes at 12 is 60 at once per node, against Npgsql's default pool of 100. Lanes that use classic (non-resident) adapters start that many processes.

To keep a lane as it was, set it to 1 **before** upgrading: on the Work groups page, set the group's **Parallel exchanges** to 1 and save (the field is stored with the group and ignored by older versions), or for the Ungrouped lane set `Bitween__BusDefaultConcurrency=1` (`busDefaultConcurrency: '1'` in the chart). The results lane of every group runs one at a time either way.

Also in this version: a publish waits for the broker to confirm it (`Bitween__BusPublisherConfirms=false` publishes as before), a pod whose broker is down at boot starts and becomes ready when the bus connects, and a stopping pod drains its running exchanges.

## CI/CD

| Pipeline | Trigger | What it does |
|---|---|---|
| `.github/workflows/bitween-api-cicd-gateway.yml` | Push to `releases/r10.0-develop`, or manual on it | Runs unit and integration tests, pushes the `simplify9/bitween-develop` image to Docker Hub, publishes the production chart (`charts/default`) as `bitween-develop` to `charts.sf9.io` and GHCR, tags the repository, and deploys to the `bitween-dev` namespace on the dev cluster (`app-dev.bitween.io`, on the gateway's `*.bitween.io` wildcard listener). Publishes no NuGet packages. |
| `.github/workflows/bitween-api-cicd-gateway.yml` | Push to `releases/r10.0`, or manual on it | Runs unit and integration tests, pushes the `simplify9/bitween` image to Docker Hub, publishes the `bitween` chart clients install to `charts.sf9.io` and GHCR (`oci://ghcr.io/simplify9/charts/bitween`), pushes `SimplyWorks.Bitween.Sdk` and `SimplyWorks.Bitween.Adapters` to NuGet, tags the repository, and deploys to the `bitween` namespace on the production cluster (`app.bitween.io`, on the gateway's `*.bitween.io` wildcard listener). The deploy runs only while the `BITWEEN_PROD_DEPLOY` repository variable is `true`. |
| `.github/workflows/cli-release.yml` | Called by the CI/CD workflow, never run by hand or by a tag: after the release from `releases/r10.0`, and after each develop build | Publishes the [bitween CLI](cli.md) at the version that run computed, as self-contained single-file binaries for `linux-x64`, `linux-arm64`, `linux-musl-x64`, `linux-musl-arm64`, `osx-x64`, `osx-arm64` and `win-x64`, with a `SHA256SUMS` file. From the release it is `cli-v<version>`, the latest release; from develop, the pre-release `cli-v<version>-dev.<run>`. `scripts/install-cli.sh` installs the latest release, a pre-release only when named. |
| `.github/workflows/sdk-release.yml` | Called by the CI/CD workflow after the release from `releases/r10.0` | Publishes the adapter contract packages at Bitween's version: `simplyworks-bitween` to PyPI and `@simplyworks/bitween` to npm, each tested first against SW-Serverless's SDK. Uses the `PYPI_API_TOKEN` and `NPM_TOKEN` secrets; a version already published is left alone. |
| `.github/workflows/dotnet-pr-checks.yml` | Pull requests to `releases/**` | Builds the solution and runs the .NET unit tests |
| `.github/workflows/frontend-tests.yml` | Pull requests to `releases/**` that touch the UI | Runs the UI unit and component tests |
| `.github/workflows/critical-vuln-check.yml` | Pull requests to release and develop branches | Fails while critical Dependabot alerts are open |
| `.github/workflows/dependabot-auto-merge.yml` | Dependabot pull requests | Auto-merges patch updates |
| `azure-pipelines.yml` | `releases/*` | The earlier Azure DevOps pipeline, still in the repository |

Releases are versioned `10.0.x`.
