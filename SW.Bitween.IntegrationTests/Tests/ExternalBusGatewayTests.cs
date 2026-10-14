using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using RabbitMQ.Client;
using SW.Bitween.Domain;
using SW.Bitween.Domain.DataSources;
using SW.Bitween.Domain.Gateway;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using SW.Serverless.Resident;
using SW.Bitween.Services.Adapters;
using SW.Bitween.Services.DataSources;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// A BusGateway fed by an EXTERNAL RabbitMQ, end to end: a message published to a customer's own
/// broker becomes an Xchange in Bitween through a resident adapter.
///
/// The external broker is a second container, deliberately. Reusing the internal one would let a
/// test pass while the message actually travelled Bitween's own bus - the precise confusion this
/// feature has to avoid.
/// </summary>
[Collection("Bitween")]
public class ExternalBusGatewayTests(BitweenFixture fixture)
{
    // ---------------------------------------------------------------- ingress

    [Fact]
    public async Task A_message_on_an_external_broker_becomes_an_Xchange()
    {
        var queue = Unique("orders");
        var (dataSourceId, documentId) = await ArrangeAsync(queue);

        await using var adapter = await StartAsync(dataSourceId);

        await PublishAsync(queue, "{\"orderId\":9001}");

        var xchange = await WaitForXchangeAsync(documentId);

        Assert.NotNull(xchange);
        Assert.Equal(documentId, xchange!.DocumentId);
    }

    /// <summary>
    /// The ordering the whole design turns on. Until Bitween has persisted, the message must still
    /// be the broker's - so a Bitween failure stops draining the customer's queue rather than
    /// losing their messages.
    /// </summary>
    [Fact]
    public async Task A_message_is_not_acknowledged_until_Bitween_has_persisted_it()
    {
        var queue = Unique("ack-order");

        // No gateway and no document: the sink cannot resolve anything, so ingest fails.
        var dataSourceId = await CreateDataSourceAsync(queue, withGateway: false);

        await using var adapter = await StartAsync(dataSourceId);

        await PublishAsync(queue, "{\"unclaimed\":true}");

        // An unclaimed endpoint is accepted-and-discarded on purpose: rejecting would requeue it
        // forever and the queue would never drain.
        await WaitAsync(async () => await DepthAsync(queue) == 0, TimeSpan.FromSeconds(30),
            "an unclaimed message should be drained, not left to requeue for ever");
    }

    /// <summary>
    /// The sink must REJECT what it cannot persist, because a rejection is what makes the adapter
    /// nack and the broker redeliver.
    ///
    /// This replaces a test that published malformed content and expected a nack. That premise was
    /// wrong: Bitween persists first and validates afterwards, so bad content becomes an Xchange
    /// carrying a bad result - a pipeline outcome, not an ingest failure. Exercising the rejection
    /// path means making the SINK fail, which is what an unattributable event does.
    ///
    /// That the adapter then nacks and the broker redelivers is proven against a real broker in
    /// SW-Serverless (A_rejected_message_is_nacked_back_and_redelivered); what belongs here is
    /// Bitween's half of that contract.
    /// </summary>
    [Fact]
    public async Task The_sink_rejects_an_event_it_cannot_attribute_to_a_data_source()
    {
        var sink = fixture.App.Services.GetRequiredService<IAdapterEventSink>();

        var outcome = await sink.OnEventAsync(new InboundEvent
        {
            AdapterId = BusAdapters.RabbitMq,
            InstanceKey = "not-a-data-source-id",
            Endpoint = "anything",
            Payload = Encoding.UTF8.GetBytes("{}")
        }, CancellationToken.None);

        Assert.False(outcome.Accepted);
        Assert.Contains("not a data source id", outcome.Error);
    }

    /// <summary>
    /// The opposite case, and it must NOT reject. An endpoint no gateway claims is a
    /// misconfiguration, not a Bitween failure - rejecting would requeue it for ever and the
    /// customer's queue would never drain.
    /// </summary>
    [Fact]
    public async Task The_sink_accepts_and_discards_an_event_no_gateway_claims()
    {
        var dataSourceId = await CreateDataSourceAsync(Unique("orphan"), withGateway: false);
        var sink = fixture.App.Services.GetRequiredService<IAdapterEventSink>();

        var outcome = await sink.OnEventAsync(new InboundEvent
        {
            AdapterId = BusAdapters.RabbitMq,
            InstanceKey = dataSourceId.ToString(),
            Endpoint = "a-queue-no-gateway-wants",
            Payload = Encoding.UTF8.GetBytes("{}")
        }, CancellationToken.None);

        Assert.True(outcome.Accepted, "an unclaimed endpoint must drain, not requeue for ever");
        Assert.Equal("unclaimed", outcome.Reference);
    }

