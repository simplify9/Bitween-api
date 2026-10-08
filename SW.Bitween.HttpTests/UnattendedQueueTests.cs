using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Xunit;

namespace SW.Bitween.HttpTests;

/// <summary>
/// Queue health's clean-up against the real broker and its management API: a lane no consumer
/// definition declares is listed with what it holds and can be deleted whole; a queue Bitween still
/// consumes, or one something is reading, never is — whatever name the caller sends.
/// </summary>
[Collection("Http")]
public class UnattendedQueueTests(HttpFixture fixture)
{
    const string Prefix = "v3.development.bitween";

    [Fact]
    public async Task Only_a_lane_nothing_reads_is_deleted_and_all_three_of_its_queues_go()
    {
        var orphan = $"{Prefix}.busservice.orphan{Guid.NewGuid():N}"[..60];
        var held = $"{Prefix}.busservice.held{Guid.NewGuid():N}"[..60];
        var attended = $"{Prefix}.xchangeservice.0ungrouped";

        using var connection = new ConnectionFactory { Uri = new Uri(fixture.RabbitConnectionString) }.CreateConnection();
        using var channel = connection.CreateModel();
        foreach (var queue in new[] { orphan, $"{orphan}.retry", $"{orphan}.bad", held })
            channel.QueueDeclare(queue, durable: true, exclusive: false, autoDelete: false);
        channel.BasicPublish("", orphan, null, Encoding.UTF8.GetBytes("left behind 1"));
        channel.BasicPublish("", orphan, null, Encoding.UTF8.GetBytes("left behind 2"));
        channel.BasicPublish("", $"{orphan}.bad", null, Encoding.UTF8.GetBytes("dead"));
        // Something outside Bitween still reads this one.
        using var reader = connection.CreateModel();
        reader.BasicConsume(held, autoAck: false, new EventingBasicConsumer(reader));

        using var admin = await fixture.AdminAsync();

        // The management API's figures lag the broker by a few seconds.
        JsonObject listed = null!;
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(30))
        {
            var lanes = (await Api.Json(await admin.GetAsync("/api/ops/unattendedqueues"))).AsArray();
            listed = lanes.FirstOrDefault(l => (string?)l!["queueName"] == orphan)?.AsObject()!;
            if (listed != null && (long)listed["messages"]! == 2 && (long)listed["deadMessages"]! == 1) break;
            await Task.Delay(1000);
        }
        Assert.NotNull(listed);
        Assert.Equal(2, (long)listed["messages"]!);
        Assert.Equal(1, (long)listed["deadMessages"]!);
        Assert.Equal(3, (long)listed["queues"]!);

        var result = await Api.Json(await admin.PostAsJsonAsync("/api/ops/deleteunattendedqueues",
            new { queueNames = new[] { orphan, attended, held } }));

        Assert.Equal([orphan], result["deleted"]!.AsArray().Select(n => (string)n!).ToArray());
        var skipped = result["skipped"]!.AsArray().ToDictionary(s => (string)s!["queueName"]!, s => (string)s!["reason"]!);
        Assert.Contains("isn't an unattended queue", skipped[attended]);
        Assert.Contains("still reading", skipped[held]);

        var queues = await fixture.QueuesAsync();
        Assert.DoesNotContain(queues, q => q.StartsWith(orphan));
        Assert.Contains(attended, queues);
        Assert.Contains(held, queues);
    }
}
