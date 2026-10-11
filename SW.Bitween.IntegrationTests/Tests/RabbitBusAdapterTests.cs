using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using SW.Bitween.Domain;
using SW.Bitween.Domain.DataSources;
using SW.Bitween.Domain.Gateway;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using SW.Serverless.Resident;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// The ingress adapter itself, under the conditions that break a broker client rather than the
/// conditions that demonstrate one.
///
/// The gateway tests prove a message arrives. These ask the harder questions: what happens when two
/// messages are byte-for-byte identical, and what happens when many arrive at once - the two cases
/// where a consumer quietly loses data instead of failing loudly.
/// </summary>
[Collection("Bitween")]
public class RabbitBusAdapterTests(BitweenFixture fixture)
{
    /// <summary>
    /// Two identical messages are two messages.
    ///
    /// Deduplication needs a key, and a publisher that sets no MessageId gives us none. Hashing the
    /// body to manufacture one looks like a reasonable fallback and is not: a customer sending the
    /// same instruction twice - reorder the same item, the same daily totals, a heartbeat - would
    /// have the second silently swallowed for the whole deduplication window, which is thirty days
    /// by default. No error, no Xchange, no way to notice.
    ///
    /// So an unkeyed delivery is handled once per delivery. That trades a possible reprocess on
    /// redelivery for never dropping a real message, which is the right way round.
    /// </summary>
    [Fact]
    public async Task Two_identical_messages_with_no_message_id_are_two_Xchanges()
    {
        var queue = Unique("rb-ident");
        var (_, documentId, lease) = await ArrangeAsync(queue);
        await using var _ = lease;

        const string body = """{"instruction":"reorder","sku":"A-1"}""";

        PublishRaw(queue, body, messageId: null);
        PublishRaw(queue, body, messageId: null);

        await WaitAsync(async () => await CountAsync(documentId) >= 2, TimeSpan.FromSeconds(45),
            "the second identical message was swallowed - a content hash is being used as identity");

        Assert.Equal(2, await CountAsync(documentId));
    }

    /// <summary>
    /// The same body WITH message ids is still two messages, and a redelivery of one of them is
    /// not. This is the line the previous test is defending: identity comes from the broker's id,
    /// never from the payload.
    /// </summary>
    [Fact]
    public async Task Identity_comes_from_the_message_id_not_from_the_body()
    {
        var queue = Unique("rb-identity");
        var (_, documentId, lease) = await ArrangeAsync(queue);
        await using var _ = lease;

        const string body = """{"same":"payload"}""";
        var redelivered = Guid.NewGuid().ToString("N");

        PublishRaw(queue, body, Guid.NewGuid().ToString("N"));
        PublishRaw(queue, body, redelivered);
        PublishRaw(queue, body, redelivered);   // the redelivery

        await WaitAsync(async () => await CountAsync(documentId) >= 2, TimeSpan.FromSeconds(45),
            "two distinct messages did not both produce an Xchange");

        // Settle, then prove the third did NOT land. Waiting for an absence needs a pause; without
        // one this asserts on a message still in flight rather than on one that was rejected.
        await Task.Delay(TimeSpan.FromSeconds(5));
        Assert.Equal(2, await CountAsync(documentId));
    }

    /// <summary>
    /// Concurrent deliveries, which is the normal state of a consumer with prefetch above one.
    ///
    /// Every delivery is handled on the thread pool, so acknowledgements land on the shared channel
    /// from several threads at once - and RabbitMQ.Client does not support concurrent application
    /// operations on one model. A corrupted frame stream drops the channel and redelivers
    /// everything in flight, so what this asserts is the outcome that matters: every message
    /// arrives, exactly once, and the connection is still up afterwards.
    ///
    /// This is an integrity test, not a race detector - a race that is not serialised may still
    /// pass on a quiet machine. It fails loudly when the ordering breaks, which is what a test can
    /// honestly offer here.
    /// </summary>
    [Fact]
    public async Task Every_message_survives_concurrent_delivery_exactly_once()
    {
        const int count = 60;

        var queue = Unique("rb-concurrent");
        var (dataSourceId, documentId, lease) = await ArrangeAsync(queue);
        await using var _ = lease;

        PublishMany(queue, count);

        await WaitAsync(async () => await CountAsync(documentId) >= count, TimeSpan.FromSeconds(90),
            $"only {await CountAsync(documentId)} of {count} messages became an Xchange");

        // Nothing extra either: a channel that dropped mid-flight would redeliver, and with a
        // dedupe key per message that would show up as a shortfall rather than a surplus - so
        // check both ends.
        await Task.Delay(TimeSpan.FromSeconds(5));
        Assert.Equal(count, await CountAsync(documentId));

        var health = InstanceOf(dataSourceId);
        Assert.NotNull(health);
        Assert.True(health!.Connected, $"the adapter lost its connection: {health.LastError}");

        Assert.Equal(count.ToString(), health.Details["received"]);
        Assert.Equal(count.ToString(), health.Details["acked"]);
        Assert.Equal("0", health.Details["nacked"]);
        Assert.Equal("0", health.Details["failed"]);
    }

