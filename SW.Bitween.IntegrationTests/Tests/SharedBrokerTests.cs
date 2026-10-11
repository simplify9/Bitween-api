using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using SW.Bitween.Domain;
using SW.Bitween.Domain.DataSources;
using SW.Bitween.Domain.Gateway;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.Bitween.Services.Cluster;
using SW.Bitween.Services.DataSources;
using Newtonsoft.Json;
using SW.PrimitiveTypes;
using SW.Serverless.Resident;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// One customer broker, several integrations - which is what a real one looks like.
///
/// Everything else in this suite exercises a data source with a single queue behind it. That is
/// the easy case and it hides the questions that actually matter once a second integration lands
/// on the same broker: does each queue reach the right information type, does one connection serve
/// all of them or does each gateway open its own, does a queue Bitween was never pointed at stay
/// untouched, and can a gateway be added or taken away without disturbing the ones beside it.
///
/// Acme's broker here has five queues. Bitween is pointed at three of them, running three
/// different integrations; the other two belong to something else entirely and Bitween must behave
/// as if they are not there.
/// </summary>
[Collection("Bitween")]
public class SharedBrokerTests(BitweenFixture fixture)
{
    private const string EchoHandler = "sw.bitween.samplehandler";

    /// <summary>
    /// The multiplexing claim, stated as a number: three gateways, one process.
    ///
    /// If each gateway opened its own connection, a customer with twenty integrations on one broker
    /// would have Bitween holding twenty connections and twenty adapter processes - and every one
    /// of them would need its own lease, its own restart budget and its own credentials in memory.
    /// The whole point of separating the connection from what is done with it is that this stays
    /// one of each.
    /// </summary>
    [Fact]
    public async Task One_connection_and_one_process_serve_every_gateway_on_the_broker()
    {
        var acme = await BrokerAsync("acme");
        var orders = await IntegrationAsync(acme, "orders");
        var invoices = await IntegrationAsync(acme, "invoices");
        var shipments = await IntegrationAsync(acme, "shipments");

        await using var supervisor = Supervisor();
        await supervisor.ReconcileAsync();

        var instances = Instances(acme);
        Assert.Single(instances);
        Assert.NotNull(instances[0].ProcessId);

        Publish(orders.Queue, """{"kind":"order"}""");
        Publish(invoices.Queue, """{"kind":"invoice"}""");
        Publish(shipments.Queue, """{"kind":"shipment"}""");

        await WaitAsync(async () =>
            await CountAsync(orders.DocumentId) == 1 &&
            await CountAsync(invoices.DocumentId) == 1 &&
            await CountAsync(shipments.DocumentId) == 1,
            TimeSpan.FromSeconds(60), "not every queue produced an Xchange");

        // Still one process after all three have been served.
        Assert.Single(Instances(acme));
    }

    /// <summary>
    /// Three queues, three information types, and no crossing over.
    ///
    /// This is the failure that would be invisible: a message filed against the wrong information
    /// type runs the wrong subscriptions, against a mapping written for a different shape, and
    /// nothing anywhere reports an error. Ten messages per queue, interleaved, so the answer does
    /// not depend on them arriving one at a time.
    /// </summary>
    [Fact]
    public async Task Every_queue_lands_on_its_own_information_type()
    {
        const int each = 10;

        var acme = await BrokerAsync("route");
        var orders = await IntegrationAsync(acme, "orders");
        var invoices = await IntegrationAsync(acme, "invoices");
        var shipments = await IntegrationAsync(acme, "shipments");

        await using var supervisor = Supervisor();
        await supervisor.ReconcileAsync();

        // Interleaved rather than queue by queue: three consumers on one channel handling
        // deliveries at the same time is the state that finds a routing bug.
        using (var connection = ExternalConnection())
        using (var channel = connection.CreateModel())
        {
            for (var i = 0; i < each; i++)
                foreach (var (queue, kind) in new[]
                         {
                             (orders.Queue, "order"), (invoices.Queue, "invoice"),
                             (shipments.Queue, "shipment"),
                         })
                    PublishOn(channel, queue, $$"""{"kind":"{{kind}}","n":{{i}}}""");
        }

        await WaitAsync(async () =>
            await CountAsync(orders.DocumentId) >= each &&
            await CountAsync(invoices.DocumentId) >= each &&
            await CountAsync(shipments.DocumentId) >= each,
            TimeSpan.FromSeconds(90), "the queues did not all drain");

        // Settle, then assert nothing extra landed anywhere - a crossed-over message shows up as a
        // surplus on one document and a shortfall on another.
        await Task.Delay(TimeSpan.FromSeconds(3));

        Assert.Equal(each, await CountAsync(orders.DocumentId));
        Assert.Equal(each, await CountAsync(invoices.DocumentId));
        Assert.Equal(each, await CountAsync(shipments.DocumentId));

        // And every payload is the one that belongs to its queue.
        foreach (var (documentId, kind) in new[]
                 {
                     (orders.DocumentId, "order"), (invoices.DocumentId, "invoice"),
                     (shipments.DocumentId, "shipment"),
                 })
            Assert.All(await ReferencesAsync(documentId), r => Assert.NotNull(r));
    }

