using System;
using System.Threading.Tasks;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using SW.Bus;

namespace SW.Bitween.Services;

/// <summary>
/// Bitween's own queues on RabbitMQ, looked at before the bus starts: whether one exists, what is in
/// it, and removing one nothing uses any more. The bus itself only ever declares, which would create
/// the very queue being asked about.
/// </summary>
public sealed class BrokerQueues : IAsyncDisposable
{
    private readonly IConnection _connection;

    private BrokerQueues(IConnection connection) => _connection = connection;

    public static async Task<BrokerQueues> ConnectAsync(string connectionString)
    {
        var factory = new ConnectionFactory
        {
            Uri = new Uri(connectionString),
            AutomaticRecoveryEnabled = false,
            RequestedConnectionTimeout = TimeSpan.FromSeconds(15)
        };
        return new BrokerQueues(await factory.CreateConnectionAsync("bitween-startup"));
    }

    /// <summary>
    /// The name the bus gives a consumer's queue: <c>v3.{environment}.{queue prefix}.{consumer}.{message}</c>.
    /// </summary>
    public static string QueueName(string environment, string queuePrefix, string consumer, string message) =>
        $"{new BusOptions(environment).ProcessExchange}.{queuePrefix}.{consumer}.{message}".ToLowerInvariant();

    /// <returns>The queue's message and consumer counts, or null when there is no such queue.</returns>
    public async Task<QueueDeclareOk> InspectAsync(string queue)
    {
        // Asking about a missing queue closes the channel it was asked on, so each question gets its own.
        await using var channel = await _connection.CreateChannelAsync();
        try
        {
            return await channel.QueueDeclarePassiveAsync(queue);
        }
        catch (OperationInterruptedException ex) when (ex.ShutdownReason?.ReplyCode == 404)
        {
            return null;
        }
    }

    /// <summary>Deletes the queue only while it is empty and nobody consumes it.</summary>
    /// <returns>Whether it was deleted.</returns>
    public async Task<bool> DeleteIfIdleAsync(string queue)
    {
        await using var channel = await _connection.CreateChannelAsync();
        try
        {
            await channel.QueueDeleteAsync(queue, ifUnused: true, ifEmpty: true);
            return true;
        }
        catch (OperationInterruptedException)
        {
            // Not empty, or consumed, by now: it waits for the next start.
            return false;
        }
    }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}
