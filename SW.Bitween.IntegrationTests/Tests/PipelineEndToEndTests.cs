using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using RabbitMQ.Client;
using SW.Bitween.Domain;
using SW.Bitween.Domain.DataSources;
using SW.Bitween.Domain.Gateway;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.Bitween.Services;
using SW.PrimitiveTypes;
using SW.Serverless.Resident;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// The whole pipeline, from where a message arrives to the XchangeResult it produces - with
/// subscriptions that actually run serverless adapters rather than stopping at the Xchange row.
///
/// The claim under test is the one the external-broker design rests on: past the point of
/// persistence, ingress from an external broker and ingress from the internal bus are the SAME
/// thing. Filtering, routing, mapping, handling and the audit trail are not reimplemented for
/// external sources, and these tests assert that by running both through identical subscriptions
/// and comparing the outcome.
///
/// It also puts both adapter lifecycles in one flow: a RESIDENT adapter owns the broker
/// connection and pushes, while CLASSIC per-invocation adapters do the mapping and handling. They
/// share nothing but the SDK, and both have to work for a single message to get through.
/// </summary>
[Collection("Bitween")]
public class PipelineEndToEndTests(BitweenFixture fixture)
{
    private const string EchoHandler = "sw.bitween.samplehandler";
    private const string ConfigurableAdapter = "sw.bitween.sampleconfigurableadapter";

    private const string MappedOutput = "{\"mapped\":true,\"by\":\"configurable-adapter\"}";

    // ---------------------------------------------------------------- external

    /// <summary>
    /// A customer's own broker, all the way to a finished XchangeResult: resident adapter ->
    /// sink -> Xchange -> filter -> route -> subscription -> classic serverless handler -> result.
    /// </summary>
    [Fact]
    public async Task An_external_broker_message_runs_a_subscription_and_produces_a_result()
    {
        var queue = Unique("e2e-ext");

        // Both stages: the configurable adapter maps to a known output, the echo handler takes
        // that and returns it. A subscription that runs only one stage proves half the pipeline.
        var setup = await ArrangeAsync(queue, EchoHandler,
            mapperId: ConfigurableAdapter,
            mapperProperties: new Dictionary<string, string> { ["OutputData"] = MappedOutput });

        await using var adapter = await StartAdapterAsync(setup.DataSourceId);

        Publish(queue, "{\"orderId\":501,\"channel\":\"web\"}");

        var parent = await WaitForXchangeAsync(setup.DocumentId);
        Assert.NotNull(parent);

        var result = await DriveToResultAsync(parent!, setup.SubscriptionId);

        Assert.NotNull(result);
        Assert.True(result!.Success, $"the pipeline failed: {result.Exception}");

        // The MAPPER's product is the output; the handler's return is the response. Asserting the
        // exact size pins that the mapper actually ran rather than the payload passing through.
        Assert.Equal(Encoding.UTF8.GetByteCount(MappedOutput), result.OutputSize);
        Assert.True(result.ResponseSize > 0, "the handler should have returned a response");
    }

    /// <summary>
    /// The same subscription, fed from the INTERNAL bus. The design says these two paths converge
    /// at SubmitFilterXchange; if either the result or the route taken differs, that claim is
    /// wrong and every argument built on it needs revisiting.
    /// </summary>
    [Fact]
    public async Task The_internal_bus_produces_the_same_outcome_as_an_external_broker()
    {
        var payload = "{\"orderId\":502,\"channel\":\"web\"}";

        // External.
        var queue = Unique("e2e-parity");
        var external = await ArrangeAsync(queue, EchoHandler);
        await using (var adapter = await StartAdapterAsync(external.DataSourceId))
        {
            Publish(queue, payload);
            var externalParent = await WaitForXchangeAsync(external.DocumentId);
            Assert.NotNull(externalParent);
            ExternalResult = await DriveToResultAsync(externalParent!, external.SubscriptionId);
        }

        // Internal: no data source, no adapter, no broker - the gateway alone.
        var internalSetup = await ArrangeAsync(endpoint: null, EchoHandler);
        await SubmitInternallyAsync(internalSetup.DocumentId, payload);

        var internalParent = await WaitForXchangeAsync(internalSetup.DocumentId);
        Assert.NotNull(internalParent);
        var internalResult = await DriveToResultAsync(internalParent!, internalSetup.SubscriptionId);

        Assert.NotNull(ExternalResult);
        Assert.NotNull(internalResult);
        Assert.True(ExternalResult!.Success);
        Assert.True(internalResult!.Success);
        Assert.Equal(ExternalResult.OutputSize, internalResult.OutputSize);
        Assert.Equal(ExternalResult.OutputHash, internalResult.OutputHash);
    }

