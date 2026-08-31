using System;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
using SW.CqApi.AuthOptions;
using SW.Logger.Console;
using SW.Logger.ElasticSerach;
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

namespace SW.Bitween.Web
{
    public class Startup
    {
        private static readonly string ApiXchangeCreatedEventQueueName = "XchangeService.ApiXchangeCreatedEvent";

        public Startup(IConfiguration configuration, IWebHostEnvironment environment)
        {
            Configuration = configuration;
            Environment = environment;
        }

        private IConfiguration Configuration { get; }
        private IWebHostEnvironment Environment { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            var bitweenOptions = new BitweenOptions();
            var themeOptions = new ThemeOptions();
            Configuration.GetSection(BitweenOptions.ConfigurationSection).Bind(bitweenOptions);
            Configuration.GetSection(ThemeOptions.ConfigurationSection).Bind(themeOptions);
            services.AddSingleton(themeOptions);
            services.AddSingleton(bitweenOptions);
            // Keeps both option objects in sync with the Settings table, which owns every editable
            // setting; Program hands configuration over to it at boot. The protector encrypts
            // secret settings before they're stored.
            services.AddSingleton<SettingsProtector>();
            services.AddSingleton<SettingsService>();
            services.AddMemoryCache();
            services.AddSingleton<IInfolinkCache, InMemoryBitweenCache>();
            services.AddSingleton<FilterService>();
            services.AddScoped<NativeAdapterDiscoveryService>();
            services.AddScoped<AdapterSecretProperties>();
            services.AddScoped<RetryUsageReport>();
            services.AddScoped<AdapterInvoker>();
            services.AddScoped<XchangeService>();
            services.AddScoped<Resources.Ops.LaneResolver>();
            services.AddScoped<AdapterRequirements>();
            services.AddHttpContextAccessor();

            services.AddScoped<SubscriptionSchedulerService>();
            services.AddHostedService<SchedulerSeedService>();

            services.AddSWConsoleLogger(options =>
            {
                options.ApplicationName = bitweenOptions.QueuePrefix;
            });

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
                        c.EnableSensitiveDataLogging();
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
                        c.EnableSensitiveDataLogging();
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
                    c.EnableSensitiveDataLogging();
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
                    c.EnableSensitiveDataLogging();
                    c.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 18)),
                        b => { b.MigrationsAssembly(typeof(MySql.DbType).Assembly.FullName); });
                });
                services.AddSchedulerMonitoring<MySql.BitweenDbContext>();
            }


            services.AddHealthChecks();

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
            services.AddNativeAdapters(sp => sp.GetRequiredService<BitweenOptions>().RebexLicenseKey);

            // services.AddScoped<INativeInfolinkHandler, NativeUpdatePartnerPropsHandler>();
            // services.AddScoped<INativeAdapter, NativeUpdatePartnerPropsHandler>();

        }


        /// <summary>
        /// Enforced. Proven Report-Only first across every page in the admin UI — a wrong
        /// enforced policy blanks the app, so the order matters. Switch back to
        /// "Content-Security-Policy-Report-Only" before widening the policy again.
        /// </summary>
        private const string ContentSecurityPolicyHeader = "Content-Security-Policy";

        /// <summary>Mirrors the policy the legacy UI enforces at nginx, minus its nginx-only bits.</summary>
        private const string ContentSecurityPolicy =
            "default-src 'self'; " +
            // The Scriban mapping editor lazy-loads Monaco, which fetches its script,
            // stylesheet and language workers from jsdelivr — each needs an explicit
            // allowance beyond 'self'.
            "script-src 'self' https://cdn.jsdelivr.net; " +
            "worker-src 'self' blob: https://cdn.jsdelivr.net; " +
            "style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net; " +
            "img-src 'self' data:; " +
            "connect-src 'self' https://login.microsoftonline.com; " +
            "frame-src https://login.microsoftonline.com; " +
            "font-src 'self' data:; " +
            "form-action 'self' https://login.microsoftonline.com; " +
            "frame-ancestors 'none'; " +
            "base-uri 'self'; " +
            "object-src 'none'";

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            app.UseSWConsoleLogger();
            app.UseForwardedHeaders();
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
                headers["Cross-Origin-Opener-Policy"] = "same-origin-allow-popups";

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
            app.UseAuthorization();
            app.UseHttpAsRequestContext();
            SW.Logger.ElasticSerach.IAppBuilderExtensions.UseRequestContextLogEnricher(app);

            app.UseSwaggerUI(c => { c.SwaggerEndpoint("/api/swagger.json", "Bitween Api"); });


            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapHealthChecks("/health");

                // SPA fallback: unmatched, non-file routes get the UI's index.html
                // so client-side routes (e.g. /team/members) survive refresh.
                endpoints.MapFallbackToFile("index.html");
            });
        }
    }
}