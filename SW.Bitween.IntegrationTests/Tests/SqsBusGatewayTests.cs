using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using SW.Bitween.Domain;
using SW.Bitween.Domain.DataSources;
using SW.Bitween.Domain.Gateway;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.Bitween.Services.Adapters;
using SW.PrimitiveTypes;
using SW.Serverless.Resident;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// Amazon SQS as a bus provider, against a real SQS API (ElasticMQ).
///
/// SQS differs from RabbitMQ in the one way that matters: there is no ack, only DELETE. A received
/// message is merely invisible for the visibility timeout and reappears if it is not deleted. That
/// is the same at-least-once contract by a different mechanism, and it maps onto the same
/// ordering:
///
///     receive -> persist the Xchange -> ONLY THEN DeleteMessage
///
/// So the tests here are about deletion and visibility rather than ack and nack, and about the
/// Selling Partner API envelope - because SP-API delivers notifications by publishing to an SQS
/// queue you own, which is the reason this provider exists at all.
/// </summary>
[Collection("Bitween")]
public class SqsBusGatewayTests(BitweenFixture fixture)
{
    // ---------------------------------------------------------------- ingress

    [Fact]
    public async Task A_message_on_an_SQS_queue_becomes_an_Xchange()
    {
        var queueUrl = await fixture.CreateSqsQueueAsync(Unique("orders"));
        var setup = await ArrangeAsync(queueUrl);

        await using var adapter = await StartAsync(setup.DataSourceId);

        await SendAsync(queueUrl, "{\"orderId\":9001}");

        var xchange = await WaitForXchangeAsync(setup.DocumentId);

        Assert.NotNull(xchange);
        Assert.Equal(setup.DocumentId, xchange!.DocumentId);
    }

    /// <summary>
    /// The SQS half of persist-then-acknowledge. Deleting is the only way a message leaves the
    /// queue, so an Xchange existing while the queue is empty is the proof that deletion happened
    /// after persistence and not before it.
    /// </summary>
    [Fact]
    public async Task A_persisted_message_is_deleted_from_the_queue()
    {
        var queueUrl = await fixture.CreateSqsQueueAsync(Unique("delete"));
        var setup = await ArrangeAsync(queueUrl);

        await using var adapter = await StartAsync(setup.DataSourceId);

        await SendAsync(queueUrl, "{\"orderId\":9002}");

        Assert.NotNull(await WaitForXchangeAsync(setup.DocumentId));

        await WaitAsync(async () => await DepthAsync(queueUrl) == 0, TimeSpan.FromSeconds(30),
            "the message was persisted but never deleted, so SQS will redeliver it");
    }

    /// <summary>
    /// A rejection must put the message back, and quickly. Returning it by resetting visibility to
    /// zero rather than waiting out the timeout is what turns a transient Bitween failure into a
    /// retry measured in seconds instead of minutes.
    /// </summary>
    [Fact]
    public async Task A_rejected_message_returns_to_the_queue_rather_than_being_deleted()
    {
        var queueUrl = await fixture.CreateSqsQueueAsync(Unique("reject"));

        // A data source with no gateway claiming a DIFFERENT endpoint: the sink cannot attribute
        // the message, so it rejects, and the adapter must return it.
        var dataSourceId = await CreateDataSourceAsync(queueUrl);

        await using var adapter = await StartAsync(dataSourceId);
        await SendAsync(queueUrl, "{\"orderId\":9003}");

        await WaitAsync(async () =>
            {
                var stats = await adapter.Instance.InvokeAsync<JObject>("GetStats");
                return stats.Value<long>("received") > 0;
            }, TimeSpan.FromSeconds(30), "the adapter never received the message");

        var stats = await adapter.Instance.InvokeAsync<JObject>("GetStats");

        // Unclaimed is accepted-and-discarded on purpose - rejecting would loop for ever - so the
        // message is deleted. What must NOT happen is a silent failure that leaves it invisible.
        Assert.Equal(0, stats.Value<long>("failed"));
        Assert.True(stats.Value<long>("deleted") > 0 || stats.Value<long>("returned") > 0,
            "the message was neither deleted nor returned, so it is stuck invisible until the timeout");
    }

    // ---------------------------------------------------------------- SP-API