    private XchangeResult? ExternalResult { get; set; }

    // ---------------------------------------------------------------- routing

    /// <summary>
    /// One gateway, two routes, different filters. The message must run the subscription whose
    /// filter matches and only that one - routing is the gateway's job whatever fed it.
    /// </summary>
    [Fact]
    public async Task A_filter_on_a_route_selects_which_subscription_runs()
    {
        var queue = Unique("e2e-route");
        var setup = await ArrangeAsync(queue, EchoHandler,
            matchExpression: new OneOfSpec("channel", ["pos"]));

        // A second subscription on the same gateway, wanting a different channel.
        var otherSubscriptionId = await AddRouteAsync(setup.GatewayId, setup.DocumentId, EchoHandler,
            new OneOfSpec("channel", ["web"]));

        await using var adapter = await StartAdapterAsync(setup.DataSourceId);

        Publish(queue, "{\"orderId\":503,\"channel\":\"pos\"}");

        var parent = await WaitForXchangeAsync(setup.DocumentId);
        Assert.NotNull(parent);

        await ProcessAsync(parent!.Id);

        var children = await ChildXchangesAsync(setup.DocumentId);

        Assert.Single(children);
        Assert.Equal(setup.SubscriptionId, children[0].SubscriptionId);
        Assert.DoesNotContain(children, c => c.SubscriptionId == otherSubscriptionId);
    }

    // ---------------------------------------------------------------- lifecycles together

    /// <summary>
    /// Both adapter lifecycles in one message's journey. The resident adapter has been running
    /// since before the message existed and owns a broker connection; the mapper and handler are
    /// spawned per invocation and die with the scope. Nothing coordinates them beyond the SDK.
    /// </summary>
    [Fact]
    public async Task A_resident_adapter_and_classic_adapters_serve_one_message_together()
    {
        var queue = Unique("e2e-both");

        // The configurable adapter lets the handler's output be asserted rather than inferred.
        var setup = await ArrangeAsync(queue, ConfigurableAdapter,
            handlerProperties: new Dictionary<string, string> { ["OutputData"] = "handled-by-classic" });

        await using var adapter = await StartAdapterAsync(setup.DataSourceId);

        // The resident adapter is already attached and consuming before the message exists.
        var health = fixture.App.Services.GetRequiredService<IResidentAdapterHost>()
            .Describe().Single(h => h.InstanceKey == setup.DataSourceId.ToString());
        Assert.Equal(InstanceState.Ready, health.State);

        Publish(queue, "{\"orderId\":504,\"channel\":\"web\"}");

        var parent = await WaitForXchangeAsync(setup.DocumentId);
        Assert.NotNull(parent);

        var result = await DriveToResultAsync(parent!, setup.SubscriptionId);

        Assert.NotNull(result);
        Assert.True(result!.Success, $"the classic handler failed: {result.Exception}");

        // The resident adapter is still running, having outlived the processes that did the work.
        var after = fixture.App.Services.GetRequiredService<IResidentAdapterHost>()
            .Describe().Single(h => h.InstanceKey == setup.DataSourceId.ToString());
        Assert.Equal(InstanceState.Ready, after.State);
        Assert.Equal(0, after.RestartCount);
    }

    /// <summary>
    /// A handler that fails must NOT unwind the ingest. The message was persisted and acknowledged
    /// long before the handler ran, so the failure belongs to the XchangeResult and the retry
    /// policy - not to the broker, which has already been told the message was taken.
    /// </summary>
    [Fact]
    public async Task A_failing_handler_is_recorded_as_a_result_not_as_an_ingest_failure()
    {
        var queue = Unique("e2e-fail");
        var setup = await ArrangeAsync(queue, ConfigurableAdapter,
            handlerProperties: new Dictionary<string, string>
            {
                ["SimulateError"] = "true",
                ["ErrorMessage"] = "handler refused the message"
            });

        await using var adapter = await StartAdapterAsync(setup.DataSourceId);

        Publish(queue, "{\"orderId\":505,\"channel\":\"web\"}");

        var parent = await WaitForXchangeAsync(setup.DocumentId);
        Assert.NotNull(parent);

        var result = await DriveToResultAsync(parent!, setup.SubscriptionId);

        Assert.NotNull(result);
        Assert.False(result!.Success, "the handler was told to simulate an error");
        Assert.Contains("handler refused the message", result.Exception);

        // Ingest still succeeded: the adapter acked, and the queue drained.
        await WaitAsync(() => Depth(queue) == 0, TimeSpan.FromSeconds(20),
            "a handler failure must not leave the message stuck on the broker");
    }

