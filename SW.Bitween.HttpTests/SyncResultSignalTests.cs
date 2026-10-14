using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using SW.Bus;
using Xunit;

namespace SW.Bitween.HttpTests;

/// <summary>
/// A sync gateway call is answered when its exchange's result is saved, not on the next look at the
/// database: the worker rings the bell on its own node, or broadcasts to the node holding the call
/// (finding 2 of docs/design/processing-performance.md). The database is only the backstop, and it
/// first looks after two seconds, so an answer well under that came from the signal.
/// </summary>
[Collection("Http")]
public class SyncResultSignalTests(HttpFixture fixture)
{
    private static readonly TimeSpan Signalled = ResultWait.BackstopFirstCheck - TimeSpan.FromMilliseconds(300);

    [Fact]
    public async Task A_sync_call_is_answered_as_soon_as_its_result_is_saved()
    {
        var (urlName, key) = await GatewayWithKeyAsync();
        await Api.CallSyncAsync(fixture, urlName, key);

        for (var i = 0; i < 3; i++)
        {
            var clock = Stopwatch.StartNew();
            using var answer = await Api.CallSyncAsync(fixture, urlName, key);
            Assert.Equal(System.Net.HttpStatusCode.OK, answer.StatusCode);
            Assert.True(clock.Elapsed < Signalled, $"answered after {clock.Elapsed}");
        }
    }

    [Fact]
    public async Task A_sync_call_whose_exchange_ran_on_another_node_is_answered_by_its_broadcast()
    {
        await using var second = fixture.SecondNode();
        var (urlName, key) = await GatewayWithKeyAsync();
        // Both nodes have served a call, so both consume the lane.
        await Api.CallSyncAsync(fixture, urlName, key);
        await Api.CallSyncAsync(HttpFixture.ClientOf(second), urlName, key);

        // Everything broadcast to the nodes, as the nodes themselves receive it.
        var bus = fixture.App.Services.GetRequiredService<BusOptions>();
        await using var connection = await new ConnectionFactory { Uri = new Uri(fixture.RabbitConnectionString) }.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        var probe = (await channel.QueueDeclareAsync("", durable: false, exclusive: true, autoDelete: true)).QueueName;
        await channel.QueueBindAsync(probe, bus.NodeExchange, bus.NodeRoutingKey);
        var announced = 0;
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) =>
        {
            if (Encoding.UTF8.GetString(delivery.Body.Span).Contains(nameof(XchangeResultReady)))
                Interlocked.Increment(ref announced);
            return Task.CompletedTask;
        };
        await channel.BasicConsumeAsync(probe, autoAck: true, consumer);

        // All to the first node; the broker hands the lane's messages to both nodes' consumers in turn.
        for (var i = 0; i < 10; i++)
        {
            var clock = Stopwatch.StartNew();
            using var answer = await Api.CallSyncAsync(fixture, urlName, key);
            Assert.Equal(System.Net.HttpStatusCode.OK, answer.StatusCode);
            Assert.True(clock.Elapsed < Signalled, $"call {i} answered after {clock.Elapsed}");
        }

        await Task.Delay(300);
        Assert.True(announced > 0, "no exchange ran on the second node, so nothing was broadcast");
    }

    private async Task<(string UrlName, string Key)> GatewayWithKeyAsync()
    {
        var (admin, urlName, _, partnerId, _) = await Api.GatewayAsync(fixture);
        var key = await Api.NewKeyAsync(admin);
        await Api.SetKeysAsync(admin, partnerId, ("main", key));
        return (urlName, key);
    }
}