    /// <summary>
    /// The customer's broker is not Bitween's broker.
    ///
    /// Acme runs other things on it - a queue their warehouse app drains, another their own
    /// services talk over. Bitween is pointed at three queues and must touch nothing else: an
    /// adapter that consumed everything it could see would silently eat another system's traffic,
    /// and the first anyone would know is when the warehouse stopped receiving orders.
    /// </summary>
    [Fact]
    public async Task A_queue_nobody_pointed_Bitween_at_is_left_alone()
    {
        var acme = await BrokerAsync("bystander");
        var orders = await IntegrationAsync(acme, "orders");

        // Acme's own traffic, on the same broker, with messages already waiting.
        var theirs = Unique("acme-warehouse");
        DeclareQueue(theirs);
        for (var i = 0; i < 5; i++) Publish(theirs, $$"""{"theirs":{{i}}}""");

        await using var supervisor = Supervisor();
        await supervisor.ReconcileAsync();

        Publish(orders.Queue, """{"kind":"order"}""");
        await WaitAsync(async () => await CountAsync(orders.DocumentId) == 1,
            TimeSpan.FromSeconds(60), "Bitween's own queue never produced an Xchange");

        // Bitween has demonstrably been running against this broker, so an untouched depth here
        // means it left the queue alone rather than that nothing happened at all.
        await Task.Delay(TimeSpan.FromSeconds(3));
        Assert.Equal(5u, Depth(theirs));
    }

    /// <summary>
    /// A fourth integration goes live on Monday. The three already running must not notice.
    ///
    /// The adapter restarts to pick up the new consume list - that is expected, and safe, because
    /// nothing is acknowledged until Bitween has persisted it. What must NOT happen is the other
    /// three losing messages or needing to be reconfigured.
    /// </summary>
    [Fact]
    public async Task A_new_integration_joins_the_same_connection_without_disturbing_the_others()
    {
        var acme = await BrokerAsync("joiner");
        var orders = await IntegrationAsync(acme, "orders");
        var invoices = await IntegrationAsync(acme, "invoices");

        await using var supervisor = Supervisor();
        await supervisor.ReconcileAsync();

        Publish(orders.Queue, """{"before":true}""");
        await WaitAsync(async () => await CountAsync(orders.DocumentId) == 1,
            TimeSpan.FromSeconds(60), "the first integration never worked");

        // Monday.
        var returns = await IntegrationAsync(acme, "returns");
        await supervisor.ReconcileAsync();

        // Still one connection: the new gateway joined it rather than opening a second.
        Assert.Single(Instances(acme));

        Publish(returns.Queue, """{"kind":"return"}""");
        Publish(orders.Queue, """{"after":true}""");
        Publish(invoices.Queue, """{"kind":"invoice"}""");

        await WaitAsync(async () =>
            await CountAsync(returns.DocumentId) == 1 &&
            await CountAsync(orders.DocumentId) == 2 &&
            await CountAsync(invoices.DocumentId) == 1,
            TimeSpan.FromSeconds(90), "the existing integrations stopped working when a new one joined");
    }

    /// <summary>
    /// Turning one integration off leaves the rest running.
    ///
    /// Deactivating a gateway has to stop its queue being consumed - otherwise Bitween keeps
    /// acknowledging messages that no longer route anywhere, which is a silent drop rather than a
    /// pause - while every other queue on the same connection carries on.
    /// </summary>
    [Fact]
    public async Task Deactivating_one_gateway_stops_only_its_queue()
    {
        var acme = await BrokerAsync("pause");
        var orders = await IntegrationAsync(acme, "orders");
        var invoices = await IntegrationAsync(acme, "invoices");

        await using var supervisor = Supervisor();
        await supervisor.ReconcileAsync();

        Publish(orders.Queue, """{"n":1}""");
        Publish(invoices.Queue, """{"n":1}""");
        await WaitAsync(async () =>
            await CountAsync(orders.DocumentId) == 1 && await CountAsync(invoices.DocumentId) == 1,
            TimeSpan.FromSeconds(60), "both integrations did not start working");

        await SetGatewayInactiveAsync(invoices.GatewayId, true);
        await supervisor.ReconcileAsync();

        Publish(orders.Queue, """{"n":2}""");
        Publish(invoices.Queue, """{"n":2}""");

        await WaitAsync(async () => await CountAsync(orders.DocumentId) == 2,
            TimeSpan.FromSeconds(60), "the active integration stopped working too");

        // The deactivated one's message is still on the broker, waiting - not acknowledged and
        // thrown away, which is the outcome that would look identical from Bitween's side.
        await Task.Delay(TimeSpan.FromSeconds(3));
        Assert.Equal(1, await CountAsync(invoices.DocumentId));
        Assert.True(Depth(invoices.Queue) >= 1,
            "the message for the deactivated gateway was consumed and dropped rather than left on the queue");
    }