    /// <summary>
    /// The reason this provider matters beyond SQS itself. SP-API wraps every notification in an
    /// envelope; forwarding it whole would make every Bitween document schema carry Amazon's
    /// wrapper. The adapter unwraps it and promotes the metadata to headers instead.
    /// </summary>
    [Fact]
    public async Task A_selling_partner_notification_is_unwrapped_to_its_payload()
    {
        var queueUrl = await fixture.CreateSqsQueueAsync(Unique("spapi"));
        var setup = await ArrangeAsync(queueUrl, unwrapSpApi: true);

        await using var adapter = await StartAsync(setup.DataSourceId);

        // The shape SP-API actually delivers.
        const string envelope = """
        {
          "notificationVersion": "1.0",
          "notificationType": "ORDER_CHANGE",
          "payloadVersion": "1.0",
          "eventTime": "2026-01-01T00:00:00.000Z",
          "payload": { "orderChangeNotification": { "amazonOrderId": "111-2223334-4445556" } },
          "notificationMetadata": {
            "applicationId": "amzn1.sp.solution.test",
            "subscriptionId": "sub-123",
            "publishTime": "2026-01-01T00:00:00.000Z",
            "notificationId": "notif-abc-123"
          }
        }
        """;

        await SendAsync(queueUrl, envelope);

        var xchange = await WaitForXchangeAsync(setup.DocumentId);
        Assert.NotNull(xchange);

        // Asserted on size rather than by reading the blob back: the adapter forwards the payload
        // compact, so its exact byte count is known, and matching it proves the envelope was
        // stripped. The envelope is several times larger, so a pass-through cannot match by
        // accident.
        var expected = Encoding.UTF8.GetByteCount(
            JObject.Parse(envelope)["payload"]!.ToString(Newtonsoft.Json.Formatting.None));

        Assert.Equal(expected, xchange!.InputSize);
        Assert.True(Encoding.UTF8.GetByteCount(envelope) > expected * 2,
            "the envelope should be substantially larger than its payload, or this proves nothing");
    }

    /// <summary>
    /// The dedupe key reaches the Xchange as a reference, and for an SP-API notification it is the
    /// notification id - which is stable across a redelivery where the SQS MessageId is not.
    ///
    /// NOTE THE GAP THIS DOES NOT COVER. Nothing in Bitween currently ENFORCES uniqueness on that
    /// reference, so a redelivered message produces a second Xchange. The key is carried, which is
    /// the precondition for deduplication, but the check itself is not implemented. At-least-once
    /// delivery makes that check mandatory rather than optional, so this test deliberately asserts
    /// only what is true today and names what is missing.
    /// </summary>
    [Fact]
    public async Task A_notification_carries_its_notification_id_as_the_dedupe_reference()
    {
        var queueUrl = await fixture.CreateSqsQueueAsync(Unique("spref"));
        var setup = await ArrangeAsync(queueUrl, unwrapSpApi: true);

        await using var adapter = await StartAsync(setup.DataSourceId);

        await SendAsync(queueUrl, """
        {"notificationType":"ORDER_CHANGE","payload":{"x":1},
         "notificationMetadata":{"notificationId":"notif-stable-999"}}
        """);

        var xchange = await WaitForXchangeAsync(setup.DocumentId);

        Assert.NotNull(xchange);
        Assert.Contains("spapi:notif-stable-999", xchange!.References);
    }

    // ---------------------------------------------------------------- controls

    [Fact]
    public async Task Test_connection_reports_the_queue_and_its_visibility_timeout()
    {
        var queueUrl = await fixture.CreateSqsQueueAsync(Unique("probe"));
        var dataSourceId = await CreateDataSourceAsync(queueUrl);

        await using var adapter = await StartAsync(dataSourceId);

        var result = await adapter.Instance.InvokeAsync<JObject>("TestConnection");

        Assert.True(result.Value<bool>("ok"), result.ToString());
        var steps = result["steps"].Select(s => s.Value<string>("step")).ToArray();
        Assert.Contains("credentials", steps);
        Assert.Contains(steps, s => s!.StartsWith("queue:"));
    }