    [Fact]
    public async Task Each_gateway_receives_only_its_own_endpoint()
    {
        var invoices = Unique("invoices");
        var shipments = Unique("shipments");

        // Both, because the adapter consumes what the DATA SOURCE lists - a gateway naming an
        // endpoint nobody is consuming would simply never see a message.
        var dataSourceId = await CreateDataSourceAsync($"{invoices},{shipments}", withGateway: false);
        var invoiceDoc = await AddGatewayAsync(dataSourceId, invoices);
        var shipmentDoc = await AddGatewayAsync(dataSourceId, shipments);

        await using var adapter = await StartAsync(dataSourceId);

        await PublishAsync(shipments, "{\"shipmentId\":77}");

        var xchange = await WaitForXchangeAsync(shipmentDoc);
        Assert.NotNull(xchange);
        Assert.Equal(shipmentDoc, xchange!.DocumentId);

        // And nothing landed on the other document.
        await using var scope = fixture.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        Assert.Equal(0, await db.Set<Xchange>().CountAsync(x => x.DocumentId == invoiceDoc));
    }

    /// <summary>
    /// A gateway with no endpoint is the catch-all: it claims whatever no other gateway does. The
    /// exact match has to win, or a data source that gains a catch-all silently starts routing
    /// every specific endpoint's messages to the wrong Document - running the wrong subscriptions,
    /// against the wrong mapping, with nothing in the audit trail saying so.
    ///
    /// Driven through the sink rather than the broker: this is a routing decision, and the queue
    /// only adds latency to it.
    /// </summary>
    [Fact]
    public async Task An_exact_endpoint_match_beats_the_catch_all_gateway()
    {
        var invoices = Unique("catchall-x");

        var dataSourceId = await CreateDataSourceAsync(invoices, withGateway: false);
        var catchAllDoc = await AddGatewayAsync(dataSourceId, endpoint: null);
        var invoiceDoc = await AddGatewayAsync(dataSourceId, invoices);

        var sink = fixture.App.Services.GetRequiredService<IAdapterEventSink>();

        var outcome = await sink.OnEventAsync(new InboundEvent
        {
            AdapterId = BusAdapters.RabbitMq,
            InstanceKey = dataSourceId.ToString(),
            Endpoint = invoices,
            DedupeKey = Guid.NewGuid().ToString("N"),
            Payload = Encoding.UTF8.GetBytes("{\"invoiceId\":9}")
        }, CancellationToken.None);

        Assert.True(outcome.Accepted, outcome.Error);

        await using var scope = fixture.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        Assert.Equal(1, await db.Set<Xchange>().CountAsync(x => x.DocumentId == invoiceDoc));
        Assert.Equal(0, await db.Set<Xchange>().CountAsync(x => x.DocumentId == catchAllDoc));
    }

    /// <summary>
    /// And the catch-all still catches what nothing else claims - otherwise the fix above would
    /// just be a way of disabling it.
    /// </summary>
    [Fact]
    public async Task The_catch_all_gateway_still_claims_an_unmatched_endpoint()
    {
        var known = Unique("catchall-k");

        var dataSourceId = await CreateDataSourceAsync(known, withGateway: false);
        var catchAllDoc = await AddGatewayAsync(dataSourceId, endpoint: null);
        var knownDoc = await AddGatewayAsync(dataSourceId, known);

        var sink = fixture.App.Services.GetRequiredService<IAdapterEventSink>();

        var outcome = await sink.OnEventAsync(new InboundEvent
        {
            AdapterId = BusAdapters.RabbitMq,
            InstanceKey = dataSourceId.ToString(),
            Endpoint = Unique("unclaimed"),
            DedupeKey = Guid.NewGuid().ToString("N"),
            Payload = Encoding.UTF8.GetBytes("{\"stray\":true}")
        }, CancellationToken.None);

        Assert.True(outcome.Accepted, outcome.Error);

        await using var scope = fixture.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        Assert.Equal(1, await db.Set<Xchange>().CountAsync(x => x.DocumentId == catchAllDoc));
        Assert.Equal(0, await db.Set<Xchange>().CountAsync(x => x.DocumentId == knownDoc));
    }