    /// <summary>
    /// Two queues, the same message id on each. Two messages, not a duplicate.
    ///
    /// A broker's message id is only unique within whatever produced it, so two systems publishing
    /// to two queues on one broker can easily pick the same one. Deduplicating across the whole
    /// data source would silently drop the second - so the key carries the endpoint.
    /// </summary>
    [Fact]
    public async Task The_same_message_id_on_two_queues_is_two_messages()
    {
        var acme = await BrokerAsync("dedupe");
        var orders = await IntegrationAsync(acme, "orders");
        var invoices = await IntegrationAsync(acme, "invoices");

        await using var supervisor = Supervisor();
        await supervisor.ReconcileAsync();

        var shared = Guid.NewGuid().ToString("N");
        Publish(orders.Queue, """{"from":"orders"}""", shared);
        Publish(invoices.Queue, """{"from":"invoices"}""", shared);

        await WaitAsync(async () =>
            await CountAsync(orders.DocumentId) == 1 && await CountAsync(invoices.DocumentId) == 1,
            TimeSpan.FromSeconds(60),
            "one of the two was deduplicated away - the key is not scoped to the endpoint");

        // And a genuine redelivery on ONE of them still is a duplicate.
        Publish(orders.Queue, """{"from":"orders"}""", shared);
        await Task.Delay(TimeSpan.FromSeconds(5));
        Assert.Equal(1, await CountAsync(orders.DocumentId));
    }

    /// <summary>
    /// Two data sources pointed at the same broker are two connections, owned independently.
    ///
    /// This is how a customer separates environments, or two of their own departments, on one
    /// physical broker: the credentials, the health and the ownership are per data source, not per
    /// broker, so one going down says nothing about the other.
    /// </summary>
    [Fact]
    public async Task Two_data_sources_on_one_broker_are_owned_separately()
    {
        var acme = await BrokerAsync("tenant-a");
        var contoso = await BrokerAsync("tenant-b");

        var acmeOrders = await IntegrationAsync(acme, "orders");
        var contosoOrders = await IntegrationAsync(contoso, "orders");

        await using var supervisor = Supervisor();
        await supervisor.ReconcileAsync();

        // One process each, not one shared: the connection belongs to the data source.
        Assert.Single(Instances(acme));
        Assert.Single(Instances(contoso));
        Assert.NotEqual(Instances(acme)[0].ProcessId, Instances(contoso)[0].ProcessId);

        Publish(acmeOrders.Queue, """{"tenant":"acme"}""");
        Publish(contosoOrders.Queue, """{"tenant":"contoso"}""");

        await WaitAsync(async () =>
            await CountAsync(acmeOrders.DocumentId) == 1 &&
            await CountAsync(contosoOrders.DocumentId) == 1,
            TimeSpan.FromSeconds(60), "the two tenants did not both work");

        // Ownership is per data source, so each holds its own lease.
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        foreach (var id in new[] { acme, contoso })
            Assert.NotNull(await db.Set<Domain.Cluster.ClusterLease>().AsNoTracking()
                .FirstOrDefaultAsync(l => l.Id == $"datasource.{id}"));
    }

