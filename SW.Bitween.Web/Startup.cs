using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using OpenTelemetry.Trace;
using OpenTelemetry.Resources;
using OpenTelemetry.Metrics;
using OpenTelemetry;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using SW.Bus;
using SW.CloudFiles.AS.Extensions;
using SW.CqApi;
using SW.CloudFiles.Extensions;
using SW.Serverless;
using SW.EfCoreExtensions;
using SW.HttpExtensions;
using SW.Bitween.JsonConverters;
using SW.Logger;
using SW.Bitween.Sdk;
using SW.PrimitiveTypes;
using SW.SimplyRazor;
using Newtonsoft.Json.Serialization;
using Npgsql;
using SW.Bitween.Domain;
using SW.Bitween.Resources.Accounts;
using SW.Bitween.Services;
using SW.Bitween.Services.Cluster;
using SW.Bitween.Services.DataSources;
using SW.Serverless.Resident;
using SW.CqApi.AuthOptions;
using SW.Logger.Console;
using Azure.Identity;
using Microsoft.Data.SqlClient;
using SW.Bitween.NativeAdapters;
using SW.Scheduler;
using SW.Scheduler.EfCore;
using SW.Scheduler.MySql;
using SW.Scheduler.PgSql;
using SW.Scheduler.SqlServer;
using SqlAuthenticationProvider = Microsoft.Data.SqlClient.SqlAuthenticationProvider;
using SqlAuthenticationMethod = Microsoft.Data.SqlClient.SqlAuthenticationMethod;
using SW.Bitween.Services.Adapters;

namespace SW.Bitween.Web
{
    public class Startup(IConfiguration configuration, IWebHostEnvironment environment)
    {
        private static readonly string ApiXchangeCreatedEventQueueName = "XchangeService.ApiXchangeCreatedEvent";

        private IConfiguration Configuration { get; } = configuration;
        private IWebHostEnvironment Environment { get; } = environment;

        public void ConfigureServices(IServiceCollection services)
        {
            var bitweenOptions = new BitweenOptions();
            var themeOptions = new ThemeOptions();
            Configuration.GetSection(BitweenOptions.ConfigurationSection).Bind(bitweenOptions);
            Configuration.GetSection(ThemeOptions.ConfigurationSection).Bind(themeOptions);
            services.AddSingleton(themeOptions);
            services.AddSingleton(bitweenOptions);

            // The same passphrase the settings secrets use, now also for the credential columns.
            SecretColumnCipher.Configure(bitweenOptions.SettingsEncryptionKey,
                Configuration["Bitween:PreviousSettingsEncryptionKey"]);
            // Keeps both option objects in sync with the Settings table, which owns every editable
            // setting; Program hands configuration over to it at boot. The protector encrypts
            // secret settings before they're stored.
            services.AddSingleton<SettingsProtector>();
            services.AddSingleton<SettingsService>();
            services.AddMemoryCache();
            services.AddSingleton<SignInThrottle>();
            services.AddSingleton<IInfolinkCache, InMemoryBitweenCache>();
            services.AddSingleton<FilterService>();
            services.AddScoped<NativeAdapterDiscoveryService>();
            services.AddSingleton<ServerlessAdapterDescriber>();
            services.AddScoped<AdapterStartupValues>();
            services.AddScoped<Resources.Adapters.AdapterListing>();
            services.AddScoped<AdapterSecretProperties>();
            services.AddScoped<RetryUsageReport>();
            // Registration ORDER is the routing order: each runtime is asked whether an
            // adapter is its own, and the classic one claims everything, so it must be asked last.
            services.AddScoped<IAdapterRuntime, NativeAdapterRuntime>();
            services.AddScoped<IAdapterRuntime, ResidentAdapterRuntime>();
            services.AddScoped<IAdapterRuntime, ClassicAdapterRuntime>();
            services.AddScoped<IAdapterInvoker, AdapterInvoker>();
            services.AddScoped<MappingContextFactory>();
            services.AddScoped<XchangeService>();
            services.AddSingleton<FileLinks>();
            services.AddSingleton<StorageRetention>();
            services.AddSingleton<StorageAccess>();
            services.AddScoped<ExchangeArchive>();
            services.AddScoped<RetentionPlanner>();
            services.AddScoped<GatewayCallers>();
            services.AddSingleton<IGatewayIssuers, OpenIdGatewayIssuers>();
            services.AddScoped<Resources.Ops.LaneResolver>();
            services.AddScoped<Resources.Ops.BrokerQueues>();
            services.AddScoped<Resources.Ops.DeadLetterQueues>();
            services.AddScoped<AdapterRequirements>();
            services.AddHttpContextAccessor();

            services.AddScoped<SubscriptionSchedulerService>();
            services.AddHostedService<SchedulerSeedService>();
            // Publishes what didn't go out straight after its commit; see OutboxMessage.
            services.AddHostedService<OutboxDispatcher>();
            // Encrypts credential columns written before encryption was on; see SecretColumnCipher.
            services.AddHostedService<SecretColumnEncryptionPass>();

            services.AddBitweenLogging(Configuration, Environment, options =>
            {
                options.ApplicationName = bitweenOptions.QueuePrefix;
            });
            services.AddSingleton<IHttpContextFactory>(sp =>
                new EdgeRequestIdHttpContextFactory(new DefaultHttpContextFactory(sp)));

            services.AddBus(config =>
            {
                config.ApplicationName = bitweenOptions.QueuePrefix;
                config.DefaultQueuePrefetch = bitweenOptions.BusDefaultQueuePrefetch!.Value;
                config.ManagementUrl = bitweenOptions.RabbitMqManagementUrl;
                config.ManagementUsername = bitweenOptions.RabbitMqManagementUsername;
                config.ManagementPassword = bitweenOptions.RabbitMqManagementPassword;
                config.AddQueueOption("XchangeService.ApiXchangeCreatedEvent", priority: 10);
            });
            services.AddBusPublish();
            services.AddBusConsume(typeof(BitweenDbContext).Assembly);

            var serializer = new JsonSerializer();
            serializer.Converters.Add(new PropertyMatchSpecificationJsonConverter());
            serializer.Converters.Add(new MatcherJsonConverter());
            serializer.Converters.Add(new DelayStrategyJsonConverter());
            serializer.Converters.Add(new Newtonsoft.Json.Converters.StringEnumConverter());
            serializer.ContractResolver = new CamelCasePropertyNamesContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy
                {
                    ProcessDictionaryKeys = false
                }
            };