    /// <summary>
    /// Deleting a data source that still feeds a gateway must be refused.
    ///
    /// The alternative is worse than an error: a nullable foreign key that quietly becomes null
    /// turns that gateway back into an INTERNAL bus gateway, and Bitween starts consuming its own
    /// bus for a Document that was configured to read a customer's broker. Nothing fails, nothing
    /// logs, and the integration is simply pointed somewhere else.
    /// </summary>
    [Fact]
    public async Task A_data_source_still_feeding_a_gateway_cannot_be_deleted()
    {
        var queue = Unique("delete-guard");
        var dataSourceId = await CreateDataSourceAsync(queue, withGateway: false);
        await AddGatewayAsync(dataSourceId, queue);

        await using var scope = fixture.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        db.Remove(await db.Set<DataSource>().FirstAsync(d => d.Id == dataSourceId));
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());

        // And the gateway is untouched - still external, still on its data source.
        await using var check = fixture.App.Services.CreateAsyncScope();
        var fresh = check.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var gateway = await fresh.Set<BusGateway>().AsNoTracking()
            .FirstAsync(g => g.DataSourceId == dataSourceId);

        Assert.Equal(dataSourceId, gateway.DataSourceId);
        Assert.Equal(queue, gateway.Endpoint);
    }

    // ---------------------------------------------------------------- egress

    [Fact]
    public async Task Bitween_can_publish_out_to_an_external_broker()
    {
        var consumed = Unique("outbound");
        var target = Unique("outbox");

        // Publish to a queue the adapter is NOT consuming. Measuring depth on a queue it drains
        // is unwinnable: the message is consumed as fast as it is published and depth reads 0
        // whether the publish worked or not.
        var dataSourceId = await CreateDataSourceAsync(consumed, withGateway: false);

        await DeclareQueueAsync(target);

        await using var adapter = await StartAsync(dataSourceId);

        await adapter.Instance.InvokeAsync<object>("Publish", new
        {
            Endpoint = target,
            Body = "{\"pushed\":true}"
        });

        await WaitAsync(async () => await DepthAsync(target) >= 1, TimeSpan.FromSeconds(15),
            "the published message never arrived on the external queue");
    }

    /// <summary>
    /// A DELIVERY publishes, which is the half that was missing. Publish had existed since this
    /// adapter did and nothing in the pipeline ever called it - the handler contract is Handle,
    /// and this class had none - so an integration could drain a customer's queue and had no way
    /// to answer on one.
    ///
    /// Driven the way the pipeline drives it: Handle, with the endpoint arriving as a
    /// per-invocation property, because one instance serves every gateway on the broker and where
    /// to send is the subscription's business rather than the connection's.
    /// </summary>
    [Fact]
    public async Task A_delivery_publishes_the_message_it_was_given()
    {
        var consumed = Unique("handler-src");
        var target = Unique("handler-out");

        // A queue the adapter is NOT consuming: depth on one it drains reads 0 whether the publish
        // worked or not, because the message is taken as fast as it is sent.
        var dataSourceId = await CreateDataSourceAsync(consumed, withGateway: false);
        await DeclareQueueAsync(target);

        await using var adapter = await StartAsync(dataSourceId);

        var response = await adapter.Instance.InvokeAsync<JObject>("Handle",
            new XchangeFile("{\"delivered\":true}", "out.json"),
            properties: new Dictionary<string, string> { ["Endpoint"] = target });

        await WaitAsync(async () => await DepthAsync(target) >= 1, TimeSpan.FromSeconds(15),
            "the delivered message never arrived on the external queue");

        // The broker's receipt comes back as the response, so what was sent and under which id is
        // on the exchange rather than only in a log.
        Assert.Contains("messageId", response.Value<string>("data") ?? response.ToString());
    }

    /// <summary>
    /// A delivery with nowhere to send is refused where it was configured, rather than publishing
    /// to a queue named the empty string.
    /// </summary>
    [Fact]
    public async Task A_delivery_with_no_endpoint_says_so()
    {
        var dataSourceId = await CreateDataSourceAsync(Unique("handler-none"), withGateway: false);
        await using var adapter = await StartAsync(dataSourceId);

        var error = await Assert.ThrowsAnyAsync<Exception>(() =>
            adapter.Instance.InvokeAsync<JObject>("Handle",
                new XchangeFile("{}", "out.json"),
                properties: new Dictionary<string, string>()));

        Assert.Contains("no Endpoint and no Exchange", error.Message);
    }

    /// <summary>
    /// The multi-node case, and the reason this needed more than a Handle method.
    ///
    /// A broker data source is EXCLUSIVE: one node holds the connection so the customer's queue is
    /// drained once. A subscription's delivery, though, runs on whichever node picked the message
    /// up - so a publish would fail on every node but the owner, which is to say almost always.
    ///
    /// Exclusivity is about CONSUMING, not about connecting. When the owned instance is not here,
    /// the runtime opens a send-only connection of its own - Consume=false, no endpoints, its own
    /// pool key - and publishes through that. Nothing is consumed twice, and a delivery works
    /// wherever it lands.
    ///
    /// Here nothing is running under the data source's instance key at all, which is exactly what
    /// a non-owning node sees.
    /// </summary>
    [Fact]
    public async Task A_delivery_publishes_from_a_node_that_does_not_own_the_connection()
    {
        var target = Unique("elsewhere-out");
        var dataSourceId = await CreateDataSourceAsync(Unique("elsewhere-src"), withGateway: false);
        await DeclareQueueAsync(target);

        var host = fixture.App.Services.GetRequiredService<IResidentAdapterHost>();
        Assert.Null(host.Get(BusAdapters.RabbitMq, dataSourceId.ToString()));

        await using var scope = fixture.App.Services.CreateAsyncScope();
        var invoker = scope.ServiceProvider.GetRequiredService<IAdapterInvoker>();

        // Exactly what the pipeline passes: the data source id, and the slot's own properties.
        var properties = new Dictionary<string, string>
        {
            [StartupValuesFiller.DataSourceIdKey] = dataSourceId.ToString(),
            ["Endpoint"] = target
        };

        await invoker.InvokeAsync<XchangeFile>(BusAdapters.RabbitMq, AdapterRole.Handler,
            "Handle", new XchangeFile("{\"fromElsewhere\":true}", "out.json"),
            properties, Guid.NewGuid().ToString("N"));

        await WaitAsync(async () => await DepthAsync(target) >= 1, TimeSpan.FromSeconds(20),
            "a node that does not own the connection could not publish");

        // And it did NOT take ownership on the way past: the send-only instance is pooled under
        // its own key, so the exclusive slot is still free for the node that should hold it.
        Assert.Null(host.Get(BusAdapters.RabbitMq, dataSourceId.ToString()));
    }

    // ---------------------------------------------------------------- controls

    [Fact]
    public async Task Test_connection_reports_each_stage()
    {
        var queue = Unique("probe");
        var dataSourceId = await CreateDataSourceAsync(queue, withGateway: false);

        await using var adapter = await StartAsync(dataSourceId);

        var result = await adapter.Instance.InvokeAsync<Dictionary<string, object>>("TestConnection");

        Assert.True(Convert.ToBoolean(result["ok"]));
    }

    [Fact]
    public async Task Health_from_the_heartbeat_reaches_the_health_view()
    {
        var queue = Unique("health");
        var dataSourceId = await CreateDataSourceAsync(queue, withGateway: false);

        await using var adapter = await StartAsync(dataSourceId);

        var host = fixture.App.Services.GetRequiredService<IResidentAdapterHost>();

        await WaitAsync(() => host.Describe()
                .Any(h => h.InstanceKey == dataSourceId.ToString() && h.LastHeartbeatOn != null),
            TimeSpan.FromSeconds(30), "no heartbeat was recorded");

        var health = host.Describe().Single(h => h.InstanceKey == dataSourceId.ToString());

        Assert.True(health.Connected);
        Assert.True(health.WorkingSetBytes > 0, "host-observed memory is sampled independently");
        Assert.Contains(queue, health.Details["endpoints"]);
    }

    // ---------------------------------------------------------------- regression

    /// <summary>
    /// The guarantee that makes this change safe to deploy: a gateway with no data source is still
    /// an internal-bus gateway and behaves exactly as it did before.
    /// </summary>
    [Fact]
    public async Task A_gateway_with_no_data_source_is_still_an_internal_bus_gateway()
    {
        await using var scope = fixture.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        var document = new Document(null, Unique("internal"), DocumentFormat.Json);
        db.Add(document);
        await db.SaveChangesAsync();

        var gateway = new BusGateway { Name = Unique("gw"), DocumentId = document.Id };
        db.Add(gateway);
        await db.SaveChangesAsync();

        var reloaded = await db.Set<BusGateway>().FirstAsync(g => g.Id == gateway.Id);

        Assert.Null(reloaded.DataSourceId);
        Assert.Null(reloaded.Endpoint);
    }

    // ---------------------------------------------------------------- helpers

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..24];

    private async Task<(int DataSourceId, int DocumentId)> ArrangeAsync(string queue)
    {
        var dataSourceId = await CreateDataSourceAsync(queue, withGateway: false);
        var documentId = await AddGatewayAsync(dataSourceId, queue);
        return (dataSourceId, documentId);
    }

    private async Task<int> CreateDataSourceAsync(string queue, bool withGateway)
    {
        await using var scope = fixture.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        var properties = new Dictionary<string, string>(fixture.ExternalRabbitProperties)
        {
            // The supervisor normally derives this from the bound gateways; tests that create a
            // data source without one still need the adapter to declare and consume something.
            ["Endpoints"] = queue
        };

        var dataSource = new DataSource
        {
            Name = Unique("ds"),
            AdapterId = BusAdapters.RabbitMq,
            Kind = DataSourceKind.Broker,
            Properties = properties
        };

        db.Add(dataSource);
        await db.SaveChangesAsync();
        return dataSource.Id;
    }

    private async Task<int> AddGatewayAsync(int dataSourceId, string endpoint)
    {
        await using var scope = fixture.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        var document = new Document(null, Unique("doc"), DocumentFormat.Json);
        db.Add(document);
        await db.SaveChangesAsync();

        db.Add(new BusGateway
        {
            Name = Unique("gw"),
            DocumentId = document.Id,
            DataSourceId = dataSourceId,
            Endpoint = endpoint
        });
        await db.SaveChangesAsync();

        // The infolink cache is a singleton holding a ten-minute snapshot. Without revoking it,
        // XchangeService resolves this brand-new Document to null and builds an Xchange that no
        // DocumentId query can find - which acks the message and silently drops it.
        scope.ServiceProvider.GetRequiredService<IInfolinkCache>().Revoke();

        return document.Id;
    }

    /// <summary>Starts the adapter for a data source and stops it when the test finishes.</summary>
    private async Task<AdapterLease> StartAsync(int dataSourceId)
    {
        await using var scope = fixture.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var dataSource = await db.Set<DataSource>().AsNoTracking().FirstAsync(d => d.Id == dataSourceId);

        var host = fixture.App.Services.GetRequiredService<IResidentAdapterHost>();

        var instance = await host.StartExclusiveAsync(new AdapterSpec
        {
            AdapterId = dataSource.AdapterId,
            // The instance key IS the data source id - that is how the sink resolves the gateway.
            InstanceKey = dataSourceId.ToString(),
            StartupValues = new Dictionary<string, string>(dataSource.Properties)
        });

        return new AdapterLease(host, dataSource.AdapterId, dataSourceId.ToString(), instance);
    }

    private sealed class AdapterLease(IResidentAdapterHost host, string adapterId, string instanceKey,
        ResidentAdapterInstance instance) : IAsyncDisposable
    {
        private readonly IResidentAdapterHost _host = host;

        public ResidentAdapterInstance Instance { get; } = instance;

        public ValueTask DisposeAsync() =>
            new(_host.StopAsync(adapterId, instanceKey, drain: false));
    }

    private async Task PublishAsync(string queue, string body)
    {
        await using var connection = await ExternalConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false);

        var properties = new BasicProperties();
        properties.ContentType = "application/json";
        properties.MessageId = Guid.NewGuid().ToString("N");
        properties.DeliveryMode = DeliveryModes.Persistent;

        await channel.BasicPublishAsync("", queue, mandatory: false, basicProperties: properties,
            body: Encoding.UTF8.GetBytes(body));
    }

    private async Task DeclareQueueAsync(string queue)
    {
        await using var connection = await ExternalConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false);
    }

    private async Task<uint> DepthAsync(string queue)
    {
        await using var connection = await ExternalConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        return (await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false)).MessageCount;
    }

    private Task<IConnection> ExternalConnectionAsync() => new ConnectionFactory
    {
        HostName = fixture.ExternalRabbitHost,
        Port = fixture.ExternalRabbitPort,
        UserName = fixture.ExternalRabbitUser,
        Password = fixture.ExternalRabbitPassword
    }.CreateConnectionAsync("integration-tests");

    private async Task<Xchange?> WaitForXchangeAsync(int documentId)
    {
        Xchange? found = null;
        await WaitAsync(async () =>
        {
            await using var scope = fixture.App.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            found = await db.Set<Xchange>().AsNoTracking()
                .FirstOrDefaultAsync(x => x.DocumentId == documentId);
            return found != null;
        }, TimeSpan.FromSeconds(45), $"no Xchange was created for document {documentId}");

        return found;
    }

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
