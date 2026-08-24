using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using SW.Bitween.Domain;
using SW.Bitween.IntegrationTests.Adapters;
using SW.Bitween.NativeAdapters;
using SW.Bitween.NativeAdapters.SmtpHandler;
using SW.Bitween.PgSql;
using SW.Bus;
using SW.CloudFiles.Extensions;
using SW.CloudFiles.LocalTests;
using SW.PrimitiveTypes;
using SW.Serverless;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Xunit;

namespace SW.Bitween.IntegrationTests.Fixtures;

/// <summary>
/// Collection-scoped fixture that starts a PostgreSQL container, a RabbitMQ container and a
/// MailHog container, applies EF migrations, installs serverless adapters to local cloud storage,
/// and builds a fully wired service provider.
/// </summary>
public sealed class BitweenFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder().Build();
    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder().Build();

    // A real SMTP server, because the one thing no unit test can prove about the alert feature is
    // that an actual handshake succeeds. Started here rather than expected on the developer's
    // machine: a test that quietly does nothing when a local service is missing reports a green run
    // while the whole delivery path goes unexercised.
    private readonly IContainer _mailHog = new ContainerBuilder()
        .WithImage("mailhog/mailhog:v1.0.1")
        .WithPortBinding(SmtpContainerPort, true)
        .WithPortBinding(ApiContainerPort, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(ApiContainerPort))
        .Build();

    private const int SmtpContainerPort = 1025;
    private const int ApiContainerPort = 8025;

    /// <summary>Host port the MailHog SMTP listener is mapped to, for a handler's Port setting.</summary>
    public int MailHogSmtpPort => _mailHog.GetMappedPublicPort(SmtpContainerPort);

    /// <summary>Base address of MailHog's own API, for reading back what was delivered.</summary>
    public string MailHogApi => $"http://{_mailHog.Hostname}:{_mailHog.GetMappedPublicPort(ApiContainerPort)}";

    public IHost App { get; private set; } = null!;

    private ExceptionDispatchInfo? _initError;

    public async Task InitializeAsync()
    {
        try
        {
            await Task.WhenAll(_postgres.StartAsync(), _rabbitMq.StartAsync(), _mailHog.StartAsync());

            var dataSourceBuilder = new NpgsqlDataSourceBuilder(_postgres.GetConnectionString());
            dataSourceBuilder.EnableDynamicJson();
            var dataSource = dataSourceBuilder.Build();

            App = Host.CreateDefaultBuilder()
                .ConfigureAppConfiguration(cfg => cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:RabbitMQ"] = _rabbitMq.GetConnectionString(),
                }))
                .ConfigureServices((ctx, services) =>
                {
                    services.AddSingleton(new BitweenOptions
                    {
                        QueuePrefix = "bitween-test",
                        StorageProvider = "LocalTests",
                        DatabaseType = "PgSql",
                        BusDefaultQueuePrefetch = 10,
                    });

                    services.AddMemoryCache();
                    services.AddScoped<RequestContext>();

                    services.AddDbContext<BitweenDbContext, PgSql.BitweenDbContext>(c =>
                        c.UseSnakeCaseNamingConvention()
                         .UseNpgsql(dataSource, b =>
                         {
                             b.MigrationsHistoryTable("_ef_migrations_history", PgSql.BitweenDbContext.Schema);
                             b.MigrationsAssembly(typeof(PgSql.DbType).Assembly.FullName);
                         }));

                    services.AddBus(cfg =>
                    {
                        cfg.ApplicationName = "bitween-test";
                        cfg.DefaultQueuePrefetch = 10;
                    });
                    services.AddBusPublish();

                    // Real local filesystem cloud files provider
                    services.AddLocalTestsCloudFiles();

                    // Real serverless service pointing to local adapter extraction path
                    services.AddServerless(opts =>
                    {
                        opts.AdapterRemotePath = "adapters";
                        opts.AdapterLocalPath = Path.Combine(Path.GetTempPath(), "bitween-test-serverless");
                    });

                    services.AddSingleton<IInfolinkCache, InMemoryBitweenCache>();
                    services.AddSingleton<INativeInfolinkReceiver, NativeTestReceiver>();
                    services.AddSingleton<INativeInfolinkReceiver, NativeFailingTestReceiver>();
                    services.AddSingleton<INativeInfolinkReceiver, NativeEmptyTestReceiver>();
                    services.AddScoped<INativeInfolinkHandler, NativeSmtpHandler>();
                    services.AddScoped<INativeAdapter, NativeSmtpHandler>();

                    services.AddSingleton<FilterService>();
                    services.AddScoped<NativeAdapterDiscoveryService>();
                    services.AddScoped<AdapterSecretProperties>();
                    services.AddScoped<RetryUsageReport>();
                    services.AddScoped<AdapterInvoker>();
                    services.AddScoped<XchangeService>();
                    services.AddScoped<RunFlagUpdater>();
                    services.AddScoped<ReceivingJob>();
                    services.AddScoped<AggregationJob>();
                    services.AddScoped<RetryJob>();
                    services.AddScoped<RetryAlertService>();
                })
                .Build();

            await using (var scope = App.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
                await db.Database.MigrateAsync();
            }

            // Install serverless adapters into local cloud storage
            await using (var scope = App.Services.CreateAsyncScope())
            {
                var cloudFiles = scope.ServiceProvider.GetRequiredService<ICloudFilesService>();
                await AdapterInstaller.InstallAsync(cloudFiles,
                    "SW.Bitween.SampleHandler", "sw.bitween.samplehandler", "SW.Bitween.SampleHandler.dll");
                await AdapterInstaller.InstallAsync(cloudFiles,
                    "SW.Bitween.SampleConfigurableAdapter", "sw.bitween.sampleconfigurableadapter", "SW.Bitween.SampleConfigurableAdapter.dll");
            }

            await App.StartAsync();
        }
        catch (Exception ex)
        {
            _initError = ExceptionDispatchInfo.Capture(ex);
        }
    }

    /// <summary>Creates a new DI scope. Caller is responsible for disposal.</summary>
    public AsyncServiceScope CreateScope()
    {
        _initError?.Throw();
        return App.Services.CreateAsyncScope();
    }

    public async Task DisposeAsync()
    {
        if (App is not null)
        {
            App.Services.GetRequiredService<CloudFilesService>().Cleanup();
            await App.StopAsync();
        }
        await _postgres.DisposeAsync();
        await _rabbitMq.DisposeAsync();
        await _mailHog.DisposeAsync();
    }
}

[CollectionDefinition("Bitween")]
public class BitweenCollection : ICollectionFixture<BitweenFixture>;
