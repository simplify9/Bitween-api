using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using SW.Bitween.Domain.Cluster;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SW.Bitween.Services.Cluster;

/// <summary>
/// Leader election over the internal RabbitMQ, using the one primitive it already gives us:
/// an EXCLUSIVE QUEUE IS TIED TO A SINGLE CONNECTION. Declaring it succeeds for exactly one node
/// and fails for every other, and the broker releases it the moment that connection goes - which
/// is liveness for free, with no lease renewal to get wrong and no clock to trust.
///
/// The database supplies what the broker cannot: a monotonic term. See <see cref="IResourceLease.Term"/>.
///
/// The connection here is DEDICATED and separate from SW.Bus's. Sharing it would tie every lease
/// in the process to the same fate as ordinary message traffic - one connection blip would drop
/// every broker connection this node owns, all at once.
/// </summary>
public class RabbitMqLeaderElection : ILeaderElection, IDisposable
{
    private const string QueuePrefix = "bitween.lease.";

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RabbitMqLeaderElection> _logger;
    private readonly string nodeName;
    private readonly ConnectionFactory _factory;

    private IConnection _connection;
    private readonly SemaphoreSlim _connectionGate = new(1, 1);

    public RabbitMqLeaderElection(IConfiguration configuration, IServiceProvider serviceProvider,
        ILogger<RabbitMqLeaderElection> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;

        // Distinct per process, not per machine: two instances on one host must not believe they
        // are the same owner.
        nodeName = NodeHeartbeat.NodeName;

        var connectionString = configuration.GetConnectionString("RabbitMQ")
            ?? throw new InvalidOperationException(
                "Leader election needs the RabbitMQ connection string; external bus providers " +
                "cannot be placed without it.");

        _factory = new ConnectionFactory
        {
            Uri = new Uri(connectionString),
            // Recovery must stay OFF. A recovered connection silently re-declares the exclusive
            // queue, so a node that lost ownership during an outage would quietly take it back
            // without ever bumping the term - two owners, neither aware.
            AutomaticRecoveryEnabled = false,
            RequestedConnectionTimeout = TimeSpan.FromSeconds(15)
        };
    }

    public string NodeName => nodeName;

    public async Task<IResourceLease> TryAcquireAsync(string resource,
        CancellationToken cancellationToken = default)
    {
        // Asked of the database first, so a lease another live node holds costs the broker nothing.
        // Declaring it anyway is refused, and RabbitMQ logs every refusal as an error: every node
        // trying every lease it doesn't hold, every thirty seconds, made most of the broker's log.
        if (await HeldByAnotherLiveNodeAsync(resource, cancellationToken)) return null;

        var connection = await GetConnectionAsync(cancellationToken);
        if (connection == null) return null;

        var queue = $"{QueuePrefix}{resource}";
        IChannel channel = null;
        try
        {
            channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

            // THE LOCK. Exclusive means one connection; every other node's declare fails here.
            await channel.QueueDeclareAsync(queue, durable: false, exclusive: true, autoDelete: true,
                cancellationToken: cancellationToken);
        }
        catch (OperationInterruptedException)
        {
            // RESOURCE_LOCKED - someone else owns it. Ordinary, and not worth logging above debug.
            channel?.Dispose();
            _logger.LogDebug("Resource {Resource} is owned by another node.", resource);
            return null;
        }
        catch (Exception ex)
        {
            channel?.Dispose();
            _logger.LogWarning(ex, "Could not attempt acquisition of {Resource}.", resource);
            return null;
        }

        try
        {
            // THE FENCE, and only after the lock is held - so exactly one node bumps the term.
            var term = await ClaimTermAsync(resource, cancellationToken);

            _logger.LogInformation("Node {Node} acquired {Resource} at term {Term}.",
                nodeName, resource, term);

            return new RabbitMqLease(resource, queue, term, channel, _serviceProvider, nodeName);
        }
        catch (Exception ex)
        {
            // Releasing the queue matters: holding a lock whose term we failed to record would
            // block every other node from ever taking it.
            channel.Dispose();
            _logger.LogError(ex, "Acquired the lock on {Resource} but could not record its term.", resource);
            return null;
        }
    }