            services.AddCqApi(configure =>
                {
                    //configure.RolePrefix = "Bitween";
                    configure.UrlPrefix = "api";
                    configure.ProtectAll = true;
                    configure.Serializer = serializer;
                    configure.AuthOptions = new CqApiAuthOptions
                    {
                        AuthType = AuthType.OAuth2
                    };
                },
                typeof(BitweenDbContext).Assembly
            );

            services.AddApiClient<BitweenClient, BitweenClientOptions>();
            switch (bitweenOptions.StorageProvider.ToUpper())
            {
                case "AS":
                    services.AddAsCloudFiles();
                    // Azure decides privacy per container, and the storage library creates it public.
                    services.AddHostedService<PrivateAzureContainer>();
                    break;
                case "OC":
                    services.AddOracleCloudFiles();
                    break;
                case "S3":
                    services.AddS3CloudFiles();
                    break;
                case "LOCAL":
                    if (!Environment.IsDevelopment())
                        throw new InvalidOperationException(
                            "StorageProvider 'Local' stores files on the local filesystem and is only allowed when ASPNETCORE_ENVIRONMENT is 'Development'.");
                    services.AddLocalTestsCloudFiles();
                    break;
                default:
                    services.AddS3CloudFiles();
                    break;
            }

            services.AddServerless(configure =>
            {
                configure.CommandTimeout = bitweenOptions.ServerlessCommandTimeout;
                configure.AdapterRemotePath = bitweenOptions.AdapterPath;
            });

            // Describes what each bus provider accepts, read from the adapter packages. Not
            // behind BusProvidersEnabled: a node that does not run connections still configures
            // them, and describing an adapter never starts one.
            services.AddSingleton<DataSourceProviderCatalog>();

            // Scoped: it reads subscriptions through the request's DbContext.
            services.AddScoped<StatementUsageReader>();

            // Scoped for the same reason: it reads the data source through the request's DbContext.
            // IResidentAdapterHost is an optional dependency, so this resolves on a node with
            // resident adapters turned off too — it just never has anyone to ask.
            services.AddScoped<StatementValidator>();

            // Resident data source providers — brokers and databases both. Off by default because
            // it is opt-in, not because it is unsafe to run on more than one node: a broker
            // connection is exclusive, and every
            // data source is held through a lease with a database-issued fencing term, so only
            // one node consumes any given source. See BusProviderSupervisor and ILeaderElection.
            if (bitweenOptions.BusProvidersEnabled)
            {
                services.AddResidentAdapters<BusProviderEventSink, BitweenAdapterStateStore>(configure =>
                {
                    configure.HeartbeatInterval = TimeSpan.FromSeconds(15);
                    configure.MaxInFlight = bitweenOptions.BusProviderMaxInFlight;
                    // The same ceiling classic adapters get, so a resident handler or mapper is not
                    // allowed longer than the classic one it replaces (both default to 300 seconds).
                    if (bitweenOptions.ServerlessCommandTimeout > 0)
                        configure.InvokeTimeout = TimeSpan.FromSeconds(bitweenOptions.ServerlessCommandTimeout);
                });
                // One election implementation, deliberately — see ILeaderElection's remarks.
                services.AddSingleton<ILeaderElection, RabbitMqLeaderElection>();
                services.AddHostedService<BusProviderSupervisor>();
            }
            services.AddScoped<RequestContext>();