    /// <summary>
    /// Egress under concurrency. Publishing builds the message properties on the channel too, so a
    /// publish that only guards the send is still two channel operations racing.
    /// </summary>
    [Fact]
    public async Task Concurrent_publishes_all_reach_the_broker()
    {
        const int count = 40;

        var queue = Unique("rb-egress");
        DeclareQueue(queue);

        var (_, _, lease) = await ArrangeAsync(Unique("rb-egress-in"));
        await using var _ = lease;

        await Task.WhenAll(Enumerable.Range(0, count).Select(i =>
            lease.Instance.InvokeAsync<object>("Publish", new
            {
                Endpoint = queue,
                Body = $$"""{"n":{{i}}}""",
                ContentType = "application/json"
            }, timeoutSeconds: 30)));

        await WaitAsync(() => Task.FromResult(Depth(queue) >= count), TimeSpan.FromSeconds(30),
            $"only {Depth(queue)} of {count} published messages reached the queue");

        Assert.Equal((uint)count, Depth(queue));
    }

    // ---------------------------------------------------------------- helpers

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..24];

    private async Task<(int DataSourceId, int DocumentId, AdapterLease Lease)> ArrangeAsync(string queue)
    {
        int dataSourceId, documentId;

        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

            var dataSource = new DataSource
            {
                Name = Unique("ds"),
                AdapterId = BusAdapters.RabbitMq,
                Kind = DataSourceKind.Broker,
                Properties = new Dictionary<string, string>(fixture.ExternalRabbitProperties)
                {
                    ["Endpoints"] = queue
                }
            };
            db.Add(dataSource);

            var document = new Document(null, Unique("doc"), DocumentFormat.Json);
            db.Add(document);
            await db.SaveChangesAsync();

            db.Add(new BusGateway
            {
                Name = Unique("gw"),
                DocumentId = document.Id,
                DataSourceId = dataSource.Id,
                Endpoint = queue
            });
            await db.SaveChangesAsync();

            // The infolink cache is a ten-minute singleton snapshot; without revoking it a
            // brand-new Document resolves to null and the message is acked and silently dropped.
            scope.ServiceProvider.GetRequiredService<IInfolinkCache>().Revoke();

            dataSourceId = dataSource.Id;
            documentId = document.Id;
        }

        var host = fixture.App.Services.GetRequiredService<IResidentAdapterHost>();
        var instance = await host.StartExclusiveAsync(new AdapterSpec
        {
            AdapterId = BusAdapters.RabbitMq,
            InstanceKey = dataSourceId.ToString(),
            StartupValues = new Dictionary<string, string>(fixture.ExternalRabbitProperties)
            {
                ["Endpoints"] = queue
            }
        });

        return (dataSourceId, documentId,
            new AdapterLease(host, BusAdapters.RabbitMq, dataSourceId.ToString(), instance));
    }

    private sealed class AdapterLease(IResidentAdapterHost host, string adapterId, string instanceKey,
        ResidentAdapterInstance instance) : IAsyncDisposable
    {
        private readonly IResidentAdapterHost _host = host;

        public ResidentAdapterInstance Instance { get; } = instance;

        public ValueTask DisposeAsync() =>
            new(_host.StopAsync(adapterId, instanceKey, drain: false));
    }

    private InstanceHealth InstanceOf(int dataSourceId) =>
        fixture.App.Services.GetRequiredService<IResidentAdapterHost>()
            .Describe().FirstOrDefault(h => h.InstanceKey == dataSourceId.ToString());

    private async Task<int> CountAsync(int documentId)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        return await db.Set<Xchange>().AsNoTracking().CountAsync(x => x.DocumentId == documentId);
    }

    /// <summary>Publishes with full control over the message id, including leaving it unset.</summary>
    private void PublishRaw(string queue, string body, string messageId)
    {
        using var connection = ExternalConnection();
        using var channel = connection.CreateModel();

        channel.QueueDeclare(queue, durable: true, exclusive: false, autoDelete: false);

        var properties = channel.CreateBasicProperties();
        properties.ContentType = "application/json";
        properties.DeliveryMode = 2;
        if (messageId != null) properties.MessageId = messageId;

        channel.BasicPublish("", queue, properties, Encoding.UTF8.GetBytes(body));
    }

    private void PublishMany(string queue, int count)
    {
        using var connection = ExternalConnection();
        using var channel = connection.CreateModel();

        channel.QueueDeclare(queue, durable: true, exclusive: false, autoDelete: false);

        for (var i = 0; i < count; i++)
        {
            var properties = channel.CreateBasicProperties();
            properties.ContentType = "application/json";
            properties.DeliveryMode = 2;
            properties.MessageId = Guid.NewGuid().ToString("N");

            channel.BasicPublish("", queue, properties,
                Encoding.UTF8.GetBytes($$"""{"n":{{i}}}"""));
        }
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
        Password = fixture.ExternalRabbitPassword
    }.CreateConnection("rabbit-adapter-tests");

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
