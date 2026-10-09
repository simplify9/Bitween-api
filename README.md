# Bitween

Bitween is a self-hosted integration platform. It takes business documents in from partners, HTTP calls, message brokers, databases and scheduled pulls. It validates, transforms and delivers them, and keeps a searchable record of every exchange.

One .NET 10 service hosts everything: the REST API, the partner-facing gateway endpoints, background processing, the scheduler and the React admin UI.

> These docs are written from the source code as of 11 September 2026. They replace the earlier README and `docs/` pages, which described an older version of Bitween.

## What Bitween does

- **Many ways in.** Partners call an API gateway. Other systems publish to Bitween's bus, or to their own RabbitMQ or Amazon SQS broker. Scheduled jobs pull from HTTP APIs, S3, Azure Blob, SFTP/FTP, POP3 mailboxes and databases. Aggregations roll finished exchanges up on a schedule.
- **One pipeline.** Every message becomes an *exchange* that runs through the same stages: filter, map, deliver, route the response.
- **Adapters for each stage.** Built-in adapters cover HTTP, S3, Azure Blob, SFTP/FTP, POP3 and SMTP, plus a visual rules-based mapper for JSON and XML. Anything else can be a custom adapter, written in .NET, Python, JavaScript or TypeScript, that runs out of process. The `bitween` CLI writes, checks and publishes them, and Python, JavaScript and TypeScript adapters can also be written in the admin UI.
- **Long-lived connections.** Data sources hold connections to RabbitMQ and Amazon SQS brokers and to PostgreSQL, MySQL, SQL Server and Oracle databases, so deliveries can publish messages or run SQL.
- **Reliability built in.** Retry policies match failures and retry them with a delay and a shared budget. Every exchange's retries form a chain you can follow. Exhausted budgets raise alerts, and notifiers report results.
- **Operable.** Queue health straight from RabbitMQ, data source health, schedule health, run history, receive attempts, a dashboard and an audit trail of every configuration change.
- **Administered in the browser.** Roles with fine-grained permissions, Microsoft sign-in, and settings and branding that change at runtime.

## How it fits together

```mermaid
flowchart LR
  subgraph In[Entry points]
    GW[API gateway]
    BUS[Bus gateway]
    JOB[Scheduled job]
    AGG[Aggregation]
  end
  subgraph Core[Bitween service]
    X[(Exchange)]
    P[Filter, mapper, handler]
    R[Result and retry policy]
  end
  EXT[(Customer broker or database)]
  EXT --> BUS
  EXT --> JOB
  GW --> X
  BUS --> X
  JOB --> X
  AGG --> X
  X -->|work group queue| P --> R
  P -->|delivery| OUT[Partner systems]
  P -->|publish or SQL| EXT
  R -->|notifiers and alerts| N[Handler adapters]
  X -. files .-> S[(Object storage)]
  R -. rows .-> DB[(Bitween database)]
```

## Requirements

| Component | Supported |
|---|---|
| Build | .NET 10 SDK, Node 22 with Yarn for the admin UI |
| Database | PostgreSQL, SQL Server or MySQL 8 |
| Message broker | RabbitMQ, with the management plugin for queue health |
| Object storage | S3-compatible, Azure Blob Storage, Oracle Cloud Object Storage, or local disk in Development |
| Custom adapters in Python or JavaScript | `python3` 3.12 or later and `node` 22 or later on a Linux or macOS host. The Docker image includes both. |

## Quick start

This runs Bitween locally against PostgreSQL, RabbitMQ and local-disk storage.

1. Start the dependencies.

   ```bash
   docker run -d --name bitween-pg -p 5432:5432 -e POSTGRES_PASSWORD=postgres postgres:16
   docker run -d --name bitween-mq -p 5672:5672 -p 15672:15672 rabbitmq:3-management
   ```

2. Build the admin UI into the web host's `wwwroot`.

   ```bash
   cd SW.Bitween.Web/ClientApp
   yarn install
   yarn build
   cd ../..
   ```

3. Configure and run the service.

   ```bash
   export ASPNETCORE_ENVIRONMENT=Development
   export Bitween__DatabaseType=PgSql
   export ConnectionStrings__BitweenDb="Host=localhost;Port=5432;Database=bitween;Username=postgres;Password=postgres"
   export ConnectionStrings__RabbitMQ="amqp://guest:guest@localhost:5672/"
   export Bitween__StorageProvider=Local
   export CloudFiles__BucketName=bitween
   export Token__Key="replace-with-a-random-string-of-at-least-32-chars"
   export Token__Issuer=bitween-local
   export Token__Audience=bitween-local
   export Bitween__InitialAdminPassword="replace-with-your-own-password"
   export Bitween__RabbitMqManagementUrl=http://localhost:15672
   export Bitween__RabbitMqManagementUsername=guest
   export Bitween__RabbitMqManagementPassword=guest

   dotnet run --project SW.Bitween.Web
   ```

4. Open https://localhost:5000 and sign in as `admin@Bitween.systems` with the password you set in `Bitween__InitialAdminPassword`.

