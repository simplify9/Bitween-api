using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using NSubstitute;
using SW.Bitween.Domain;
using SW.Bitween.IntegrationTests.Adapters;
using SW.Bitween.NativeAdapters;
using SW.Bitween.PgSql;
using SW.Bus;
using SW.CloudFiles;
using SW.PrimitiveTypes;
using SW.Serverless;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Xunit;

namespace SW.Bitween.IntegrationTests.Fixtures;

/// <summary>
/// Collection-scoped fixture that starts a PostgreSQL container and a RabbitMQ container,
/// applies EF migrations, and builds a fully wired service provider.
///
/// Cloud files and serverless are stubbed with NSubstitute — the user has confirmed
/// these are out of scope for this test suite.
/// </summary>
public sealed class BitweenFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder().Build();
    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder().Build();

    // Exposed stubs so individual tests can configure return values as needed.
    public ICloudFilesService CloudFiles { get; } = Substitute.For<ICloudFilesService>();
    public IServerlessService Serverless { get; } = Substitute.For<IServerlessService>();

    public IHost App { get; private set; } = null!;

    // Captured instead of re-thrown so that startup failures surface as proper red
    // test failures in Rider rather than the silent "Inconclusive" that xUnit produces
    // when InitializeAsync throws and marks the entire collection as skipped.
    private ExceptionDispatchInfo? _initError;

    public async Task InitializeAsync()
    {
        try
        {
            await Task.WhenAll(_postgres.StartAsync(), _rabbitMq.StartAsync());

            var dataSourceBuilder = new NpgsqlDataSourceBuilder(_postgres.GetConnectionString());
            dataSourceBuilder.EnableDynamicJson();
            var dataSource = dataSourceBuilder.Build();

            App = Host.CreateDefaultBuilder()
                .ConfigureAppConfiguration(cfg => cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    // Bus reads its AMQP connection string from ConnectionStrings:RabbitMQ
                    ["ConnectionStrings:RabbitMQ"] = _rabbitMq.GetConnectionString(),
                }))
                .ConfigureServices((ctx, services) =>
                {
                    services.AddSingleton(new BitweenOptions
                    {
                        QueuePrefix = "bitween-test",
                        StorageProvider = "S3",
                        DatabaseType = "PgSql",
                        BusDefaultQueuePrefetch = 10,
                    });

                    services.AddMemoryCache();
                    services.AddScoped<RequestContext>();

                    // Real PostgreSQL-backed DbContext using the EF migrations from SW.Bitween.PgSql
                    services.AddDbContext<BitweenDbContext, PgSql.BitweenDbContext>(c =>
                        c.UseSnakeCaseNamingConvention()
                         .UseNpgsql(dataSource, b =>
                         {
                             b.MigrationsHistoryTable("_ef_migrations_history", PgSql.BitweenDbContext.Schema);
                             b.MigrationsAssembly(typeof(PgSql.DbType).Assembly.FullName);
                         }));

                    // Real RabbitMQ bus — connection string injected via IConfiguration above
                    services.AddBus(cfg =>
                    {
                        cfg.ApplicationName = "bitween-test";
                        cfg.DefaultQueuePrefetch = 10;
                    });
                    services.AddBusPublish();

                    // Stubs for out-of-scope dependencies
                    services.AddSingleton(CloudFiles);
                    services.AddSingleton(Serverless);

                    // Real in-process cache backed by the PostgreSQL DbContext
                    services.AddSingleton<IInfolinkCache, InMemoryBitweenCache>();

                    // Native adapter for receiving tests
                    services.AddSingleton<INativeInfolinkReceiver, NativeTestReceiver>();

                    // Core application services
                    services.AddSingleton<FilterService>();
                    services.AddScoped<NativeAdapterDiscoveryService>();
                    services.AddScoped<XchangeService>();
                    services.AddScoped<RunFlagUpdater>();
                    services.AddScoped<ReceivingJob>();
                    services.AddScoped<AggregationJob>();
                })
                .Build();

            // Apply all EF migrations against the fresh PostgreSQL container
            await using (var scope = App.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
                await db.Database.MigrateAsync();
            }

            await App.StartAsync();
        }
        catch (Exception ex)
        {
            // Store rather than re-throw. xUnit treats InitializeAsync exceptions as
            // "collection skipped", which Rider renders as Inconclusive with no message.
            // Storing the error and re-throwing from CreateScope() makes every test in
            // the collection fail with the real exception — visible and actionable.
            _initError = ExceptionDispatchInfo.Capture(ex);
        }
    }

    /// <summary>Creates a new DI scope. Caller is responsible for disposal.</summary>
    public AsyncServiceScope CreateScope()
    {
        // Re-throw any startup failure with its original stack trace so the test
        // fails with a real error message instead of showing as Inconclusive.
        _initError?.Throw();
        return App.Services.CreateAsyncScope();
    }

    public async Task DisposeAsync()
    {
        if (App is not null)
            await App.StopAsync();
        await _postgres.DisposeAsync();
        await _rabbitMq.DisposeAsync();
    }
}

[CollectionDefinition("Bitween")]
public class BitweenCollection : ICollectionFixture<BitweenFixture>;