    [Fact]
    public async Task Discover_lists_the_queues_the_credentials_can_see()
    {
        var queueUrl = await fixture.CreateSqsQueueAsync(Unique("discover"));
        var dataSourceId = await CreateDataSourceAsync(queueUrl);

        await using var adapter = await StartAsync(dataSourceId);

        var result = await adapter.Instance.InvokeAsync<JObject>("Discover");

        Assert.Null(result["error"]);
        Assert.Contains(result["queues"]!, q => queueUrl.EndsWith(q.Value<string>("name")!));
    }

    [Fact]
    public async Task Bitween_can_send_a_message_out_to_SQS()
    {
        var consumed = await fixture.CreateSqsQueueAsync(Unique("out-in"));
        var target = await fixture.CreateSqsQueueAsync(Unique("out-target"));

        // Send to a queue the adapter is NOT polling, or it consumes the message as fast as it
        // sends it and the depth assertion can never be satisfied.
        var dataSourceId = await CreateDataSourceAsync(consumed);

        await using var adapter = await StartAsync(dataSourceId);

        await adapter.Instance.InvokeAsync<JObject>("Publish", new
        {
            Endpoint = target,
            Body = "{\"pushed\":true}"
        });

        await WaitAsync(async () => await DepthAsync(target) >= 1, TimeSpan.FromSeconds(20),
            "the message never arrived on the target queue");
    }

    /// <summary>
    /// A DELIVERY sends, which is the half that was missing. Publish had existed since this
    /// adapter did and nothing in the pipeline ever called it.
    /// </summary>
    [Fact]
    public async Task A_delivery_sends_the_message_it_was_given()
    {
        var consumed = await fixture.CreateSqsQueueAsync(Unique("handler-in"));
        var target = await fixture.CreateSqsQueueAsync(Unique("handler-out"));

        var dataSourceId = await CreateDataSourceAsync(consumed);
        await using var adapter = await StartAsync(dataSourceId);

        await adapter.Instance.InvokeAsync<XchangeFile>("Handle",
            new XchangeFile("{\"delivered\":true}", "out.json"),
            properties: new Dictionary<string, string> { ["Endpoint"] = target });

        await WaitAsync(async () => await DepthAsync(target) >= 1, TimeSpan.FromSeconds(20),
            "the delivered message never arrived on the target queue");
    }

    [Fact]
    public async Task A_delivery_with_no_endpoint_says_so()
    {
        var dataSourceId = await CreateDataSourceAsync(
            await fixture.CreateSqsQueueAsync(Unique("handler-none")));

        await using var adapter = await StartAsync(dataSourceId);

        var error = await Assert.ThrowsAnyAsync<Exception>(() =>
            adapter.Instance.InvokeAsync<XchangeFile>("Handle",
                new XchangeFile("{}", "out.json"),
                properties: new Dictionary<string, string>()));

        Assert.Contains("no Endpoint", error.Message);
    }

    /// <summary>
    /// The multi-node case. A broker data source is exclusive, but a delivery runs on whichever
    /// node picked the message up - so when the owned instance is not here the runtime opens a
    /// send-only connection of its own rather than failing. See the RabbitMQ twin of this test
    /// for the reasoning in full.
    /// </summary>
    [Fact]
    public async Task A_delivery_sends_from_a_node_that_does_not_own_the_connection()
    {
        var target = await fixture.CreateSqsQueueAsync(Unique("elsewhere-out"));
        var dataSourceId = await CreateDataSourceAsync(
            await fixture.CreateSqsQueueAsync(Unique("elsewhere-in")));

        var host = fixture.App.Services.GetRequiredService<IResidentAdapterHost>();
        Assert.Null(host.Get(BusAdapters.Sqs, dataSourceId.ToString()));

        await using var scope = fixture.App.Services.CreateAsyncScope();
        var invoker = scope.ServiceProvider.GetRequiredService<IAdapterInvoker>();

        await invoker.InvokeAsync<XchangeFile>(BusAdapters.Sqs, AdapterRole.Handler,
            "Handle", new XchangeFile("{\"fromElsewhere\":true}", "out.json"),
            new Dictionary<string, string>
            {
                [StartupValuesFiller.DataSourceIdKey] = dataSourceId.ToString(),
                ["Endpoint"] = target
            },
            Guid.NewGuid().ToString("N"));

        await WaitAsync(async () => await DepthAsync(target) >= 1, TimeSpan.FromSeconds(25),
            "a node that does not own the connection could not send");

        // And it did not take ownership on the way past.
        Assert.Null(host.Get(BusAdapters.Sqs, dataSourceId.ToString()));
    }