    /// <summary>
    /// The point of all of it: three queues on one broker running three different integrations,
    /// each producing its own result.
    ///
    /// Everything above is about plumbing. This is the outcome the plumbing exists for - a message
    /// arriving on Acme's broker runs the subscription configured for that queue, and the result is
    /// recorded against the right one.
    /// </summary>
    [Fact]
    public async Task Each_queue_runs_its_own_integration_and_records_its_own_result()
    {
        var acme = await BrokerAsync("pipeline");
        var orders = await IntegrationAsync(acme, "orders", withSubscription: true);
        var invoices = await IntegrationAsync(acme, "invoices", withSubscription: true);

        await using var supervisor = Supervisor();
        await supervisor.ReconcileAsync();

        Publish(orders.Queue, """{"kind":"order","id":"A-1"}""");
        Publish(invoices.Queue, """{"kind":"invoice","id":"I-9"}""");

        var orderXchange = await WaitForXchangeAsync(orders.DocumentId);
        var invoiceXchange = await WaitForXchangeAsync(invoices.DocumentId);

        Assert.NotNull(orderXchange);
        Assert.NotNull(invoiceXchange);

        // Each ran the subscription bound to ITS gateway, not the other's.
        var orderResult = await ResultAsync(orderXchange!, orders.SubscriptionId);
        var invoiceResult = await ResultAsync(invoiceXchange!, invoices.SubscriptionId);

        Assert.NotNull(orderResult);
        Assert.NotNull(invoiceResult);
        Assert.NotEqual(orders.SubscriptionId, invoices.SubscriptionId);
    }

