using System;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
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
using SW.Logger.ElasticSerach;
using Azure.Identity;
using Microsoft.Data.SqlClient;
using SW.Bitween.NativeAdapters;
using SqlAuthenticationProvider = Microsoft.Data.SqlClient.SqlAuthenticationProvider;
using SqlAuthenticationMethod = Microsoft.Data.SqlClient.SqlAuthenticationMethod;

namespace SW.Bitween.Web
{
    public class Startup
    {
        private static readonly string ApiXchangeCreatedEventQueueName = "XchangeService.ApiXchangeCreatedEvent";

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        private IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            var bitweenOptions = new BitweenOptions();
            var themeOptions = new ThemeOptions();
            Configuration.GetSection(BitweenOptions.ConfigurationSection).Bind(bitweenOptions);
            Configuration.GetSection(ThemeOptions.ConfigurationSection).Bind(themeOptions);
            services.AddSingleton(themeOptions);
            services.AddSingleton(bitweenOptions);
            services.AddMemoryCache();
            services.AddSingleton<IInfolinkCache, InMemoryBitweenCache>();
            services.AddSingleton<FilterService>();
            services.AddScoped<NativeAdapterDiscoveryService>();
            services.AddScoped<XchangeService>();
            services.AddHttpContextAccessor();

            services.AddHostedService<AggregationService>();
            services.AddHostedService<ReceivingService>();

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
                default:
                    services.AddS3CloudFiles();
                    break;
            }

            services.AddServerless(configure =>
            {
                configure.CommandTimeout = bitweenOptions.ServerlessCommandTimeout;
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
            }
            else
            {
                services.AddDbContext<BitweenDbContext>(c =>
                {
                    c.EnableSensitiveDataLogging();
                    if (string.Equals(bitweenOptions.DatabaseType, RelationalDbType.MySql.ToString(),
                            StringComparison.CurrentCultureIgnoreCase))
                    {
                        // MySQL doesn't support Azure Managed Identity in the same way
                        c.UseMySql(Configuration.GetConnectionString(BitweenDbContext.ConnectionString),
                            new MySqlServerVersion(new Version(8, 0, 18)),
                            b => { b.MigrationsAssembly(typeof(MySql.DbType).Assembly.FullName); });
                    }
                    else if (bitweenOptions.DatabaseType.ToLower() == RelationalDbType.MsSql.ToString().ToLower())
                    {
                        // For Azure Managed Identity with SQL Server, add Authentication parameter
                        if (bitweenOptions.UseAzureManagedIdentity)
                        {
                            // Ensure connection string has the required authentication mode
                            if (!connectionString.Contains("Authentication=", StringComparison.OrdinalIgnoreCase))
                            {
                                connectionString += ";Authentication=Active Directory Default";
                            }
                        }

                        c.UseSqlServer(connectionString,
                            b => { b.MigrationsAssembly(typeof(MsSql.DbType).Assembly.FullName); });
                    }
                });
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
                        else
                        {
                            builder.AllowAnyOrigin();
                            builder.AllowAnyHeader();
                            builder.AllowAnyMethod();
                        }
                    });
            });

            services.AddNativeAdapters(bitweenOptions.RebexLicenseKey);

            // services.AddScoped<INativeInfolinkHandler, NativeUpdatePartnerPropsHandler>();
            // services.AddScoped<INativeAdapter, NativeUpdatePartnerPropsHandler>();

        }


        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            app.UseForwardedHeaders();

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseCors();
            app.UsePathBase("/bitween");
            app.UseStaticFiles();
            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseHttpAsRequestContext();
            app.UseRequestContextLogEnricher();

            app.UseSwaggerUI(c => { c.SwaggerEndpoint("/api/swagger.json", "Bitween Api"); });


            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapHealthChecks("/health");

            });
        }
    }
}