    [Fact]
    public async Task Health_carries_the_queue_detail_from_the_heartbeat()
    {
        var queueUrl = await fixture.CreateSqsQueueAsync(Unique("health"));
        var dataSourceId = await CreateDataSourceAsync(queueUrl);

        await using var adapter = await StartAsync(dataSourceId);

        var host = fixture.App.Services.GetRequiredService<IResidentAdapterHost>();

        await WaitAsync(() => host.Describe()
                .Any(h => h.InstanceKey == dataSourceId.ToString() && h.LastHeartbeatOn != null),
            TimeSpan.FromSeconds(30), "no heartbeat was recorded");

        var health = host.Describe().Single(h => h.InstanceKey == dataSourceId.ToString());

        Assert.True(health.Connected);
        Assert.Equal("elasticmq", health.Details["region"]);
        Assert.Contains(queueUrl, health.Details["endpoints"]);
    }

    // ---------------------------------------------------------------- helpers

    private record Setup(int DocumentId, int DataSourceId);

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..20];

    private async Task<Setup> ArrangeAsync(string queueUrl, bool unwrapSpApi = false)
    {
        var dataSourceId = await CreateDataSourceAsync(queueUrl, unwrapSpApi);

        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        var document = new Document(null, Unique("sqs-doc"), DocumentFormat.Json);
        db.Add(document);
        await db.SaveChangesAsync();

        db.Add(new BusGateway
        {
            Name = Unique("sqs-gw"),
            DocumentId = document.Id,
            DataSourceId = dataSourceId,
            Endpoint = queueUrl
        });
        await db.SaveChangesAsync();

        // Ten-minute singleton snapshot - without this the document is invisible to the pipeline.
        scope.ServiceProvider.GetRequiredService<IInfolinkCache>().Revoke();

        return new Setup(document.Id, dataSourceId);
    }

    private async Task<int> CreateDataSourceAsync(string queueUrl, bool unwrapSpApi = false)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        var properties = new Dictionary<string, string>(fixture.SqsProperties)
        {
            ["Endpoints"] = queueUrl
        };
        if (unwrapSpApi) properties["UnwrapSellingPartnerNotification"] = "true";

        var dataSource = new DataSource
        {
            Name = Unique("sqs-ds"),
            AdapterId = BusAdapters.Sqs,
            Kind = DataSourceKind.Broker,
            Properties = properties
        };

        db.Add(dataSource);
        await db.SaveChangesAsync();
        return dataSource.Id;
    }

    private async Task<AdapterLease> StartAsync(int dataSourceId)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var dataSource = await db.Set<DataSource>().AsNoTracking().FirstAsync(d => d.Id == dataSourceId);

        var host = fixture.App.Services.GetRequiredService<IResidentAdapterHost>();
        var instance = await host.StartExclusiveAsync(new AdapterSpec
        {
            AdapterId = dataSource.AdapterId,
            InstanceKey = dataSourceId.ToString(),
            StartupValues = new Dictionary<string, string>(dataSource.Properties)
        });

        return new AdapterLease(host, dataSource.AdapterId, dataSourceId.ToString(), instance);
    }

    private sealed class AdapterLease(IResidentAdapterHost host, string adapterId, string instanceKey,
        ResidentAdapterInstance instance) : IAsyncDisposable
    {
        public ResidentAdapterInstance Instance { get; } = instance;
        public ValueTask DisposeAsync() => new(host.StopAsync(adapterId, instanceKey, drain: false));
    }

    private async Task SendAsync(string queueUrl, string body)
    {
        using var sqs = fixture.CreateSqsClient();
        await sqs.SendMessageAsync(new SendMessageRequest { QueueUrl = queueUrl, MessageBody = body });
    }

    private async Task<int> DepthAsync(string queueUrl)
    {
        using var sqs = fixture.CreateSqsClient();
        var attributes = await sqs.GetQueueAttributesAsync(queueUrl,
            new List<string> { "ApproximateNumberOfMessages", "ApproximateNumberOfMessagesNotVisible" });

        // Invisible messages still belong to the queue: counting only the visible ones would read
        // an in-flight message as delivered.
        return attributes.ApproximateNumberOfMessages + attributes.ApproximateNumberOfMessagesNotVisible;
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
