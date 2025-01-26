using System;
using System.Text;
using Microsoft.AspNetCore.Authentication.Cookies;
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
using SW.Bitween.Domain;
using SW.Bitween.Resources.Accounts;
using SW.Bitween.Services;
using SW.CqApi.AuthOptions;

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
            services.AddScoped<XchangeService>();
            
            services.AddHostedService<AggregationService>();
            services.AddHostedService<ReceivingService>();

            services.AddBus(config =>
            {
                config.ApplicationName = "bitween";
                config.DefaultQueuePrefetch = bitweenOptions.BusDefaultQueuePrefetch!.Value;
                config.AddQueueOption("XchangeService.ApiXchangeCreatedEvent", priority: 10);
            });
            services.AddBusPublish();
            services.AddBusConsume(typeof(BitweenDbContext).Assembly);

            var serializer = new JsonSerializer();
            serializer.Converters.Add(new PropertyMatchSpecificationJsonConverter());
            serializer.ContractResolver = new CamelCasePropertyNamesContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy
                {
                    ProcessDictionaryKeys = false
                }
            };

            services.AddCqApi(configure =>
                {
                    configure.RolePrefix = "Bitween";
                    configure.UrlPrefix = "api";
                    configure.ProtectAll = true;
                    configure.Serializer = serializer;
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

            if (string.Equals(bitweenOptions.DatabaseType, RelationalDbType.PgSql.ToString(),
                    StringComparison.CurrentCultureIgnoreCase))
            {
                services.AddDbContext<BitweenDbContext, PgSql.BitweenDbContext>(c =>
                {
                    c.EnableSensitiveDataLogging();
                    c.UseSnakeCaseNamingConvention();
                    c.UseNpgsql(Configuration.GetConnectionString(BitweenDbContext.ConnectionString), b =>
                    {
                        b.MigrationsHistoryTable("_ef_migrations_history", PgSql.BitweenDbContext.Schema);
                        b.MigrationsAssembly(typeof(PgSql.DbType).Assembly.FullName);
                        b.UseAdminDatabase(bitweenOptions.AdminDatabaseName);
                    });
                });
            }
            else
            {
                services.AddDbContext<BitweenDbContext>(c =>
                {
                    c.EnableSensitiveDataLogging();
                    if (string.Equals(bitweenOptions.DatabaseType, RelationalDbType.MySql.ToString(),
                            StringComparison.CurrentCultureIgnoreCase))
                    {
                        c.UseMySql(Configuration.GetConnectionString(BitweenDbContext.ConnectionString),
                            new MySqlServerVersion(new Version(8, 0, 18)),
                            b => { b.MigrationsAssembly(typeof(MySql.DbType).Assembly.FullName); });
                    }
                    else if (bitweenOptions.DatabaseType.ToLower() == RelationalDbType.MsSql.ToString().ToLower())
                    {
                        c.UseSqlServer(Configuration.GetConnectionString(BitweenDbContext.ConnectionString),
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


            services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
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
                        builder.AllowAnyOrigin();
                        builder.AllowAnyHeader();
                        builder.AllowAnyMethod();
                    });
            });
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