using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Domain;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.HttpTests;

/// <summary>
/// Messages through the real broker to the real consumers. The app here starts its bus consumers as
/// production does, so nothing reaches a consumer except through RabbitMQ.
/// </summary>
[Collection("Http")]
public class QueueTests(HttpFixture fixture)
{
    /// <summary>
    /// The gateway only queues the exchange and then waits for its result, so an answer of 200 means
    /// the consumer took the message off the queue and processed it.
    /// </summary>
    [Fact]
    public async Task A_gateway_call_is_consumed_from_the_queue_and_answered_with_its_result()
    {
        var (admin, urlName, _, partnerId, _) = await Api.GatewayAsync(fixture);
        var key = await Api.NewKeyAsync(admin);
        await Api.SetKeysAsync(admin, partnerId, ("main", key));
        admin.Dispose();

        using var partner = fixture.Client();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/gateway/{urlName}/sync")
        {
            Content = new StringContent("{\"order\":42}", Encoding.UTF8, "application/json")
        };
        request.Headers.Add("partnerkey", key);
        request.Headers.Add("Wait-Period", "30");
        var response = await partner.SendAsync(request);

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {body}");
        Assert.Matches("^\"?[0-9a-f]{32}\"?$", body);
    }

    /// <summary>
    /// A message the consumer cannot read is retried through the retry queue and then parked in the
    /// bad queue — not lost, and not retried for ever.
    /// </summary>
    [Fact(Skip = "SimplyWorks.Bus 8.1.0 to 10.0.1 drop such a message instead of parking it: the ack comes " +
                 "before the bad-queue publish, which always throws. Fixed in SW-Bus fix/dead-letter-lost; " +
                 "unskip once Bitween is on the release carrying it.")]
    public async Task A_message_the_consumer_cannot_process_is_retried_and_then_dead_lettered()
    {
        var lane = WorkGroup.None.GetBusMessageName();
        var bad = $"v3.development.bitween.xchangeservice.{lane.ToLowerInvariant()}.bad";
        var before = await DepthAsync(bad);

        await using (var scope = fixture.App.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IPublish>().Publish(lane, "this is not an exchange message");

        var clock = Stopwatch.StartNew();
        var depth = before;
        while (depth == before && clock.Elapsed < TimeSpan.FromSeconds(30))
        {
            await Task.Delay(500);
            depth = await DepthAsync(bad);
        }

        Assert.Equal(before + 1, depth);
        // Retried before it was parked: RetryCount retries a second apart.
        Assert.True(clock.Elapsed >= TimeSpan.FromSeconds(HttpFixture.RetryCount - 0.5),
            $"dead-lettered after {clock.ElapsedMilliseconds} ms, too soon to have been retried");
    }

    async Task<int> DepthAsync(string queue)
    {
        var lines = (await fixture.RabbitCtlAsync("list_queues", "name", "messages")).Split('\n');
        var row = lines.Select(l => l.Split('\t')).FirstOrDefault(c => c.Length == 2 && c[0] == queue);
        Assert.True(row != null, $"no queue {queue}: {string.Join(", ", lines)}");
        return int.Parse(row![1].Trim());
    }
}