    /// <summary>
    /// True when the lease's owner, by the database, is another node whose heartbeat is current. Only
    /// a hint: the queue is still the lock. A node that released its lease cleared the owner, so the
    /// next node takes it at once; one that died is tried again once its heartbeat is stale
    /// (<see cref="NodeHeartbeat.GoneAfter"/>), when the broker has long since freed the queue. Any
    /// doubt, such as no row or no heartbeat, is answered by trying, as before.
    /// </summary>
    private async Task<bool> HeldByAnotherLiveNodeAsync(string resource, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

            var owner = await dbContext.Set<ClusterLease>().AsNoTracking()
                .Where(l => l.Id == resource).Select(l => l.OwnerNode)
                .FirstOrDefaultAsync(cancellationToken);
            if (owner == null || owner == nodeName) return false;

            var aliveSince = DateTime.UtcNow - NodeHeartbeat.GoneAfter;
            return await dbContext.Set<ClusterNode>().AsNoTracking()
                .AnyAsync(n => n.Id == owner && n.LastSeenOn > aliveSince, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Could not read the owner of {Resource}; trying the lock.", resource);
            return false;
        }
    }

    private async Task<long> ClaimTermAsync(string resource, CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        var lease = await dbContext.Set<ClusterLease>()
            .FirstOrDefaultAsync(l => l.Id == resource, cancellationToken);

        if (lease == null)
        {
            lease = new ClusterLease(resource, nodeName);
            dbContext.Add(lease);
        }
        else
        {
            lease.Claim(nodeName);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return lease.Term;
    }

    private async Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        if (_connection is { IsOpen: true }) return _connection;

        await _connectionGate.WaitAsync(cancellationToken);
        try
        {
            if (_connection is { IsOpen: true }) return _connection;

            _connection?.Dispose();
            _connection = await _factory.CreateConnectionAsync($"bitween-election-{nodeName}", cancellationToken);

            _logger.LogInformation("Election connection open for node {Node}.", nodeName);
            return _connection;
        }
        catch (Exception ex)
        {
            // Not fatal: without a connection this node simply owns nothing, which is the correct
            // behaviour rather than an outage.
            _logger.LogWarning(ex, "Could not open the election connection; this node will own nothing.");
            return null;
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    public void Dispose()
    {
        try { _connection?.CloseAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult(); } catch { }
        _connection?.Dispose();
        _connectionGate.Dispose();
    }

    private sealed class RabbitMqLease(string resource, string queue, long term, IChannel channel,
        IServiceProvider serviceProvider, string nodeName) : IResourceLease
    {
        private readonly IChannel _channel = channel;
        private readonly IServiceProvider _serviceProvider = serviceProvider;
        private bool _released;

        private readonly string _queue = queue;

        public string Resource { get; } = resource;
        public long Term { get; } = term;

        // The channel closing IS the loss of ownership - the broker has already released the queue.
        public bool IsHeld => !_released && _channel.IsOpen;

        public async Task<bool> ValidateAsync(CancellationToken cancellationToken = default)
        {
            if (!IsHeld) return false;

            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

            var current = await dbContext.Set<ClusterLease>().AsNoTracking()
                .FirstOrDefaultAsync(l => l.Id == Resource, cancellationToken);

            // A higher term means someone else acquired while we were not looking, whatever our
            // channel still believes.
            return current != null && current.Term == Term && current.OwnerNode == nodeName;
        }

        public async ValueTask DisposeAsync()
        {
            _released = true;

            // DELETE the queue, do not merely close the channel.
            //
            // An exclusive queue belongs to the CONNECTION, not the channel - AMQP deletes it when
            // the connection closes. Closing the channel released nothing, so a node that gave up
            // a lease still held the lock until its whole election connection dropped, and the
            // next node could never take over. Deleting is allowed for the owning connection and
            // frees it at once.
            try { await _channel.QueueDeleteAsync(_queue, ifUnused: false, ifEmpty: false); } catch { }
            try { await _channel.CloseAsync(); } catch { }
            _channel.Dispose();

            // And say so, or the other nodes would wait out this node's heartbeat before trying it.
            // Only this term's ownership: a node that has taken it since keeps it.
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
                await dbContext.Set<ClusterLease>()
                    .Where(l => l.Id == Resource && l.Term == Term && l.OwnerNode == nodeName)
                    .ExecuteUpdateAsync(set => set.SetProperty(l => l.OwnerNode, (string)null));
            }
            catch { }
        }
    }
}