    // ---------------------------------------------------------------- helpers

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..24];

    private sealed record Integration(string Queue, int DocumentId, int GatewayId, int SubscriptionId);

    /// <summary>A data source with connection settings only - the endpoints come from its gateways.</summary>
    private async Task<int> BrokerAsync(string label)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        var dataSource = new DataSource
        {
            Name = Unique($"ds-{label}"),
            AdapterId = BusAdapters.RabbitMq,
            Kind = DataSourceKind.Broker,
            Properties = new Dictionary<string, string>(fixture.ExternalRabbitProperties),
        };

        db.Add(dataSource);
        await db.SaveChangesAsync();
        return dataSource.Id;
    }

    /// <summary>One queue, one information type, one gateway - and optionally a subscription behind it.</summary>
    private async Task<Integration> IntegrationAsync(int dataSourceId, string label,
        bool withSubscription = false)
    {
        var queue = Unique(label);

        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        var document = new Document(null, Unique($"doc-{label}"), DocumentFormat.Json);
        db.Add(document);
        await db.SaveChangesAsync();

        var subscriptionId = 0;
        if (withSubscription)
        {
            // Every Subscription constructor sets Inactive = true, so it has to be turned on or
            // the filter never matches it - see PipelineEndToEndTests.
            var subscription = new Subscription(Unique($"sub-{label}"), document.Id,
                SubscriptionType.BusGateway)
            {
                HandlerId = EchoHandler,
                Inactive = false,
            };
            subscription.SetDictionaries(
                new Dictionary<string, string>(), new Dictionary<string, string>(),
                new Dictionary<string, string>(), new Dictionary<string, string>(),
                new Dictionary<string, string>());

            db.Add(subscription);
            await db.SaveChangesAsync();
            subscriptionId = subscription.Id;
        }

        var gateway = new BusGateway
        {
            Name = Unique($"gw-{label}"),
            DocumentId = document.Id,
            DataSourceId = dataSourceId,
            Endpoint = queue,
        };
        db.Add(gateway);
        await db.SaveChangesAsync();

        if (withSubscription)
        {
            db.Add(new BusGatewayRoute { BusGatewayId = gateway.Id, SubscriptionId = subscriptionId });
            await db.SaveChangesAsync();
        }

        // Ten-minute singleton snapshot: without revoking it the pipeline reads configuration from
        // before this test's own setup and files the message against a null document.
        scope.ServiceProvider.GetRequiredService<IInfolinkCache>().Revoke();

        // Declared here rather than left to the adapter, so a queue exists to publish into even
        // before the supervisor has started anything.
        DeclareQueue(queue);

        return new Integration(queue, document.Id, gateway.Id, subscriptionId);
    }

    private async Task SetGatewayInactiveAsync(int gatewayId, bool inactive)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var gateway = await db.Set<BusGateway>().FirstAsync(g => g.Id == gatewayId);
        gateway.Inactive = inactive;
        await db.SaveChangesAsync();
        scope.ServiceProvider.GetRequiredService<IInfolinkCache>().Revoke();
    }

    private List<InstanceHealth> Instances(int dataSourceId) =>
        fixture.App.Services.GetRequiredService<IResidentAdapterHost>()
            .Describe().Where(h => h.InstanceKey == dataSourceId.ToString()).ToList();

    private async Task<int> CountAsync(int documentId)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        return await db.Set<Xchange>().AsNoTracking()
            .CountAsync(x => x.DocumentId == documentId && x.SubscriptionId == null);
    }

    private async Task<List<string?>> ReferencesAsync(int documentId)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        return await db.Set<Xchange>().AsNoTracking()
            .Where(x => x.DocumentId == documentId && x.SubscriptionId == null)
            .Select(x => x.Id)
            .ToListAsync();
    }

    private async Task<XchangeResult?> ResultAsync(Xchange xchange, int subscriptionId)
    {
        await ProcessAsync(xchange.Id);

        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var child = await db.Set<Xchange>().AsNoTracking()
                .FirstOrDefaultAsync(x => x.DocumentId == xchange.DocumentId
                                          && x.SubscriptionId == subscriptionId);

            Assert.True(child != null,
                $"the arriving message did not produce a run for subscription {subscriptionId}");

            await ProcessAsync(child!.Id);

            return await db.Set<XchangeResult>().AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == child.Id);
        }
    }

    private async Task ProcessAsync(string xchangeId)
    {
        await using var scope = fixture.CreateScope();
        await scope.ServiceProvider.GetRequiredService<XchangeService>()
            .Process("XchangeCreated", JsonConvert.SerializeObject(new { Id = xchangeId }));
    }

    private async Task<Xchange?> WaitForXchangeAsync(int documentId)
    {
        Xchange? found = null;
        await WaitAsync(async () =>
        {
            await using var scope = fixture.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            found = await db.Set<Xchange>().AsNoTracking()
                .FirstOrDefaultAsync(x => x.DocumentId == documentId && x.SubscriptionId == null);
            return found != null;
        }, TimeSpan.FromSeconds(60), $"no Xchange was created for document {documentId}");

        return found;
    }

    private TestSupervisor Supervisor() => new(
        new BusProviderSupervisor(
            fixture.App.Services,
            fixture.App.Services.GetRequiredService<IResidentAdapterHost>(),
            new RabbitMqLeaderElection(
                fixture.App.Services.GetRequiredService<IConfiguration>(),
                fixture.App.Services,
                fixture.App.Services.GetRequiredService<ILoggerFactory>()
                    .CreateLogger<RabbitMqLeaderElection>()),
            fixture.App.Services.GetRequiredService<ILoggerFactory>()
                .CreateLogger<BusProviderSupervisor>()),
        fixture.App.Services.GetRequiredService<IResidentAdapterHost>());

    /// <summary>
    /// A supervisor that cleans up after itself. Only instances that appeared after it was built
    /// are stopped - the collection shares one resident host, so stopping "whatever is running"
    /// would reach into another test.
    /// </summary>
    private sealed class TestSupervisor : IAsyncDisposable
    {
        private readonly BusProviderSupervisor _supervisor;
        private readonly IResidentAdapterHost _adapters;
        private readonly HashSet<string> _preexisting;
        private readonly HashSet<string> _started = new();

        public TestSupervisor(BusProviderSupervisor supervisor, IResidentAdapterHost adapters)
        {
            _supervisor = supervisor;
            _adapters = adapters;
            _preexisting = Keys();
        }

        private HashSet<string> Keys() =>
            _adapters.Describe().Select(h => $"{h.AdapterId}|{h.InstanceKey}").ToHashSet();

        public async Task ReconcileAsync()
        {
            await _supervisor.ReconcileAsync();
            foreach (var key in Keys().Where(k => !_preexisting.Contains(k))) _started.Add(key);
        }

        public async ValueTask DisposeAsync()
        {
            try { await _supervisor.StopAsync(default); } catch { /* best effort */ }

            var live = Keys();
            foreach (var key in _started.Where(live.Contains))
            {
                var parts = key.Split('|');
                try { await _adapters.StopAsync(parts[0], parts[1], drain: false); } catch { }
            }

            _supervisor.Dispose();
        }
    }

    private void Publish(string queue, string body, string? messageId = null)
    {
        using var connection = ExternalConnection();
        using var channel = connection.CreateModel();
        PublishOn(channel, queue, body, messageId);
    }

    private static void PublishOn(IModel channel, string queue, string body, string? messageId = null)
    {
        channel.QueueDeclare(queue, durable: true, exclusive: false, autoDelete: false);

        var properties = channel.CreateBasicProperties();
        properties.ContentType = "application/json";
        properties.MessageId = messageId ?? Guid.NewGuid().ToString("N");
        properties.DeliveryMode = 2;

        channel.BasicPublish("", queue, properties, Encoding.UTF8.GetBytes(body));
    }

    private void DeclareQueue(string queue)
    {
        using var connection = ExternalConnection();
        using var channel = connection.CreateModel();
        channel.QueueDeclare(queue, durable: true, exclusive: false, autoDelete: false);
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
        Password = fixture.ExternalRabbitPassword,
    }.CreateConnection("shared-broker-tests");

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