            // Get and validate connection string
            var connectionString = Configuration.GetConnectionString(BitweenDbContext.ConnectionString);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"Connection string '{BitweenDbContext.ConnectionString}' is not configured. " +
                    "Please check your appsettings.json or environment configuration.");
            }

            // For SQL Server + managed identity, augment the connection string up front so both
            // the Quartz scheduler below and the DbContext registered later use the exact same
            // (fully authenticated) value — previously this was only applied after the scheduler
            // had already captured the un-augmented string, so Quartz would fail to authenticate.
            if (bitweenOptions.UseAzureManagedIdentity &&
                bitweenOptions.DatabaseType.Equals(RelationalDbType.MsSql.ToString(), StringComparison.OrdinalIgnoreCase) &&
                !connectionString.Contains("Authentication=", StringComparison.OrdinalIgnoreCase))
            {
                connectionString += ";Authentication=Active Directory Default";
            }

            // Register the persistent Quartz scheduler using the same DB as Bitween.
            // NOTE: clustering is only guaranteed once SimplyWorks.Scheduler.* is bumped past
            // 8.1.1 (the version pinned in the .csproj files as of this comment) — the fix that
            // makes clustering unconditional (unique auto-generated SchedulerId per instance)
            // hasn't been published yet. Until that bump happens, these packages run
            // NON-clustered (EnableClustering defaulted to false and no longer settable here).
            if (string.Equals(bitweenOptions.DatabaseType, RelationalDbType.PgSql.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                // For PostgreSQL + managed identity, Npgsql has no "Authentication=" connection
                // string keyword equivalent to SQL Server's — SW.Scheduler.PgSql's
                // AzureManagedIdentityNpgsqlDbProvider handles this by fetching a fresh AAD token
                // and supplying it as Password= on every physical connection Quartz opens, so no
                // password needs to be (or should be) present in the connection string itself.
                services.AddPgSqlScheduler(
                    pg =>
                    {
                        pg.ConnectionString = connectionString;
                        pg.Schema = PgSql.BitweenDbContext.Schema;
                        pg.UseAzureManagedIdentity = bitweenOptions.UseAzureManagedIdentity;
                        pg.AzureManagedIdentityClientId = bitweenOptions.AzureManagedIdentityClientId;
                    },
                    assemblies: new[] { typeof(BitweenDbContext).Assembly });
            }
            else if (string.Equals(bitweenOptions.DatabaseType, RelationalDbType.MsSql.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                services.AddSqlServerScheduler(
                    connectionString: connectionString,
                    assemblies: typeof(BitweenDbContext).Assembly);
            }
            else
            {
                // MySql (default)
                services.AddMySqlScheduler(
                    connectionString: connectionString,
                    assemblies: typeof(BitweenDbContext).Assembly);
            }

            // Configure Azure Managed Identity for SQL Server if enabled
            if (bitweenOptions.UseAzureManagedIdentity &&
                bitweenOptions.DatabaseType.Equals(RelationalDbType.MsSql.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                var authProvider = new AzureSqlAuthenticationProvider(bitweenOptions.AzureManagedIdentityClientId);
                SqlAuthenticationProvider.SetProvider(SqlAuthenticationMethod.ActiveDirectoryDefault, authProvider);
            }

            if (string.Equals(bitweenOptions.DatabaseType, RelationalDbType.PgSql.ToString(),
                    StringComparison.CurrentCultureIgnoreCase))
            {
                // Validate PostgreSQL connection string format
                if (!connectionString.Contains("Host=", StringComparison.OrdinalIgnoreCase) &&
                    !connectionString.Contains("Server=", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"PostgreSQL connection string is missing 'Host=' or 'Server=' parameter. " +
                        $"Connection string: '{connectionString}'. " +
                        "Please check your ConnectionStrings:BitweenDb configuration.");
                }

                // Configure connection with Azure Managed Identity for PostgreSQL if enabled
                if (bitweenOptions.UseAzureManagedIdentity)
                {
                    var tokenProvider = new AzurePostgreSqlTokenProvider(bitweenOptions.AzureManagedIdentityClientId);
                    var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
                    dataSourceBuilder.EnableDynamicJson();

                    // Configure periodic password provider for token refresh
                    dataSourceBuilder.UsePeriodicPasswordProvider(
                        async (_, ct) => await tokenProvider.GetAccessTokenAsync(),
                        TimeSpan.FromMinutes(50), // Refresh token before expiry (typically 60 min)
                        TimeSpan.FromSeconds(10)  // Initial delay
                    );

                    var dataSource = dataSourceBuilder.Build();

                    services.AddDbContext<BitweenDbContext, PgSql.BitweenDbContext>(c =>
                    {
                        // Parameter values in EF's logs are passwords, partner keys and payloads; only
                        // a developer's own machine should ever write them out.
                        if (Environment.IsDevelopment()) c.EnableSensitiveDataLogging();
                        c.UseSnakeCaseNamingConvention();
                        c.UseNpgsql(dataSource, b =>
                        {
                            b.MigrationsHistoryTable("_ef_migrations_history", PgSql.BitweenDbContext.Schema);
                            b.MigrationsAssembly(typeof(PgSql.DbType).Assembly.FullName);
                            b.UseAdminDatabase(bitweenOptions.AdminDatabaseName);
                        });
                    });
                }
                else
                {
                    // Traditional connection string authentication
                    var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
                    dataSourceBuilder.EnableDynamicJson();
                    var dataSource = dataSourceBuilder.Build();

                    services.AddDbContext<BitweenDbContext, PgSql.BitweenDbContext>(c =>
                    {
                        // Parameter values in EF's logs are passwords, partner keys and payloads; only
                        // a developer's own machine should ever write them out.
                        if (Environment.IsDevelopment()) c.EnableSensitiveDataLogging();
                        c.UseSnakeCaseNamingConvention();
                        c.UseNpgsql(dataSource, b =>
                        {
                            b.MigrationsHistoryTable("_ef_migrations_history", PgSql.BitweenDbContext.Schema);
                            b.MigrationsAssembly(typeof(PgSql.DbType).Assembly.FullName);
                            b.UseAdminDatabase(bitweenOptions.AdminDatabaseName);
                        });
                    });
                }

                services.AddSchedulerMonitoring<PgSql.BitweenDbContext>();
            }
            else if (string.Equals(bitweenOptions.DatabaseType, RelationalDbType.MsSql.ToString(),
                StringComparison.OrdinalIgnoreCase))
            {
                services.AddDbContext<BitweenDbContext, MsSql.BitweenDbContext>(c =>
                {
                    // Parameter values in EF's logs are passwords, partner keys and payloads; only
                    // a developer's own machine should ever write them out.
                    if (Environment.IsDevelopment()) c.EnableSensitiveDataLogging();
                    c.UseSqlServer(connectionString,
                        b => { b.MigrationsAssembly(typeof(MsSql.DbType).Assembly.FullName); });
                });
                services.AddSchedulerMonitoring<MsSql.BitweenDbContext>();
            }
            else
            {
                // MySql (default)
                services.AddDbContext<BitweenDbContext, MySql.BitweenDbContext>(c =>
                {
                    // Parameter values in EF's logs are passwords, partner keys and payloads; only
                    // a developer's own machine should ever write them out.
                    if (Environment.IsDevelopment()) c.EnableSensitiveDataLogging();
                    c.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 18)),
                        b => { b.MigrationsAssembly(typeof(MySql.DbType).Assembly.FullName); });
                });
                services.AddSchedulerMonitoring<MySql.BitweenDbContext>();
            }


            services.AddHealthChecks().AddDependencyChecks();

            // Metrics and traces over OTLP, only where a collector is configured — the standard
            // OTEL_EXPORTER_OTLP_ENDPOINT, or OpenTelemetry:Endpoint. Nothing changes for a
            // deployment that sets neither.
            var otlpEndpoint = Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] ?? Configuration["OpenTelemetry:Endpoint"];
            if (!string.IsNullOrWhiteSpace(otlpEndpoint))
            {
                services.AddOpenTelemetry()
                    .ConfigureResource(resource => resource.AddService(
                        Configuration["OTEL_SERVICE_NAME"] ?? "bitween"))
                    .WithMetrics(metrics => metrics
                        .AddMeter(BitweenTelemetry.Name)
                        .AddAspNetCoreInstrumentation()
                        .AddHttpClientInstrumentation()
                        .AddRuntimeInstrumentation())
                    .WithTracing(tracing => tracing
                        .AddSource(BitweenTelemetry.Name)
                        .AddAspNetCoreInstrumentation(o =>
                            o.Filter = context => !context.Request.Path.StartsWithSegments("/health"))
                        .AddHttpClientInstrumentation())
                    .UseOtlpExporter(
                        Configuration["OTEL_EXPORTER_OTLP_PROTOCOL"] == "http/protobuf"
                            ? OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf
                            : OpenTelemetry.Exporter.OtlpExportProtocol.Grpc,
                        new Uri(otlpEndpoint));
            }

            // services.AddRazorPages(options =>
            // {
            //     options.Conventions.AuthorizeFolder("/");
            //     options.Conventions.AllowAnonymousToPage("/Login");
            // });

            // services.AddServerSideBlazor().AddHubOptions(
            //     options => { options.MaximumReceiveMessageSize = 131072; });

            // services.AddSimplyRazor(config =>
            // {
            //     config.DefaultApiClientFactory = sp => sp.GetService<BitweenClient>();
            // });
            RejectSampleSigningKey();
            AddRateLimiting(services);
            services.AddJwtTokenParameters();
            services.AddAuthorization();
            services.AddScoped<RunFlagUpdater>();
            services.AddControllers();

            // Nothing was compressed before this: the SPA bundle went out at its full ~1 MB, and
            // JSON list responses grow with the customer's data. Measured on the current bundle,
            // gzip at Optimal takes it from 1,054 KB to 288 KB for ~15 ms of CPU.
            //
            // Gzip only, deliberately. .NET exposes just two useful Brotli levels and neither wins
            // here: Fastest produces 329 KB (worse than gzip at Optimal) and Optimal produces
            // 233 KB but costs ~0.9 s of CPU per megabyte, paid again by every cold visitor because
            // nothing caches the compressed bytes server-side. Browsers prefer Brotli when it's
            // offered, so registering it at Fastest would actively make the common case worse. The
            // remaining 54 KB is only worth chasing by pre-compressing at build time.
            //
            // The explicit "text/javascript" matters — the static file middleware labels .js files
            // that way, while the framework's default list only names "application/javascript", so
            // relying on the defaults would silently skip the single largest response we serve.
            //
            // EnableForHttps is deliberate. TLS terminates here in local dev (and may in a
            // deployment that doesn't front the pod with a proxy), so leaving it off would mean no
            // compression at all in exactly the place we test it. The BREACH risk it guards against
            // needs a secret and attacker-controlled text in the same response body; the API returns
            // neither — auth tokens travel in headers and the Set-Cookie, never in a GET body.
            services.AddResponseCompression(options =>
            {
                options.EnableForHttps = true;
                options.Providers.Add<GzipCompressionProvider>();
                options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[]
                {
                    "text/javascript",
                    "image/svg+xml",
                });
            });
            services.Configure<GzipCompressionProviderOptions>(options =>
                options.Level = CompressionLevel.Optimal);


            services.AddAuthentication()
                .AddJwtBearer(configureOptions =>
                {
                    configureOptions.RequireHttpsMetadata = false;
                    configureOptions.SaveToken = true;
                    configureOptions.TokenValidationParameters = new TokenValidationParameters()
                    {
                        ValidIssuer = Configuration["Token:Issuer"],
                        ValidAudience = Configuration["Token:Audience"],
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Configuration["Token:Key"]))
                    };
                    // A partner calling a gateway sends its key as a bearer token, which is not ours to
                    // validate: reading it as an admin login fails on every call and logs a failed
                    // sign-in each time. The gateway checks its own callers.
                    configureOptions.Events = new JwtBearerEvents
                    {
                        OnMessageReceived = context =>
                        {
                            if (context.Request.Path.StartsWithSegments("/api/gateway"))
                                context.NoResult();
                            return Task.CompletedTask;
                        },
                    };
                });

            services.AddCors(options =>
            {
                options.AddDefaultPolicy(
                    builder =>
                    {
                        if (bitweenOptions.CorsOrigins != null && bitweenOptions.CorsOrigins.Length > 0)
                        {
                            builder.WithOrigins(bitweenOptions.CorsOrigins)
                                   .AllowAnyHeader()
                                   .AllowAnyMethod()
                                   .AllowCredentials();
                        }
                        // When no origins are configured, allow no cross-origin access.
                        // The SPA is served same-origin, so CORS is only needed for
                        // split-host / local-dev setups that set CorsOrigins explicitly.
                    });
            });

            // Resolved per adapter instance so a license key saved in Settings applies without a restart.
            services.AddNativeAdapters(
                sp => sp.GetRequiredService<BitweenOptions>().RebexLicenseKey,
                sp => sp.GetRequiredService<BitweenOptions>().BlockPrivateNetworkAddresses);

            // services.AddScoped<INativeInfolinkHandler, NativeUpdatePartnerPropsHandler>();
            // services.AddScoped<INativeAdapter, NativeUpdatePartnerPropsHandler>();

        }


        /// <summary>
        /// Enforced. Proven Report-Only first across every page in the admin UI — a wrong
        /// enforced policy blanks the app, so the order matters. Switch back to
        /// "Content-Security-Policy-Report-Only" before widening the policy again.
        /// </summary>
        private const string ContentSecurityPolicyHeader = "Content-Security-Policy";

        /// <summary>
        /// Where the Microsoft sign-in popup lands — the page registered as the Azure AD
        /// redirect URI. Deliberately not the app root: index.html boots the admin UI, which
        /// finds no session and navigates to /login, dropping the "#code=..." fragment the
        /// sign-in depends on.
        /// </summary>
        private const string MsalRedirectPath = "/blank.html";

        /// <summary>Mirrors the policy the legacy UI enforces at nginx, minus its nginx-only bits.</summary>
        private const string ContentSecurityPolicy =
            "default-src 'self'; " +
            // No CDN entry, deliberately. The Scriban mapping editor used to lazy-load
            // Monaco from jsdelivr, which meant a third party could serve executable
            // code into this app. It runs on CodeMirror now, bundled with everything
            // else, so nothing outside this origin is allowed to run.
            "script-src 'self'; " +
            "style-src 'self' 'unsafe-inline'; " +
            "img-src 'self' data:; " +
            "connect-src 'self' https://login.microsoftonline.com; " +
            "frame-src https://login.microsoftonline.com; " +
            "font-src 'self' data:; " +
            "form-action 'self' https://login.microsoftonline.com; " +
            "frame-ancestors 'none'; " +
            "base-uri 'self'; " +
            "object-src 'none'";

        /// <summary>
        /// Says so at startup when Microsoft sign-in points anywhere but <see cref="MsalRedirectPath"/>.
        /// <para>
        /// An upgrade cannot move a deployment onto the right redirect URI by itself. A stored setting
        /// wins over configuration once its row exists (see SettingsService), so editing Helm or
        /// appsettings has no effect, and the URI has to be registered on the Azure AD app by hand
        /// regardless — rewriting the value here would only trade a hung popup for AADSTS50011.
        /// A warning is all this can honestly do, and it beats leaving a sign-in that hangs with no
        /// explanation as the only symptom.
        /// </para>
        /// </summary>
        private static void WarnIfMsalRedirectUriIsStale(IApplicationBuilder app)
        {
            var options = app.ApplicationServices.GetRequiredService<BitweenOptions>();
            if (string.IsNullOrWhiteSpace(options.MsalClientId)) return;

            var redirectUri = options.MsalRedirectUri ?? "";
            if (redirectUri.TrimEnd('/').EndsWith(MsalRedirectPath, StringComparison.OrdinalIgnoreCase)) return;

            app.ApplicationServices.GetRequiredService<ILogger<Startup>>().LogWarning(
                "Microsoft sign-in redirect URI is {RedirectUri}, which is not this instance's sign-in " +
                "landing page {LandingPage}. The popup has to land there: every other page sends a " +
                "Cross-Origin-Opener-Policy that severs it from the window that opened it, and MSAL then " +
                "fails the sign-in with \"user_cancelled\". Register the landing page on the Azure AD app " +
                "and set Bitween.MsalRedirectUri to match.",
                redirectUri.Length == 0 ? "(not set)" : redirectUri, MsalRedirectPath);
        }

        /// <summary>
        /// The signing key every token is minted and verified with. Anyone holding it can mint a
        /// token for any identity, including one that has no account, so the one value that must
        /// never be shared between deployments is this one.
        /// </summary>
        /// <remarks>
        /// The sample below ships as the default in our public deployment chart, which is exactly
        /// why it has to be rejected: a deployment that never overrode it looked completely healthy
        /// — tokens were issued, signatures verified — while anyone who had read the chart could
        /// mint their own. A penetration test did precisely that.
        /// <para>
        /// Checked in every environment, deliberately. Exempting Development would have made this
        /// inert exactly where it is needed most: the chart also defaults ASPNETCORE_ENVIRONMENT to
        /// Development, so the deployments most likely to be running an untouched chart are the
        /// ones that would have skipped the check. Local development uses its own key instead,
        /// which is what the settings files carry.
        /// </para>
        /// <para>
        /// Refusing to start is the point. There is no degraded mode worth offering: running on a
        /// public key is indistinguishable, from the inside, from having no authentication at all.
        /// </para>
        /// </remarks>
        private const string SampleSigningKey = "6547647654764764767657658658758765876532542";

        private const int MinimumSigningKeyLength = 32;

        private void RejectSampleSigningKey()
        {
            var key = Configuration["Token:Key"];

            if (string.IsNullOrWhiteSpace(key))
                throw new InvalidOperationException(
                    "Token:Key is not configured. Generate a random secret unique to this " +
                    "deployment — tokens cannot be trusted without one.");

            // The length our own configuration documents. A short key is brute-forceable offline
            // against any token the holder has seen, which is every token they were ever issued.
            if (key.Length < MinimumSigningKeyLength)
                throw new InvalidOperationException(
                    $"Token:Key is {key.Length} characters; it must be at least " +
                    $"{MinimumSigningKeyLength}. Generate a random secret rather than choosing one.");

            if (key == SampleSigningKey)
                throw new InvalidOperationException(
                    "Token:Key is still the sample value that ships as the default in the " +
                    "deployment chart, and it is published in a public repository — anyone can " +
                    "mint a valid token with it. Set Token:Key (env: Token__Key) to a random " +
                    "secret unique to this deployment. Doing so signs out everyone holding a " +
                    "token issued under the old key, which is the intended outcome.");
        }

        /// <summary>
        /// One limit over the whole application, rather than an attribute remembered endpoint by
        /// endpoint.
        /// </summary>
        /// <remarks>
        /// A penetration test ran forty-six passwords at the login endpoint in a single
        /// uninterrupted burst; nothing throttled, delayed or blocked any of them, and response
        /// times stayed flat throughout. Nothing else in the API behaved differently — the whole
        /// dataset could be pulled as fast as it could be asked for.
        /// <para>
        /// Partitioned by account where there is one and by address otherwise. Partitioning
        /// everything by address would have made one office behind a single NAT share one budget,
        /// which turns a busy afternoon into an outage; partitioning by account cannot work before
        /// anyone has signed in, which is exactly where the strict limit is needed. Address is read
        /// after <c>UseForwardedHeaders</c>, so it is the client rather than the ingress.
        /// </para>
        /// <para>
        /// Sign-in is deliberately far tighter than everything else. Ten attempts a minute is more
        /// than any person needs and nowhere near enough to work through a password list. It is not
        /// a replacement for the per-account lockout, which counts attempts against one account
        /// across every address; this counts them per address across every account.
        /// </para>
        /// <para>
        /// Each number can be overridden through <c>Bitween:RateLimits</c> (<c>SignInPerMinute</c>,
        /// <c>RequestsPerMinute</c>, <c>FileLinksPerMinute</c>), and is unchanged wherever
        /// that is not set. The end-to-end suite needs it: it drives the UI far faster than a person,
        /// all as one account, and spends the per-account budget several times over in a run.
        /// </para>
        /// </remarks>
        private void AddRateLimiting(IServiceCollection services)
        {
            var limits = Configuration.GetSection("Bitween:RateLimits");
            var signInLimit = limits.GetValue("SignInPerMinute", 10);
            var requestLimit = limits.GetValue("RequestsPerMinute", 600);
            var fileLinkLimit = limits.GetValue("FileLinksPerMinute", 60000);

            // UseForwardedHeaders does nothing until it is told which headers to read, so every
            // request looked like it came from the ingress: one shared sign-in budget for the whole
            // world, which a single client could spend for everyone. ForwardLimit 1 reads only the
            // address the nearest proxy appended, so whatever a client writes into the header
            // itself is ignored. Any proxy is trusted unless Bitween:TrustedProxies names them,
            // since an ingress's address is rarely known ahead of time.
            services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                options.ForwardLimit = 1;
                options.KnownIPNetworks.Clear();
                options.KnownProxies.Clear();
                foreach (var entry in (Configuration["Bitween:TrustedProxies"] ?? "")
                             .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(entry.Contains('/') ? entry : entry + (entry.Contains(':') ? "/128" : "/32")));
            });

            services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

                options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                {
                    if (IsSignInPath(context.Request.Path))
                        return RateLimitPartition.GetFixedWindowLimiter(
                            $"signin:{ClientAddress(context)}",
                            _ => new FixedWindowRateLimiterOptions
                            {
                                PermitLimit = signInLimit,
                                Window = TimeSpan.FromMinutes(1)
                            });

                    // File links have a budget of their own: an aggregation's handler downloads every
                    // link in a roll-up — up to 10,000 — and the storage URLs they replace had no limit.
                    // Still bounded, and a bad seal is turned away before storage is touched.
                    if (context.Request.Path.StartsWithSegments("/" + FileLinks.RoutePrefix))
                        return RateLimitPartition.GetFixedWindowLimiter(
                            $"files:{ClientAddress(context)}",
                            _ => new FixedWindowRateLimiterOptions
                            {
                                PermitLimit = fileLinkLimit,
                                Window = TimeSpan.FromMinutes(1)
                            });

                    var account = context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                    return RateLimitPartition.GetFixedWindowLimiter(
                        account is null ? $"anon:{ClientAddress(context)}" : $"account:{account}",
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = requestLimit,
                            Window = TimeSpan.FromMinutes(1)
                        });
                });
            });
        }

        /// <summary>
        /// Matched on the trailing segment so that a sign-in route added later is covered the day
        /// it appears rather than the day someone remembers this list.
        /// </summary>
        /// <remarks>
        /// Narrowed to the API prefix because the SPA has a <c>/login</c> route of its own, served
        /// through the fallback further down this pipeline. Without that, reloading the sign-in
        /// page ten times would spend the whole sign-in budget on page loads and lock someone out
        /// of an app they had not yet tried to enter.
        /// </remarks>
        private static bool IsSignInPath(PathString path)
        {
            if (!path.HasValue) return false;

            // Routing treats "/api/accounts/login/" as the same endpoint, so the limit has to as
            // well — otherwise one trailing character moves an attempt to the general budget.
            var value = path.Value!.TrimEnd('/');

            return value.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
                && value.EndsWith("/login", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// What a token that still has to change its password may call: the sign-in and
        /// self-service endpoints the change-password page needs, and nothing else.
        /// </summary>
        /// <remarks>
        /// <c>POST /api/xchanges</c> is left open on purpose. Whether and how to guard it is being
        /// studied separately, because it has been reachable this way since r6 and how deployments
        /// use it is not yet known.
        /// </remarks>
        private static bool MustChangePasswordAllows(HttpRequest request)
        {
            var path = request.Path.Value!.TrimEnd('/');

            if (path.Equals("/api/xchanges", StringComparison.OrdinalIgnoreCase) &&
                HttpMethods.IsPost(request.Method))
                return true;

            return MustChangePasswordPaths.Contains(path);
        }

        private static readonly HashSet<string> MustChangePasswordPaths = new(StringComparer.OrdinalIgnoreCase)
        {
            "/api/accounts/login",
            "/api/accounts/logout",
            "/api/accounts/profile",
            "/api/accounts/changepassword",
            "/api/settings/config",
            "/api/settings/myversion",
            "/api/permissions",
        };

        private static string ClientAddress(HttpContext context) =>
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            var apiDocsExposed = app.ApplicationServices.GetRequiredService<BitweenOptions>().ExposeApiDocs;

            app.UseSWConsoleLogger();
            WarnIfMsalRedirectUriIsStale(app);
            app.UseForwardedHeaders();

            // The API description is a map of the whole surface — every route, every request and
            // response shape — and nobody who should have it needs to fetch it from a running
            // deployment. Closed here, ahead of everything else, because the two halves are served
            // by different things: the UI page below, and /api/swagger.json by CqApi itself, which
            // offers no switch of its own. One gate covers both.
            if (!apiDocsExposed)
            {
                app.Use(async (context, next) =>
                {
                    var path = context.Request.Path;
                    if (path.StartsWithSegments("/swagger") ||
                        path.Equals("/api/swagger.json", StringComparison.OrdinalIgnoreCase))
                    {
                        context.Response.StatusCode = StatusCodes.Status404NotFound;
                        return;
                    }

                    await next();
                });
            }

            // Early, so everything downstream — static files, the SPA fallback, every API
            // response — is compressed on the way out.
            app.UseResponseCompression();

            app.Use(async (context, next) =>
            {
                var headers = context.Response.Headers;
                headers["X-Frame-Options"] = "DENY";
                headers["X-Content-Type-Options"] = "nosniff";
                headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
                headers["X-Permitted-Cross-Domain-Policies"] = "none";

                // Cross-Origin-Opener-Policy, with the sign-in landing page carved out.
                //
                // "same-origin-allow-popups" is what lets this app keep its handle on a popup it
                // opened, and MSAL needs that handle: it polls the popup's URL for the "#code=..."
                // Azure AD appends (PopupClient.monitorPopupForHash).
                //
                // The landing page itself has to stay "unsafe-none". The popup arrives there
                // directly from login.microsoftonline.com, which sends no COOP, and a document
                // whose COOP does not match the one it is replacing forces a browsing context
                // group switch — which severs the opener's handle. The symptom is deceptive:
                // popup.closed reads true while the popup is plainly still on screen, so MSAL
                // takes the poll's closed branch and fails the sign-in with "user_cancelled".
                headers["Cross-Origin-Opener-Policy"] =
                    string.Equals(context.Request.Path.Value, MsalRedirectPath, StringComparison.OrdinalIgnoreCase)
                        ? "unsafe-none"
                        : "same-origin-allow-popups";

                // Content-Security-Policy for the admin UI.
                //
                // The legacy deployment set this in nginx, which only ever served the SPA.
                // Here the same host also serves Swagger UI, which needs inline scripts and
                // styles of its own, so the policy is scoped to everything else — applied
                // globally it would simply take Swagger down.
                //
                // 'unsafe-inline' is present for styles only: the brand colour is applied at
                // runtime as custom properties. Scripts need no such exemption — the built
                // index.html carries no inline script, only the module bundle.
                if (!context.Request.Path.StartsWithSegments("/swagger"))
                    headers[ContentSecurityPolicyHeader] = ContentSecurityPolicy;

                // Every Cache-Control decision lives here, in one ordered set of rules, so they
                // cannot contradict each other. Deferred to OnStarting because the content type
                // is only known once whatever handled the request has decided what it's sending.
                context.Response.OnStarting(() =>
                {
                    var contentType = context.Response.ContentType ?? "";
                    var isJson = contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase) ||
                                 contentType.Contains("+json", StringComparison.OrdinalIgnoreCase);

                    if (isJson)
                    {
                        // Sensitive API responses must not be cached by the browser or intermediaries.
                        context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
                    }
                    else if (context.Request.Path.StartsWithSegments("/assets"))
                    {
                        // Vite content-hashes every filename under /assets, so the bytes behind a
                        // given URL never change — a new build produces new URLs. Saying so lets a
                        // returning browser skip the request entirely. Without this header it isn't
                        // told anything and falls back to guessing a freshness window from the
                        // file's age, which differs between browsers and shrinks after each deploy.
                        context.Response.Headers["Cache-Control"] = "public, max-age=31536000, immutable";
                    }
                    else if (contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase))
                    {
                        // index.html is the one file whose URL survives a deploy, and it carries the
                        // hashed asset names. It has to be revalidated every time: a heuristically
                        // cached copy would keep pointing at assets the new build has replaced.
                        context.Response.Headers["Cache-Control"] = "no-cache";
                    }

                    return Task.CompletedTask;
                });

                await next();
            });

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseCors();
            app.UseDefaultFiles();
            app.UseStaticFiles();
            app.UseRouting();
            app.UseAuthentication();
            // After authentication, so an authenticated request is counted against its account
            // rather than against whatever address it shares with everyone else in the building.
            app.UseRateLimiter();
            app.UseAuthorization();

            // A signed-in caller who is refused gets 403, not 401. Handlers refuse with
            // SWUnauthorizedException, which CqApi renders as 401 — the answer for a missing or expired
            // token — so the admin UI took every permission denial for an expired session and spent a
            // refresh on it. Each refresh rotates the refresh cookie, and one whose response was lost
            // to a navigation left the browser holding a cookie that no longer worked: signed out. A
            // request whose token is missing or expired is not authenticated here and keeps its 401.
            app.Use(async (context, next) =>
            {
                if (context.Request.Path.StartsWithSegments("/api") &&
                    context.User.Identity?.IsAuthenticated == true)
                {
                    context.Response.OnStarting(() =>
                    {
                        if (context.Response.StatusCode == StatusCodes.Status401Unauthorized)
                            context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return Task.CompletedTask;
                    });
                }

                await next();
            });

            // A token for an account whose password nobody has chosen reaches self-service and
            // nothing else. GetPermissions already grants such a token nothing, but that only
            // protects handlers that ask for a permission; a handler that forgets to ask was open
            // to it, and the seeded administrator's password is published. Enforced here, once,
            // so the next handler that forgets cannot reopen it.
            app.Use(async (context, next) =>
            {
                if (context.User.FindFirst(RequestContextExtensions.MustChangePasswordClaim) is not null &&
                    context.Request.Path.StartsWithSegments("/api") &&
                    !MustChangePasswordAllows(context.Request))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }

                await next();
            });

            app.UseHttpAsRequestContext();
            SW.Logger.Console.IAppBuilderExtensions.UseRequestContextLogEnricher(app);

            if (apiDocsExposed)
                app.UseSwaggerUI(c => { c.SwaggerEndpoint("/api/swagger.json", "Bitween Api"); });


            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                // /health keeps answering as it always has — the process is up — because probes
                // already point at it. /health/live says the same under the conventional name, and
                // /health/ready also asks the database and the broker; see DependencyHealthChecks.
                endpoints.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
                endpoints.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
                endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
                {
                    Predicate = check => check.Tags.Contains(DependencyHealthChecks.ReadyTag)
                });

                // SPA fallback: unmatched, non-file routes get the UI's index.html
                // so client-side routes (e.g. /team/members) survive refresh.
                endpoints.MapFallbackToFile("index.html");
            });
        }
    }
}