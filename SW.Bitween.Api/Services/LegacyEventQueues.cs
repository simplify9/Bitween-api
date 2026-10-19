using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SW.Bitween.Domain;

namespace SW.Bitween.Services;

/// <summary>
/// The five queues named after exchange events, which a Bitween before work groups (January 2026)
/// published to. Nothing publishes to them now, but an exchange waiting in one when its Bitween was
/// upgraded runs only if it is read. Each start looks for them: one with something in it is read
/// until it's empty, and an empty one nobody reads is removed. A setting used to switch this on, and
/// an upgrade that didn't know about it left those exchanges behind.
/// </summary>
public class LegacyEventQueues
{
    public static readonly string[] Names =
    [
        nameof(ApiXchangeCreatedEvent), nameof(InternalXchangeCreatedEvent), nameof(ReceivingXchangeCreatedEvent),
        nameof(AggregateXchangeCreatedEvent), nameof(XchangeResultCreatedEvent)
    ];

    private readonly List<string> _toDrain = [];

    /// <summary>The ones <see cref="XchangeService"/> reads from on this start.</summary>
    public IReadOnlyList<string> ToDrain => _toDrain;

    /// <summary>Looks for them, before the bus starts and would create any it was told to read.</summary>
    public async Task FindAsync(BrokerQueues broker, string environment, string queuePrefix, ILogger logger)
    {
        foreach (var name in Names)
        {
            var queue = BrokerQueues.QueueName(environment, queuePrefix, nameof(XchangeService), name);
            var main = await broker.InspectAsync(queue);
            if (main is null) continue;

            // A failed message waits out its delay in the retry queue, and then goes back to the main one.
            var retry = await broker.InspectAsync($"{queue}.retry");
            if (main.MessageCount > 0 || main.ConsumerCount > 0 || retry?.MessageCount > 0)
            {
                _toDrain.Add(name);
                logger.LogInformation("Reading {Queue}, left by a version of Bitween before work groups, until it is empty.",
                    queue);
                continue;
            }

            await broker.DeleteIfIdleAsync(queue);
            if (retry is not null) await broker.DeleteIfIdleAsync($"{queue}.retry");
            // What failed for good stays where the queue's dead letters can still be looked at.
            if (await broker.InspectAsync($"{queue}.bad") is { MessageCount: 0 })
                await broker.DeleteIfIdleAsync($"{queue}.bad");
            logger.LogInformation("Removed {Queue}, left empty by a version of Bitween before work groups.", queue);
        }
    }
}

public static class LegacyEventQueuesHostExtensions
{
    /// <summary>
    /// Runs just before the bus starts. An unreachable broker is looked at again on the next start;
    /// nothing in those queues goes anywhere meanwhile.
    /// </summary>
    public static IHost FindLegacyEventQueues(this IHost host)
    {
        using var scope = host.Services.CreateScope();
        var services = scope.ServiceProvider;
        var logger = services.GetRequiredService<ILogger<LegacyEventQueues>>();
        try
        {
            FindAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not reach RabbitMQ to look for queues left by a version of Bitween " +
                                  "before work groups; they are looked for again on the next start.");
        }

        return host;

        async Task FindAsync()
        {
            await using var broker = await BrokerQueues.ConnectAsync(
                services.GetRequiredService<IConfiguration>().GetConnectionString("RabbitMQ"));
            await services.GetRequiredService<LegacyEventQueues>().FindAsync(broker,
                services.GetRequiredService<IHostEnvironment>().EnvironmentName,
                services.GetRequiredService<BitweenOptions>().QueuePrefix, logger);
        }
    }
}