The database schema is created on first start. A new database refuses to start without `Bitween__InitialAdminPassword` (see [Configuration](docs/configuration.md#bitweeninitialadminpassword-on-a-new-installation)). Work through the [production checklist](docs/security.md#production-checklist) before exposing an instance. To use brokers or databases as data sources, see [Data sources](docs/data-sources.md#turning-them-on).

## Documentation

| Page | What it covers |
|---|---|
| [Concepts](docs/concepts.md) | Information types, partners, subscriptions, exchanges, data sources and the rest of the vocabulary |
| [Architecture](docs/architecture.md) | Components, projects, adapter runtimes, data, messaging, storage and caching |
| [Exchange pipeline](docs/exchange-pipeline.md) | What happens to a message from arrival to result |
| [Entry points](docs/entry-points.md) | API gateways, bus gateways, scheduled jobs, aggregations and legacy entry points |
| [Adapters](docs/adapters.md) | Every built-in adapter and its properties, custom adapters in .NET, Python and JavaScript, publishing, the adapter editor and the source viewer |
| [Adapter contract](docs/adapter-contract.md) | What Bitween calls on an adapter of each kind and what it passes, in every language |
| [The bitween CLI](docs/cli.md) | Signing in from a terminal, and writing, building, checking and publishing adapters |
| [Mapping](docs/mapping.md) | The rules-based mapper and the legacy Scriban JSON mapper |
| [Data sources](docs/data-sources.md) | Long-lived connections: enabling them, placement, health and resource limits |
| [External brokers](docs/external-brokers.md) | Reading from and publishing to a customer's RabbitMQ or Amazon SQS |
| [Databases](docs/databases.md) | PostgreSQL, MySQL, SQL Server and Oracle: statements, polling and writing |
| [Retries and alerts](docs/retries-and-alerts.md) | Manual and bulk retry, retry chains, retry policies, budgets, alerts and notifiers |
| [Scheduling](docs/scheduling.md) | Schedules, jobs, run history and schedule health |
| [Security](docs/security.md) | Sign-in, tokens, roles and permissions, partner keys, hardening, audit |
| [Configuration](docs/configuration.md) | Every configuration key, runtime setting and Helm value |
| [Deployment](docs/deployment.md) | Docker image, Helm chart, databases, storage and CI/CD |
| [Operations](docs/operations.md) | Monitoring, troubleshooting, logs and data retention |
| [Admin UI](docs/admin-ui.md) | A tour of the web interface |
| [API reference](docs/api-reference.md) | REST conventions and every endpoint |
| [Development](docs/development.md) | Local setup, tests, migrations and extending Bitween |
| [Known limitations](docs/caveats.md) | Behaviours and gaps in the current code that operators should know about |

Deeper reference written alongside the features:

- [Database adapters: API reference](docs/database-adapters-api.md) and the [per-engine guide](docs/database-adapters-per-engine.md)
- [External bus providers](docs/external-bus-providers.md), the implementation notes for brokers
- [Design documents](docs/design/README.md): proposals and plans, not a description of current behaviour

## Repository layout

| Path | Purpose |
|---|---|
| `SW.Bitween.Web` | ASP.NET Core host: startup, authentication, security headers, and the admin UI in `ClientApp` |
| `SW.Bitween.Api` | Domain model, database context, API handlers, exchange pipeline, jobs, settings and the data source supervisor |
| `SW.Bitween.NativeAdapters` | Built-in adapters and the rules-based mapper |
| `SW.Bitween.Adapters.Bus.RabbitMq`, `SW.Bitween.Adapters.Bus.Sqs` | Resident broker adapters |
| `SW.Bitween.Adapters.Db.*` | Resident database adapters and their shared core |
| `SW.Bitween.Sdk` | Shared models and the retry policy evaluator, published to NuGet as `SimplyWorks.Bitween.Sdk` |
| `SW.Bitween.Adapters` | The adapter contract and its .NET interfaces, published to NuGet as `SimplyWorks.Bitween.Adapters` |
| `SW.Bitween.Adapters.Tooling` | Bitween's adapters on SW-Serverless's tooling: the contract, templates and Python and Node packages the CLI and the adapter editor use |
| `SW.Bitween.Cli` | The `bitween` command |
| `sdk/python`, `sdk/node` | The adapter contract for Python (`simplyworks-bitween`) and for JavaScript and TypeScript (`@simplyworks/bitween`) |
| `SW.Bitween.PgSql`, `SW.Bitween.MySql`, `SW.Bitween.MsSql` | Provider-specific database contexts and migrations |
| `SW.Bitween.Sample*` | Sample custom adapters, including a resident handler |
| `SW.Bitween.UnitTests`, `SW.Bitween.IntegrationTests`, `SW.Bitween.HttpTests`, `SW.Bitween.TransportTests` | Test suites |
| `tools` | Development databases for trying the database adapters, and the end-to-end test script |
| `scripts/install-cli.sh` | Installs the `bitween` CLI from a release |
| `charts/default` | Helm chart |
| `Dockerfile` | Container image build |

## License

Bitween is licensed under the [GNU Affero General Public License v3.0](LICENSE), with the additional terms in [NOTICE](NOTICE):

- You can use, run and modify Bitween for free, including in production inside your organization.
- If you distribute Bitween or offer it as a service to others, you must release your source code under the AGPL and keep the "Based on Bitween by Simplify9" attribution.
- Forks and modified versions must use a different name. "Bitween" is a Simplify9 trademark.
- `SW.Bitween.Sdk` (the `SimplyWorks.Bitween.Sdk` NuGet package) and the `SW.Bitween.Sample*` projects stay under the MIT License, so your custom adapters can use any license.
- Versions released before the license change remain available under the MIT License.

A commercial license is available from Simplify9 ([info@simplify9.com](mailto:info@simplify9.com)) for embedding Bitween in closed-source products or hosting it for third parties without releasing source code.
