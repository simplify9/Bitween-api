using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using SW.Bus;
using System.Net.Http.Json;
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
        using var partner = new EchoPartner();
        var (urlName, key) = await GatewayWithKeyAsync(partner.Url);
        // Both nodes have served a call, so both consume the lane.
        await Api.CallSyncAsync(fixture, urlName, key);
        await Api.CallSyncAsync(HttpFixture.ClientOf(second), urlName, key);

        // Everything broadcast to the nodes, as the nodes themselves receive it.
        var bus = fixture.App.Services.GetRequiredService<BusOptions>();
        await using var connection = await new ConnectionFactory { Uri = new Uri(fixture.RabbitConnectionString) }.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        var probe = (await channel.QueueDeclareAsync("", durable: false, exclusive: true, autoDelete: true)).QueueName;
        await channel.QueueBindAsync(probe, bus.NodeExchange, bus.NodeRoutingKey);
        var announced = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) =>
        {
            var body = Encoding.UTF8.GetString(delivery.Body.Span);
            if (body.Contains(nameof(XchangeResultReady))) announced.Enqueue(body);
            return Task.CompletedTask;
        };
        await channel.BasicConsumeAsync(probe, autoAck: true, consumer);

        // All to the first node; the broker hands the lane's messages to both nodes' consumers in turn.
        for (var i = 0; i < 10; i++)
        {
            var clock = Stopwatch.StartNew();
            using var answer = await Api.CallSyncAsync(fixture, urlName, key, $"{{\"call\":{i}}}");
            Assert.Equal(System.Net.HttpStatusCode.OK, answer.StatusCode);
            Assert.True(clock.Elapsed < Signalled, $"call {i} answered after {clock.Elapsed}");
            // The partner's answer, whichever node ran the exchange: from the broadcast, not storage.
            Assert.Equal($"{{\"call\":{i}}}", await answer.Content.ReadAsStringAsync());
        }

        await Task.Delay(300);
        Assert.False(announced.IsEmpty, "no exchange ran on the second node, so nothing was broadcast");
        // The response travels in the broadcast (escaped once more inside the bus envelope).
        Assert.All(announced, body => Assert.Contains("call", body));
    }

    /// <summary>A gateway and a partner key; with <paramref name="partnerUrl"/>, its subscription posts there and answers with what it gets back.</summary>
    private async Task<(string UrlName, string Key)> GatewayWithKeyAsync(string? partnerUrl = null)
    {
        var (admin, urlName, _, partnerId, subscriptionId) = await Api.GatewayAsync(fixture);
        var key = await Api.NewKeyAsync(admin);
        await Api.SetKeysAsync(admin, partnerId, ("main", key));
        if (partnerUrl != null)
        {
            var subscription = (await Api.Json(await admin.GetAsync($"/api/subscriptions/{subscriptionId}"))).AsObject();
            subscription["handlerId"] = "NativeHttpHandler";
            subscription["handlerProperties"] = new System.Text.Json.Nodes.JsonArray(
                new System.Text.Json.Nodes.JsonObject { ["key"] = "Url", ["value"] = partnerUrl },
                new System.Text.Json.Nodes.JsonObject { ["key"] = "Verb", ["value"] = "post" },
                new System.Text.Json.Nodes.JsonObject { ["key"] = "ContentType", ["value"] = "application/json" });
            await Api.Json(await admin.PostAsJsonAsync($"/api/subscriptions/{subscriptionId}", subscription));
        }

        return (urlName, key);
    }

    /// <summary>A partner on the loopback address that answers every post with its body.</summary>
    private sealed class EchoPartner : IDisposable
    {
        private readonly System.Net.HttpListener listener = new();

        public EchoPartner()
        {
            var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            probe.Start();
            var port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            Url = $"http://127.0.0.1:{port}/orders";
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Start();
            _ = Task.Run(ServeAsync);
        }

        public string Url { get; }

        private async Task ServeAsync()
        {
            while (listener.IsListening)
            {
                System.Net.HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync();
                }
                catch
                {
                    return;
                }

                using var reader = new System.IO.StreamReader(context.Request.InputStream);
                var body = Encoding.UTF8.GetBytes(await reader.ReadToEndAsync());
                context.Response.ContentType = "application/json";
                await context.Response.OutputStream.WriteAsync(body);
                context.Response.Close();
            }
        }

        public void Dispose() => listener.Close();
    }
}