    // ---------------------------------------------------------------- helpers

    private record Setup(int DocumentId, int SubscriptionId, int GatewayId, int DataSourceId);

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..24];

    /// <summary>
    /// Document, subscription with adapters, gateway, and one route. When <paramref name="endpoint"/>
    /// is null the gateway is an INTERNAL one - no data source, which is what null has always meant.
    /// </summary>
    private async Task<Setup> ArrangeAsync(string? endpoint, string handlerId,
        IDictionary<string, string>? handlerProperties = null,
        IPropertyMatchSpecification? matchExpression = null,
        string? mapperId = null,
        IDictionary<string, string>? mapperProperties = null)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        var document = new Document(null, Unique("e2e-doc"), DocumentFormat.Json);
        db.Add(document);
        await db.SaveChangesAsync();

        // Every Subscription constructor sets Inactive = true, so a freshly created one is off
        // and the filter will never match it. GatewayRoutingTests turns it on for the same reason.
        var subscription = new Subscription(Unique("e2e-sub"), document.Id, SubscriptionType.BusGateway)
        {
            HandlerId = handlerId,
            MapperId = mapperId,
            Inactive = false
        };
        subscription.SetDictionaries(
            (IReadOnlyDictionary<string, string>)(handlerProperties ?? new Dictionary<string, string>()),
            (IReadOnlyDictionary<string, string>)(mapperProperties ?? new Dictionary<string, string>()),
            new Dictionary<string, string>(), new Dictionary<string, string>(),
            new Dictionary<string, string>());

        db.Add(subscription);
        await db.SaveChangesAsync();

        var dataSourceId = 0;
        var gateway = new BusGateway { Name = Unique("e2e-gw"), DocumentId = document.Id };

        if (endpoint != null)
        {
            var dataSource = new DataSource
            {
                Name = Unique("e2e-ds"),
                AdapterId = BusAdapters.RabbitMq,
                Kind = DataSourceKind.Broker,
                Properties = new Dictionary<string, string>(fixture.ExternalRabbitProperties)
                {
                    ["Endpoints"] = endpoint
                }
            };
            db.Add(dataSource);
            await db.SaveChangesAsync();

            dataSourceId = dataSource.Id;
            gateway.DataSourceId = dataSource.Id;
            gateway.Endpoint = endpoint;
        }

        db.Add(gateway);
        await db.SaveChangesAsync();

        db.Add(new BusGatewayRoute
        {
            BusGatewayId = gateway.Id,
            SubscriptionId = subscription.Id,
            MatchExpression = matchExpression
        });
        await db.SaveChangesAsync();

        // Ten-minute singleton snapshot: without this the pipeline reads configuration from
        // before this test's own setup - see GatewayRoutingTests.
        scope.ServiceProvider.GetRequiredService<IInfolinkCache>().Revoke();

        return new Setup(document.Id, subscription.Id, gateway.Id, dataSourceId);
    }

    private async Task<int> AddRouteAsync(int gatewayId, int documentId, string handlerId,
        IPropertyMatchSpecification matchExpression)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        var subscription = new Subscription(Unique("e2e-sub2"), documentId, SubscriptionType.BusGateway)
        {
            HandlerId = handlerId,
            Inactive = false
        };
        db.Add(subscription);
        await db.SaveChangesAsync();

        db.Add(new BusGatewayRoute
        {
            BusGatewayId = gatewayId,
            SubscriptionId = subscription.Id,
            MatchExpression = matchExpression
        });
        await db.SaveChangesAsync();

        scope.ServiceProvider.GetRequiredService<IInfolinkCache>().Revoke();
        return subscription.Id;
    }

    private Task SubmitInternallyAsync(int documentId, string payload)
    {
        return Run(async scope =>
        {
            var xchangeService = scope.ServiceProvider.GetRequiredService<XchangeService>();
            await xchangeService.SubmitFilterXchange(documentId, new XchangeFile(payload));
        });
    }

    /// <summary>
    /// Drives the parent Xchange through filtering and then runs the child the route produced,
    /// which is what the internal bus consumer does in production.
    /// </summary>
    private async Task<XchangeResult?> DriveToResultAsync(Xchange parent, int subscriptionId)
    {
        await ProcessAsync(parent.Id);

        var children = await ChildXchangesAsync(parent.DocumentId);
        var child = children.FirstOrDefault(c => c.SubscriptionId == subscriptionId);
        if (child == null) return null;

        await ProcessAsync(child.Id);

        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        return await db.Set<XchangeResult>().AsNoTracking().FirstOrDefaultAsync(r => r.Id == child.Id);
    }

    private Task ProcessAsync(string xchangeId) => Run(scope =>
        scope.ServiceProvider.GetRequiredService<XchangeService>()
            .Process("XchangeCreated", JsonConvert.SerializeObject(new { Id = xchangeId })));

    /// <summary>
    /// A gateway hit produces an Xchange carrying a SubscriptionId; there is no explicit parent
    /// link on the entity, so these are found by Document - unique per test, so unambiguous.
    /// </summary>
    private async Task<List<Xchange>> ChildXchangesAsync(int documentId)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        return await db.Set<Xchange>().AsNoTracking()
            .Where(x => x.DocumentId == documentId && x.SubscriptionId != null)
            .ToListAsync();
    }

    private async Task Run(Func<IServiceScope, Task> work)
    {
        await using var scope = fixture.CreateScope();
        await work(scope);
    }

    private async Task<Xchange?> WaitForXchangeAsync(int documentId)
    {
        Xchange? found = null;
        await WaitAsync(async () =>
        {
            await using var scope = fixture.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            found = await db.Set<Xchange>().AsNoTracking()
                .Where(x => x.DocumentId == documentId && x.SubscriptionId == null)
                .FirstOrDefaultAsync();
            return found != null;
        }, TimeSpan.FromSeconds(45), $"no Xchange was created for document {documentId}");

        return found;
    }

    private async Task<AdapterLease> StartAdapterAsync(int dataSourceId)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var dataSource = await db.Set<DataSource>().AsNoTracking().FirstAsync(d => d.Id == dataSourceId);

        var host = fixture.App.Services.GetRequiredService<IResidentAdapterHost>();
        await host.StartExclusiveAsync(new AdapterSpec
        {
            AdapterId = dataSource.AdapterId,
            InstanceKey = dataSourceId.ToString(),
            StartupValues = new Dictionary<string, string>(dataSource.Properties)
        });

        return new AdapterLease(host, dataSource.AdapterId, dataSourceId.ToString());
    }

    private sealed class AdapterLease(IResidentAdapterHost host, string adapterId, string instanceKey)
        : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => new(host.StopAsync(adapterId, instanceKey, drain: false));
    }

    private void Publish(string queue, string body)
    {
        using var connection = ExternalConnection();
        using var channel = connection.CreateModel();
        channel.QueueDeclare(queue, durable: true, exclusive: false, autoDelete: false);

        var properties = channel.CreateBasicProperties();
        properties.ContentType = "application/json";
        properties.MessageId = Guid.NewGuid().ToString("N");
        properties.DeliveryMode = 2;

        channel.BasicPublish("", queue, properties, Encoding.UTF8.GetBytes(body));
    }

    private uint Depth(string queue)
    {
        using var connection = ExternalConnection();
        using var channel = connection.CreateModel();
        return channel.QueueDeclare(queue, durable: true, exclusive: false, autoDelete: false).MessageCount;
    }

    private IConnection ExternalConnection() => new ConnectionFactory
    {
        HostName = fixture.ExternalRabbitHost,
        Port = fixture.ExternalRabbitPort,
        UserName = fixture.ExternalRabbitUser,
        Password = fixture.ExternalRabbitPassword
    }.CreateConnection("pipeline-tests");

    private static async Task WaitAsync(Func<bool> condition, TimeSpan timeout, string because) =>
        await WaitAsync(() => Task.FromResult(condition()), timeout, because);

    private static async Task WaitAsync(Func<Task<bool>> condition, TimeSpan timeout, string because)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try { if (await condition()) return; } catch { /* still settling */ }
            await Task.Delay(300);
        }
        Assert.Fail($"Timed out after {timeout}: {because}");
    }
}
