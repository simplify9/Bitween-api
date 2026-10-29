using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using SW.Bus.RabbitMqExtensions;

namespace SW.Bitween.Resources.Ops;

/// <summary>One message in a dead-letter queue, as the browse shows it.</summary>
public record DeadLetterMessageView(
    int Position,
    string Body,
    bool BodyCut,
    string LastException,
    string CorrelationId,
    string RoutingKey,
    IReadOnlyList<string> ExceptionHistory);

/// <summary>
/// The messages the bus gave up on - each consumer's <c>.bad</c> queue - read without taking them
/// off, and moved back onto the consumer's own queue to be tried again.
/// </summary>
/// <remarks>
/// A queue name is only ever matched against the bus's own dead-letter list, never handed to the
/// broker as given, so neither call can be pointed at a queue Bitween doesn't own.
/// </remarks>
public class DeadLetterQueues(
    IBusDashboardDataService dashboard,
    IErrorQueueReader errorQueueReader,
    IConfiguration configuration,
    ILogger<DeadLetterQueues> logger)
{
    public const int MaxBrowse = 50;
    public const int MaxRequeue = 1000;
    private const int MaxBodyChars = 64 * 1024;

    /// <summary>The queue as the bus names it, or null when it isn't one of its dead-letter queues.</summary>
    public async Task<string> Resolve(string queueName)
    {
        if (string.IsNullOrWhiteSpace(queueName)) return null;
        var known = await dashboard.GetDeadLetterSummaryAsync();
        return known.Select(d => d.DeadLetterQueueName)
            .FirstOrDefault(q => string.Equals(q, queueName.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The first <paramref name="count"/> messages, oldest first, left where they are.</summary>
    public async Task<IReadOnlyList<DeadLetterMessageView>> Browse(string queue, int count)
    {
        var messages = await errorQueueReader.PeekByQueueName(queue, Math.Clamp(count, 1, MaxBrowse));
        return messages.Select((m, i) =>
        {
            var body = m.RawBody ?? "";
            return new DeadLetterMessageView(
                i + 1,
                body.Length > MaxBodyChars ? body[..MaxBodyChars] : body,
                body.Length > MaxBodyChars,
                m.LastException?.ToString(),
                m.CorrelationId,
                m.RoutingKey,
                (m.ExceptionHistory ?? []).Select(e => e?.ToString()).ToList());
        }).ToList();
    }

    /// <summary>
    /// Moves up to <paramref name="count"/> messages, oldest first, back onto the queue they failed
    /// on, with a fresh retry count. Each one leaves the dead-letter queue only once the broker has
    /// confirmed it is on the main queue, so a failure part-way loses nothing.
    /// </summary>
    /// <returns>How many were moved.</returns>
    public async Task<int> RequeueAsync(string queue, int count)
    {
        var main = Regex.Replace(queue, @"\.bad$", "", RegexOptions.IgnoreCase);
        var connectionString = configuration.GetConnectionString("RabbitMQ")
                               ?? throw new InvalidOperationException("No RabbitMQ connection string is configured.");
        var factory = new ConnectionFactory
        {
            Uri = new Uri(connectionString),
            AutomaticRecoveryEnabled = false,
            RequestedConnectionTimeout = TimeSpan.FromSeconds(15)
        };

        await using var connection = await factory.CreateConnectionAsync("bitween-dead-letter-requeue");
        // With confirmations tracked, a publish completes only once the broker has the message,
        // and throws when it can't route it.
        await using var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true));
        // Published straight to the queue: through its exchange, every other consumer of the same
        // message would get it again too. And a queue that isn't there would swallow it.
        await channel.QueueDeclarePassiveAsync(main);

        var moved = 0;
        while (moved < Math.Clamp(count, 1, MaxRequeue))
        {
            var message = await channel.BasicGetAsync(queue, autoAck: false);
            if (message is null) break;

            var properties = new BasicProperties(message.BasicProperties);
            if (properties.Headers is { } headers)
                properties.Headers = headers.Where(h => !IsRetryState(h.Key))
                    .ToDictionary(h => h.Key, h => h.Value);

            try
            {
                using var confirmed = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await channel.BasicPublishAsync("", main, mandatory: true, properties, message.Body, confirmed.Token);
            }
            catch (PublishException)
            {
                // Returned or refused: it stays where it was.
                await channel.BasicNackAsync(message.DeliveryTag, multiple: false, requeue: true);
                break;
            }

            await channel.BasicAckAsync(message.DeliveryTag, multiple: false);
            moved++;
        }

        logger.LogInformation("Moved {Count} dead-lettered messages from {Queue} back to {Main}", moved, queue, main);
        return moved;
    }

    /// <summary>What the bus counts retries and records failures with: left on, the message would fail straight back.</summary>
    private static bool IsRetryState(string header) =>
        header.StartsWith("x-death", StringComparison.OrdinalIgnoreCase)
        || header.StartsWith("x-first-death", StringComparison.OrdinalIgnoreCase)
        || header.StartsWith("x-last-death", StringComparison.OrdinalIgnoreCase)
        || Regex.IsMatch(header, @"^exception\d*$", RegexOptions.IgnoreCase);
}
