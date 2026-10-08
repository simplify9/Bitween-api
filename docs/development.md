# Development

## Prerequisites

- .NET 10 SDK
- Node 22 and Yarn 1
- Docker, for local dependencies and the integration tests
- `dotnet-ef` 9, for migrations

## Running locally

Follow the [quick start](../README.md#quick-start). Keep local configuration in environment variables or in `SW.Bitween.Web/appsettings.Development.json`, which git ignores.

The launch profile serves `https://localhost:5000` and `http://localhost:5003`, with Swagger UI at `/swagger`.

To work on the UI, run `yarn build` in `SW.Bitween.Web/ClientApp` and refresh the browser. `dotnet publish` builds the UI itself unless you pass `-p:SkipClientBuild=true`.

## Tests

| Suite | Command | Needs |
|---|---|---|
| Unit tests, MSTest | `dotnet test SW.Bitween.UnitTests` | Nothing. Uses in-memory SQLite and fakes. |
| Integration tests, xUnit | `dotnet test SW.Bitween.IntegrationTests` | A running Docker daemon |
| UI unit tests, Vitest | `yarn test` in `ClientApp` | Nothing |
| UI end-to-end, Playwright | `tools/e2e.sh` | A running Docker daemon |

- The integration tests start PostgreSQL, RabbitMQ and MailHog with Testcontainers. They run migrations, use local-disk storage and the real serverless runner, and replace Quartz with a recording fake. The build publishes the sample adapters and the tests install them into local storage, so custom adapters run for real. Tests run one at a time.
- `MigrationDriftTests` fails when the model changed without a migration for all three providers.
- The Rebex POP3 tests are inconclusive unless `Bitween__RebexLicenseKey` is set.
- The database adapter tests start PostgreSQL 16, MySQL 8.4, SQL Server 2022 and Oracle Free 23 containers, and the broker adapter tests run against containers too. The first run downloads large images.
- The integration tests use their own local storage bucket, `bitween-integration-tests`, so they leave a local instance's adapters alone.
- UI unit tests and Playwright specs are type-checked through `tsconfig.test.json`.
- The Playwright suite runs against a real instance. See [End-to-end tests](#end-to-end-tests).

## End-to-end tests

`tools/e2e.sh` runs the Playwright suite from nothing. It starts PostgreSQL 16 on port 55432 and RabbitMQ on 55672 (management on 55673) in Docker, builds the UI and the backend, starts the app on `https://localhost:7155` with local-disk storage in its own `bitween-e2e` bucket, runs `playwright test`, and removes all of it afterwards. It needs Docker, the .NET SDK, Yarn and a trusted ASP.NET Core development certificate (`dotnet dev-certs https --trust`). Set `DOCKER_HOST` if your daemon is not the default one, as with Colima.

```bash
tools/e2e.sh                              # fresh environment, run everything, tear down
tools/e2e.sh -- e2e/exchanges.spec.ts     # arguments after -- go to playwright test
tools/e2e.sh --keep                       # leave the environment running afterwards
tools/e2e.sh --reuse                      # run again against the kept environment
tools/e2e.sh --up                         # start an environment without running tests
tools/e2e.sh --down                       # remove a kept environment
tools/e2e.sh --no-build                   # skip the UI and backend builds
```

A new installation refuses to start without `Bitween__InitialAdminPassword`, and refuses the old published default. The script passes `E2E_ADMIN_PASSWORD` to the app as that setting, and generates a random one when it is unset. The app log is in `$TMPDIR/bitween-e2e/app.log`.

The suite's `seed` project runs before every spec. It checks that the backend answers, that the administrator can sign in, that RabbitMQ consumers are attached and that the HTTP adapters are installed. It then removes what earlier runs left behind, which is anything named `Playwright …` or `PW …` and `pw-…@example.test` accounts, and finds or creates the rows the specs build on. These are listed in `ClientApp/e2e/seed-data.ts`: the partner `Acme Retail`, the information types `Shipment order` and `Delivery proof`, and a subscription that delivers to an unreachable address so that there are always failed exchanges to retry. Seeding is idempotent, so the suite also runs against a long-lived database.

To run against an instance you started yourself, run `yarn test:e2e` in `ClientApp` and set:

| Variable | Default |
|---|---|
| `E2E_BASE_URL` | `https://localhost:7155/` |
| `E2E_ADMIN_EMAIL` | `admin@Bitween.systems` |
| `E2E_ADMIN_PASSWORD` | The old published default, which databases created before `Bitween__InitialAdminPassword` existed still have |

The instance needs RabbitMQ with the management API configured (`Bitween__RabbitMqManagementUrl`), and sign-in rate limits high enough for a suite that signs in before every test (`Bitween__RateLimits__SignInPerMinute`).

Two retention tests need the storage bucket to have a `temp30/` deletion rule. Local-disk storage never reports one, so under `tools/e2e.sh` they are skipped and say why.

## Trying data sources locally

`tools/dev-database.md` explains how to publish the broker and database adapter packages to local storage and how to seed a database for each engine with the `tools/dev-warehouse*.sql` scripts. Set `Bitween__BusProvidersEnabled=true` on the local instance.

## Adding a database migration

Each provider project has a design-time factory, so no startup project or running database is needed. Add the migration to all three providers with the same name.

```bash
dotnet tool install -g dotnet-ef --version 9.0.19
(cd SW.Bitween.PgSql && dotnet ef migrations add <Name>)
(cd SW.Bitween.MySql && dotnet ef migrations add <Name>)
(cd SW.Bitween.MsSql && dotnet ef migrations add <Name>)
dotnet test SW.Bitween.UnitTests --filter MigrationDriftTests
```

The PostgreSQL context declares its own model rather than inheriting the shared one. Make mapping changes in both `SW.Bitween.Api/Data/BitweenDbContext.cs` and `SW.Bitween.PgSql/BitweenDbContext.cs`.

`migratedb.sh` refers to project names from an older version and does not work.

## Adding an API endpoint

Add a class under `SW.Bitween.Api/Resources/{Area}` that implements a CqApi handler interface. The folder name, lower-cased, becomes the path. These rules are inferred from the existing handlers and the routes the UI calls.

| Handler | Route |
|---|---|
| `ISearchyHandler` or keyless `IQueryHandler` | `GET /api/{area}` |
| `IGetHandler<TKey, …>` | `GET /api/{area}/{key}` |
| Keyless `ICommandHandler<TRequest, …>` | `POST /api/{area}` |
| Keyed `ICommandHandler<TKey, TRequest, …>` named `Update` | `POST /api/{area}/{key}` |
| Keyed handler with any other class name | Adds the class name, as in `POST /api/apigateways/{id}/addpartner` |
| `IDeleteHandler<TKey, …>` | `DELETE /api/{area}/{key}` |
| `[HandlerName("name")]` | Adds `/name` |

Every endpoint needs a signed-in caller unless the class has `[Unprotect]`. Check the permission first thing in the handler.

```csharp
await _requestContext.EnsurePermission(_dbContext, Model.Permissions.Subscriptions.Edit);
```

Always save with `SaveChangesAsync`. The synchronous `SaveChanges` throws, because it would skip the audit trail. After changing cached configuration, call `IInfolinkCache.BroadcastRevoke()` so every instance reloads.

## Adding a native adapter

1. Create a folder in `SW.Bitween.NativeAdapters` with an input class. Annotate its properties with `[Required]`, `[Description]`, `[DefaultValue]` and, for secrets, `[Secure]`.
2. Create the adapter class. Implement `INativeInfolinkHandler`, `INativeInfolinkReceiver`, `INativeInfolinkValidator` or `INativeInfolinkMapper`. Its `Name` must start with `Native`, `StartupValuesType` returns the input class, and `InitializeStartupValues` reads the properties.
3. Register it twice in `ServiceCollectionExtensions.AddNativeAdapters`: once as its kind's interface and once as `INativeAdapter`.
4. Implement `IRequiresRebexLicense` if it needs Rebex. Implement `IReceivesMappingContext` if it is a mapper that should receive partner and global values as context.

Adapters are found by name, ignoring case, and the name is the id stored on subscriptions. Renaming an adapter breaks every subscription that uses it.

## Adding a permission

1. Add the constant and its catalogue entry in `SW.Bitween.Sdk/Model/Permissions.cs`. `PermissionCatalogTests` checks that the two agree.
2. Guard the handlers with `EnsurePermission`.
3. Gate the route in `ClientApp/src/router.tsx` and the navigation entry in `ClientApp/src/nav.ts`.

Administrators get new permissions automatically. Members and Viewers get them when the area belongs to the Operate, Subscriptions or Configuration group.

## Adding a setting

1. Add the property to `BitweenOptions` or `ThemeOptions`.
2. Add a definition to `SettingsCatalog`. Make it editable only if every consumer reads the options object each time it uses the value. Anything read once at startup must be read-only.
3. Add an `OnChange` action when assigning the value is not enough, as the retry job cron does.

## Adding a scheduled job

Model it on `ReceiveAttemptCleanupJob`: mark the class with `[ScheduleConfig(AllowConcurrentExecution = false, MisfireInstructions = Skip)]`, and register its schedule in `SchedulerSeedService`.

## Conventions

- Code comments explain decisions in detail. Read them before changing behaviour.
- `.editorconfig` prefers primary constructors, file-scoped namespaces and pattern matching.
- The admin UI's permission catalogue is served by the API, so the backend is the single source of truth.
