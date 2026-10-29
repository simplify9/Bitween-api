using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Services;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// The queues a Bitween before work groups published to are read while they hold something and
/// removed once they don't, with nothing to switch on.
/// </summary>
[Collection("Bitween")]
public class LegacyEventQueuesTests(BitweenFixture fixture)
{
    private const string Environment = "Staging";
    private readonly string _queuePrefix = $"legacy-{Guid.NewGuid():N}";

    [Fact]
    public async Task A_queue_with_exchanges_waiting_is_read()
    {
        var queue = await DeclareAsync("ApiXchangeCreatedEvent", messages: 1);

        var legacy = await FindAsync();

        Assert.Equal(["ApiXchangeCreatedEvent"], legacy.ToDrain);
        Assert.NotNull(await InspectAsync(queue));
    }

    [Fact]
    public async Task An_empty_queue_is_removed_but_its_dead_letters_stay()
    {
        var queue = await DeclareAsync("InternalXchangeCreatedEvent", messages: 0);
        await DeclareQueueAsync($"{queue}.retry", messages: 0);
        await DeclareQueueAsync($"{queue}.bad", messages: 1);

        var legacy = await FindAsync();

        Assert.Empty(legacy.ToDrain);
        Assert.Null(await InspectAsync(queue));
        Assert.Null(await InspectAsync($"{queue}.retry"));
        Assert.NotNull(await InspectAsync($"{queue}.bad"));
    }

    [Fact]
    public async Task A_queue_whose_messages_wait_to_be_retried_is_read()
    {
        var queue = await DeclareAsync("XchangeResultCreatedEvent", messages: 0);
        await DeclareQueueAsync($"{queue}.retry", messages: 1);

        Assert.Equal(["XchangeResultCreatedEvent"], (await FindAsync()).ToDrain);
    }

    [Fact]
    public async Task Without_them_nothing_is_read_or_created()
    {
        Assert.Empty((await FindAsync()).ToDrain);
        Assert.Null(await InspectAsync(Name("ApiXchangeCreatedEvent")));
    }

    private async Task<LegacyEventQueues> FindAsync()
    {
        var legacy = new LegacyEventQueues();
        await using var broker = await BrokerQueues.ConnectAsync(ConnectionString);
        await legacy.FindAsync(broker, Environment, _queuePrefix, NullLogger.Instance);
        return legacy;
    }

    private async Task<QueueDeclareOk?> InspectAsync(string queue)
    {
        await using var broker = await BrokerQueues.ConnectAsync(ConnectionString);
        return await broker.InspectAsync(queue);
    }

    private async Task<string> DeclareAsync(string message, uint messages)
    {
        var queue = Name(message);
        await DeclareQueueAsync(queue, messages);
        return queue;
    }

    private async Task DeclareQueueAsync(string queue, uint messages)
    {
        await using var connection = await new ConnectionFactory { Uri = new Uri(ConnectionString) }.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false);
        for (var i = 0; i < messages; i++)
            await channel.BasicPublishAsync("", queue, "left behind"u8.ToArray());
    }

    private string Name(string message) => BrokerQueues.QueueName(Environment, _queuePrefix, "XchangeService", message);

    private string ConnectionString =>
        fixture.App.Services.GetRequiredService<IConfiguration>().GetConnectionString("RabbitMQ")!;
}
