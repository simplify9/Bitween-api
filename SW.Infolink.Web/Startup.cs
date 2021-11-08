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
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;
using Pomelo.EntityFrameworkCore.MySql.Storage;
using SW.Bus;
using SW.CqApi;
using SW.CloudFiles.Extensions;
using SW.Serverless;
using SW.EfCoreExtensions;
using SW.HttpExtensions;
using SW.Logger;
using SW.Infolink.Sdk;
using SW.PrimitiveTypes;
using SW.SimplyRazor;
//using SW.Infolink.PgSql;

namespace SW.Infolink.Web
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            var infolinkOptions = new InfolinkOptions();
            Configuration.GetSection(InfolinkOptions.ConfigurationSection).Bind(infolinkOptions);
            services.AddSingleton(infolinkOptions);

            services.AddSingleton<FilterService>();
            services.AddScoped<XchangeService>();

            services.AddHostedService<AggregationService>();
            
            
            services.AddHostedService<ReceivingService>();

            services.AddBus(config =>
            {
                config.ApplicationName = "infolink";
                config.DefaultQueuePrefetch = 12;
            });
            services.AddBusPublish();
            services.AddBusConsume(typeof(InfolinkDbContext).Assembly);

            services.AddCqApi(configure =>
            {
                configure.RolePrefix = "Infolink";
                configure.UrlPrefix = "api";
                configure.ProtectAll = true;
            },
            typeof(InfolinkDbContext).Assembly);

            services.AddApiClient<InfolinkClient, InfolinkClientOptions>();
            services.AddCloudFiles();
            services.AddServerless(configure =>
            {
                configure.CommandTimeout = infolinkOptions.ServerlessCommandTimeout;
            });
            services.AddScoped<RequestContext>();

            if (infolinkOptions.DatabaseType.ToLower() == RelationalDbType.PgSql.ToString().ToLower())
            {
                services.AddDbContext<InfolinkDbContext, PgSql.InfolinkDbContext>(c =>
                {
                    c.EnableSensitiveDataLogging(true);
                    c.UseSnakeCaseNamingConvention();
                    c.UseNpgsql(Configuration.GetConnectionString(InfolinkDbContext.ConnectionString), b =>
                    {
                        b.MigrationsHistoryTable("_ef_migrations_history", PgSql.InfolinkDbContext.Schema);
                        b.MigrationsAssembly(typeof(PgSql.DbType).Assembly.FullName);
                        b.UseAdminDatabase(infolinkOptions.AdminDatabaseName);
                    });

                });
            }
            else
            {
                services.AddDbContext<InfolinkDbContext>(c =>
                {
                    c.EnableSensitiveDataLogging(true);


                    if (infolinkOptions.DatabaseType.ToLower() == RelationalDbType.MySql.ToString().ToLower())
                    {
                        c.UseMySql(Configuration.GetConnectionString(InfolinkDbContext.ConnectionString), b =>
                        {
                            b.ServerVersion(new ServerVersion(new Version(8, 0, 18), ServerType.MySql));
                            b.MigrationsAssembly(typeof(MySql.DbType).Assembly.FullName);
                        });
                    }
                    else if (infolinkOptions.DatabaseType.ToLower() == RelationalDbType.MsSql.ToString().ToLower())
                    {
                        c.UseSqlServer(Configuration.GetConnectionString(InfolinkDbContext.ConnectionString), b =>
                        {
                            b.MigrationsAssembly(typeof(MsSql.DbType).Assembly.FullName);
                        });
                    }

                });

            }


            services.AddHealthChecks();
            services.AddRazorPages(options =>
            {
                options.Conventions.AuthorizeFolder("/");
                options.Conventions.AllowAnonymousToPage("/Login");
            });
            
            services.AddServerSideBlazor().AddHubOptions(
                options => { options.MaximumReceiveMessageSize = 131072; });
            
            services.AddSimplyRazor(config =>
            {
                //config.BlobsUri = new Uri(Configuration["BlobsUrl"]);
                config.DefaultApiClientFactory = sp => sp.GetService<InfolinkClient>();
            });
            services.AddJwtTokenParameters();
            
            services.AddScoped<RunFlagUpdater>();

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
                })
                .AddCookie(options =>
                {
                    options.Cookie.Name = "InfolinkUser";
                    options.LoginPath = "/login";
                });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            app.UseForwardedHeaders();

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UsePathBase("/infolink");
            app.UseStaticFiles();
            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseHttpAsRequestContext();
            app.UseRequestContextLogEnricher();

            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/api/swagger.json", "Infolink Api");
            });


            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapHealthChecks("/health");
                endpoints.MapBlazorHub();
                endpoints.MapFallbackToPage("/_Host");
            });
        }
    }
}
