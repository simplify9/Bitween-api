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
using SW.Bitween.Services;
using SW.Bus;
using SW.CloudFiles.Extensions;
using SW.HttpExtensions;
using SW.CloudFiles.LocalTests;
using SW.PrimitiveTypes;
using SW.Scheduler;
using SW.Serverless;
using SW.Serverless.Resident;
using SW.Bitween.Services.DataSources;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Xunit;
using SW.Bitween.Services.Adapters;

namespace SW.Bitween.IntegrationTests.Fixtures;

/// <summary>
/// Collection-scoped fixture that starts a PostgreSQL container, a RabbitMQ container and a
/// MailHog container, applies EF migrations, installs serverless adapters to local cloud storage,
/// and builds a fully wired service provider.
/// </summary>
/// <remarks>
/// Every test in the collection shares this one database, so entities must not be given hand-picked
/// primary keys. Documents used to be created with literal ids chosen to be "high enough" to miss
/// the seeded rows, which worked only for as long as no two test files happened to pick the same
/// number — and when they eventually did, the pair passed in isolation and failed together, which
/// reads as a broken test rather than a collision. Let the database assign ids: for a document that
/// is <c>new Document(null, name, format)</c>, the constructor that exists for exactly this.
/// </remarks>
public sealed class BitweenFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder().Build();
    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder().Build();

    // A SECOND RabbitMQ, standing in for a customer's own broker. Reusing the internal one would
    // let a test pass while the external path quietly published to Bitween's own bus, which is
    // exactly the confusion the feature exists to avoid.
    private readonly RabbitMqContainer _externalRabbitMq = new RabbitMqBuilder().Build();

    // ElasticMQ speaks the SQS API without LocalStack's weight. The point is to exercise real
    // receive/delete/visibility semantics rather than a mock that agrees with our assumptions.
    private readonly IContainer _elasticMq = new ContainerBuilder()
        .WithImage("softwaremill/elasticmq-native:1.6.11")
        .WithPortBinding(ElasticMqContainerPort, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(ElasticMqContainerPort))
        .Build();

    private const int ElasticMqContainerPort = 9324;

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

    /// <summary>
    /// Connection details for the external broker, as a data source's properties.
    ///
    /// Read from the container's own connection string rather than assumed: RabbitMqBuilder
    /// generates random credentials, so hardcoding guest/guest gets ACCESS_REFUSED.
    /// </summary>
    public Dictionary<string, string> ExternalRabbitProperties => new()
    {
        ["Host"] = ExternalRabbitHost,
        ["Port"] = ExternalRabbitPort.ToString(),
        ["UserName"] = ExternalRabbitUser,
        ["Password"] = ExternalRabbitPassword,
        ["VirtualHost"] = "/",
        ["DeclareMode"] = "create",
        ["Prefetch"] = "8"
    };

    private Uri ExternalRabbitUri => new(_externalRabbitMq.GetConnectionString());

    public string ExternalRabbitHost => ExternalRabbitUri.Host;
    public int ExternalRabbitPort => ExternalRabbitUri.Port;
    public string ExternalRabbitUser => ExternalRabbitUri.UserInfo.Split(':')[0];
    public string ExternalRabbitPassword => ExternalRabbitUri.UserInfo.Split(':') is [_, var p] ? p : "";

    public string SqsServiceUrl => $"http://{_elasticMq.Hostname}:{_elasticMq.GetMappedPublicPort(ElasticMqContainerPort)}";

    public Dictionary<string, string> SqsProperties => new()
    {
        ["Region"] = "elasticmq",
        ["ServiceUrl"] = SqsServiceUrl,
        ["AccessKeyId"] = "x",
        ["SecretAccessKey"] = "x",
        ["WaitTimeSeconds"] = "1",
        ["VisibilityTimeoutSeconds"] = "10"
    };

    /// <summary>
    /// Creates a queue and returns a URL that actually resolves from the test host.
    ///
    /// ElasticMQ builds QueueUrl from its own node address, which is the port INSIDE the
    /// container, not the mapped one — so the URL it hands back is unreachable. Only the path is
    /// trustworthy; the authority has to come from the mapped endpoint.
    /// </summary>
    public async Task<string> CreateSqsQueueAsync(string name)
    {
        using var sqs = CreateSqsClient();
        var created = await sqs.CreateQueueAsync(name);

        var path = new Uri(created.QueueUrl).AbsolutePath;
        return SqsServiceUrl.TrimEnd('/') + path;
    }

    public Amazon.SQS.IAmazonSQS CreateSqsClient() =>
        new Amazon.SQS.AmazonSQSClient(
            new Amazon.Runtime.BasicAWSCredentials("x", "x"),
            new Amazon.SQS.AmazonSQSConfig
            {
                ServiceURL = SqsServiceUrl,
                AuthenticationRegion = "elasticmq"
            });

    public IHost App { get; private set; } = null!;

    private ExceptionDispatchInfo? _initError;

    public async Task InitializeAsync()
    {
        try
        {
            await Task.WhenAll(_postgres.StartAsync(), _rabbitMq.StartAsync(), _mailHog.StartAsync(),
                _externalRabbitMq.StartAsync(), _elasticMq.StartAsync());

            var dataSourceBuilder = new NpgsqlDataSourceBuilder(_postgres.GetConnectionString());
            dataSourceBuilder.EnableDynamicJson();
            var dataSource = dataSourceBuilder.Build();

            App = Host.CreateDefaultBuilder()
                .ConfigureAppConfiguration(cfg => cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:RabbitMQ"] = _rabbitMq.GetConnectionString(),
                    // Signing material for the tokens the login handler issues. Test-only values —
                    // the key just has to be long enough for the algorithm to accept it.
                    ["Token:Key"] = "integration-test-signing-key-not-used-anywhere-else-0123456789",
                    ["Token:Issuer"] = "bitween-tests",
                    ["Token:Audience"] = "bitween-tests",
                }))
                .ConfigureServices((ctx, services) =>
                {
                    services.AddSingleton(new BitweenOptions
                    {
                        QueuePrefix = "bitween-test",
                        StorageProvider = "LocalTests",
                        DatabaseType = "PgSql",
                        BusDefaultQueuePrefetch = 10,
                        JwtExpiryMinutes = 30,
                        // A passphrase has to exist or secret settings refuse to be stored at
                        // all, which would make the encryption path untestable.
                        SettingsEncryptionKey = "integration-test-settings-passphrase",
                    });

                    services.AddSingleton(new ThemeOptions());
                    services.AddSingleton<SettingsProtector>();
                    services.AddSingleton<SettingsService>();

                    // The login handler writes its refresh token to a response cookie, so it needs
                    // an HttpContext to exist. Nothing else in these tests goes through HTTP.
                    services.AddHttpContextAccessor();
                    services.AddJwtTokenParameters();

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
                    // Its OWN bucket. The default one is shared with whatever else uses the local
                    // store on this machine — including a developer's running Bitween — and the
                    // teardown below calls Cleanup(), which deletes the bucket outright. Sharing it
                    // meant running this suite silently unpublished the dev environment's adapters,
                    // and the next thing anyone did there failed as "metadata is missing
                    // 'EntryAssembly'", which points nowhere near a test run.
                    services.AddLocalTestsCloudFiles(o => o.BucketName = "bitween-integration-tests");

                    // Real serverless service pointing to local adapter extraction path
                    services.AddServerless(opts =>
                    {
                        opts.AdapterRemotePath = "adapters";
                        opts.AdapterLocalPath = Path.Combine(Path.GetTempPath(), "bitween-test-serverless");
                    });

                    // The external bus provider runtime. The supervisor is deliberately NOT
                    // registered as a hosted service here: tests start data sources explicitly so
                    // they control timing, and BusProviderSupervisorTests drives it directly.
                    services.AddResidentAdapters<BusProviderEventSink>(o =>
                    {
                        o.SocketPath = $"/tmp/bitween-tests-{Environment.ProcessId}.sock";
                        o.PipeName = $"bitween-tests-{Environment.ProcessId}";
                        o.HeartbeatInterval = TimeSpan.FromSeconds(2);
                        o.HandshakeTimeout = TimeSpan.FromSeconds(60);
                        o.MaxInFlight = 8;
                    });

                    services.AddSingleton<IInfolinkCache, InMemoryBitweenCache>();
                    services.AddSingleton<INativeInfolinkReceiver, NativeTestReceiver>();
                    services.AddSingleton<INativeInfolinkReceiver, NativeFailingTestReceiver>();
                    services.AddSingleton<INativeInfolinkReceiver, NativeEmptyTestReceiver>();
                    services.AddScoped<INativeInfolinkHandler, NativeSmtpHandler>();
                    services.AddScoped<INativeAdapter, NativeSmtpHandler>();

                    // The mapper step had no end-to-end coverage at all — every mapper test was a
                    // unit test against ScribanJsonHelper, so what XchangeService does to a payload
                    // on the way in was never exercised. See MapperEnrichmentBaselineTests.
                    services.AddScoped<INativeInfolinkMapper, NativeJSONMapper>();
                    services.AddScoped<INativeAdapter, NativeJSONMapper>();

                    services.AddScoped<INativeInfolinkMapper, NativeAdapters.Mapper.NativeMapper>();
                    services.AddScoped<INativeAdapter, NativeAdapters.Mapper.NativeMapper>();

                    // See RecordingScheduleRepository: the create/update handlers need a scheduler
                    // to construct, and a real Quartz store would fire background jobs mid-test.
                    services.AddSingleton<IScheduleRepository, RecordingScheduleRepository>();
                    services.AddScoped<SubscriptionSchedulerService>();

                    services.AddSingleton<DataSourceProviderCatalog>();
                    services.AddScoped<StatementUsageReader>();
                    services.AddScoped<StatementValidator>();
                    services.AddSingleton<FilterService>();
                    services.AddScoped<NativeAdapterDiscoveryService>();
                    services.AddSingleton<ServerlessAdapterDescriber>();
                    services.AddScoped<AdapterStartupValues>();
                    services.AddScoped<Resources.Adapters.AdapterListing>();
                    services.AddScoped<AdapterRequirements>();
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

                // Protocol 2 metadata is what tells the host these are resident adapters rather
                // than the classic per-invocation kind.
                await AdapterInstaller.InstallAsync(cloudFiles,
                    "SW.Bitween.Adapters.Bus.RabbitMq", BusAdapters.RabbitMq,
                    "SW.Bitween.Adapters.Bus.RabbitMq.dll",
                    new Dictionary<string, string> { ["Protocol"] = "2", ["Lifecycle"] = "resident" });
                await AdapterInstaller.InstallAsync(cloudFiles,
                    "SW.Bitween.Adapters.Bus.Sqs", BusAdapters.Sqs,
                    "SW.Bitween.Adapters.Bus.Sqs.dll",
                    new Dictionary<string, string> { ["Protocol"] = "2", ["Lifecycle"] = "resident" });
                await AdapterInstaller.InstallAsync(cloudFiles,
                    "SW.Bitween.Adapters.Db.Oracle", BusAdapters.Oracle,
                    "SW.Bitween.Adapters.Db.Oracle.dll",
                    new Dictionary<string, string> { ["Protocol"] = "2", ["Lifecycle"] = "resident" });
                await AdapterInstaller.InstallAsync(cloudFiles,
                    "SW.Bitween.Adapters.Db.PostgreSql", BusAdapters.PostgreSql,
                    "SW.Bitween.Adapters.Db.PostgreSql.dll",
                    new Dictionary<string, string> { ["Protocol"] = "2", ["Lifecycle"] = "resident" });
                await AdapterInstaller.InstallAsync(cloudFiles,
                    "SW.Bitween.Adapters.Db.MySql", BusAdapters.MySql,
                    "SW.Bitween.Adapters.Db.MySql.dll",
                    new Dictionary<string, string> { ["Protocol"] = "2", ["Lifecycle"] = "resident" });
                await AdapterInstaller.InstallAsync(cloudFiles,
                    "SW.Bitween.Adapters.Db.SqlServer", BusAdapters.SqlServer,
                    "SW.Bitween.Adapters.Db.SqlServer.dll",
                    new Dictionary<string, string> { ["Protocol"] = "2", ["Lifecycle"] = "resident" });
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
        await _externalRabbitMq.DisposeAsync();
        await _elasticMq.DisposeAsync();
    }
}

[CollectionDefinition("Bitween")]
public class BitweenCollection : ICollectionFixture<BitweenFixture>;
