using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Cluster;
using SW.Bitween.Domain.DataSources;
using SW.Bitween.Domain.Gateway;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.Bitween.Services.Cluster;
using SW.Bitween.Services.DataSources;
using SW.PrimitiveTypes;
using SW.Serverless.Resident;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// The supervisor: the loop that decides WHICH broker connections this node holds open.
///
/// Everything else in the external-bus feature has been exercised through an adapter started by
/// hand. That skips the part most likely to go wrong in production, because reconciliation is not a
/// one-shot: it runs every thirty seconds, for ever, against a database other people are editing.
/// The failures it can produce are the expensive kind — an adapter restarted on every pass drops
/// in-flight messages, a lease held after it was superseded means two nodes consuming one queue,
/// and one unreachable broker taking the loop down with it stops every other integration on the
/// node.
///
/// So these tests drive ReconcileAsync directly and ask what the loop actually did, rather than
/// waiting on a timer and hoping.
/// </summary>
[Collection("Bitween")]
public class BusProviderSupervisorTests(BitweenFixture fixture)
{
    /// <summary>
    /// The whole point, end to end: a row in the database becomes a live broker connection, and a
    /// message on that broker becomes an Xchange — with nobody starting an adapter by hand.
    /// </summary>
    [Fact]
    public async Task Reconciling_starts_the_adapter_for_an_active_data_source()
    {
        var queue = Unique("sup-start");
        var dataSourceId = await CreateDataSourceAsync();
        var documentId = await AddGatewayAsync(dataSourceId, queue);

        await using var supervisor = Supervisor();
        await supervisor.ReconcileAsync();

        Assert.NotNull(InstanceOf(dataSourceId));

        Publish(queue, """{"from":"the supervisor"}""");

        var xchange = await WaitForXchangeAsync(documentId);
        Assert.NotNull(xchange);
    }

    /// <summary>
    /// The endpoint comes from the GATEWAY, not from the data source. This is what lets one
    /// connection serve many queues, and it is the only reason a data source needs no queue
    /// configuration of its own.
    /// </summary>
    [Fact]
    public async Task A_gateway_endpoint_is_what_tells_the_adapter_which_queue_to_consume()
    {
        var queue = Unique("sup-endpoint");
        var dataSourceId = await CreateDataSourceAsync();
        var documentId = await AddGatewayAsync(dataSourceId, queue);

        await using var supervisor = Supervisor();
        await supervisor.ReconcileAsync();

        var declared = await DetailsOf(dataSourceId);
        Assert.Contains(queue, string.Join(",", declared.Values));

        Publish(queue, """{"routed":"by endpoint"}""");
        Assert.NotNull(await WaitForXchangeAsync(documentId));
    }

    /// <summary>
    /// A second gateway on the same connection must reach the adapter, and the first must keep
    /// working. Restarting for a new queue is acceptable; losing the old one is not.
    /// </summary>
    [Fact]
    public async Task A_second_gateway_adds_its_queue_without_losing_the_first()
    {
        var first = Unique("sup-two-a");
        var second = Unique("sup-two-b");

        var dataSourceId = await CreateDataSourceAsync();
        var firstDocument = await AddGatewayAsync(dataSourceId, first);

        await using var supervisor = Supervisor();
        await supervisor.ReconcileAsync();

        var secondDocument = await AddGatewayAsync(dataSourceId, second);
        await supervisor.ReconcileAsync();

        Publish(first, """{"queue":"first"}""");
        Publish(second, """{"queue":"second"}""");

        Assert.NotNull(await WaitForXchangeAsync(firstDocument));
        Assert.NotNull(await WaitForXchangeAsync(secondDocument));
    }

    /// <summary>
    /// The most consequential thing this loop does is NOTHING. Reconciliation runs every thirty
    /// seconds; if an unchanged data source were restarted each pass, every long-running consumer
    /// on the node would be torn down twice a minute and whatever it held in flight would be
    /// redelivered. The process id is the honest witness — a restart cannot preserve it.
    /// </summary>
    [Fact]
    public async Task An_unchanged_data_source_is_left_alone_across_passes()
    {
        var dataSourceId = await CreateDataSourceAsync();
        await AddGatewayAsync(dataSourceId, Unique("sup-stable"));

        await using var supervisor = Supervisor();
        await supervisor.ReconcileAsync();

        var before = InstanceOf(dataSourceId)?.ProcessId;
        Assert.NotNull(before);

        await supervisor.ReconcileAsync();
        await supervisor.ReconcileAsync();

        Assert.Equal(before, InstanceOf(dataSourceId)?.ProcessId);
    }

    /// <summary>
    /// A changed connection, on the other hand, MUST restart: an adapter holding a connection built
    /// from the old credentials is exactly the stale state the loop exists to correct.
    /// </summary>
    [Fact]
    public async Task A_changed_data_source_restarts_its_adapter()
    {
        var dataSourceId = await CreateDataSourceAsync();
        await AddGatewayAsync(dataSourceId, Unique("sup-change"));

        await using var supervisor = Supervisor();
        await supervisor.ReconcileAsync();

        var before = InstanceOf(dataSourceId)?.ProcessId;
        Assert.NotNull(before);

        await MutateAsync(dataSourceId, d => d.Properties["Prefetch"] = "3");
        await supervisor.ReconcileAsync();

        var after = InstanceOf(dataSourceId)?.ProcessId;
        Assert.NotNull(after);
        Assert.NotEqual(before, after);
    }

    /// <summary>Deactivating is how an operator stops a connection without losing its configuration.</summary>
    [Fact]
    public async Task Deactivating_a_data_source_stops_its_adapter()
    {
        var dataSourceId = await CreateDataSourceAsync();
        await AddGatewayAsync(dataSourceId, Unique("sup-inactive"));

        await using var supervisor = Supervisor();
        await supervisor.ReconcileAsync();
        Assert.NotNull(InstanceOf(dataSourceId));

        await MutateAsync(dataSourceId, d => d.Inactive = true);
        await supervisor.ReconcileAsync();

        Assert.Null(InstanceOf(dataSourceId));
    }

    /// <summary>
    /// Stopping is not enough — the lease has to go too. A node holding a lock on a data source
    /// nobody is running would block whichever node later wants it, and nothing would ever
    /// release it.
    /// </summary>
    [Fact]
    public async Task Deactivating_a_data_source_releases_its_lease()
    {
        var dataSourceId = await CreateDataSourceAsync();
        await AddGatewayAsync(dataSourceId, Unique("sup-release"));

        await using var supervisor = Supervisor();
        await supervisor.ReconcileAsync();

        await MutateAsync(dataSourceId, d => d.Inactive = true);
        await supervisor.ReconcileAsync();

        // Another node must now be able to take it. Retried briefly: the broker releases an
        // exclusive queue promptly, but not instantly.
        using var other = Node();
        var lease = await WaitForAcquireAsync(other, $"datasource.{dataSourceId}");

        Assert.NotNull(lease);
        await lease!.DisposeAsync();
    }

    /// <summary>
    /// Placement. A data source another node already owns must not be started here, or both nodes
    /// consume the same queue and every message is processed twice — the failure this whole design
    /// exists to prevent.
    /// </summary>
    [Fact]
    public async Task A_data_source_owned_by_another_node_is_not_started_here()
    {
        var dataSourceId = await CreateDataSourceAsync();
        await AddGatewayAsync(dataSourceId, Unique("sup-owned"));

        using var otherNode = Node();
        await using var theirs = await otherNode.TryAcquireAsync($"datasource.{dataSourceId}");
        Assert.NotNull(theirs);

        await using var supervisor = Supervisor();
        await supervisor.ReconcileAsync();

        Assert.Null(InstanceOf(dataSourceId));
    }

    /// <summary>
    /// Fencing, which is the case a lock alone cannot handle. A node paused long enough for its
    /// lease to be superseded still believes it holds ownership; the database term is what tells it
    /// otherwise. Bumping the term stands in for that pause.
    ///
    /// What the supervisor must do is STOP — immediately, and without draining, because another
    /// node may already be consuming and finishing in-flight work here would process the same
    /// messages twice. The process id is the witness: it can only change if the adapter was torn
    /// down and started again.
    ///
    /// It then starts again under a new term, and that is correct rather than a miss. Losing the
    /// term does not mean the resource is taken — it means this node's claim is no longer proof
    /// that it isn't. So it drops everything and asks again. Here nobody else holds the broker
    /// lock, so it legitimately wins; when another node really does hold it, the re-acquire fails
    /// and the adapter stays down — which is what
    /// <see cref="A_data_source_taken_over_by_another_node_is_stopped_here"/> covers.
    /// </summary>
    [Fact]
    public async Task A_superseded_lease_stops_the_adapter_before_anything_else()
    {
        var dataSourceId = await CreateDataSourceAsync();
        await AddGatewayAsync(dataSourceId, Unique("sup-fence"));

        await using var supervisor = Supervisor();
        await supervisor.ReconcileAsync();

        var before = InstanceOf(dataSourceId)?.ProcessId;
        Assert.NotNull(before);
        var termBefore = await TermOf($"datasource.{dataSourceId}");

        // Someone else won the resource while this node was not looking.
        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var row = await db.Set<ClusterLease>().FirstAsync(l => l.Id == $"datasource.{dataSourceId}");
            row.Claim("some-other-node");
            await db.SaveChangesAsync();
        }

        await supervisor.ReconcileAsync();

        var after = InstanceOf(dataSourceId)?.ProcessId;
        Assert.NotNull(after);
        Assert.NotEqual(before, after);

        // And it did not simply carry on under the stale claim.
        Assert.True(await TermOf($"datasource.{dataSourceId}") > termBefore + 1,
            "the supervisor kept its superseded term instead of taking a new one");
    }

    /// <summary>
    /// The handover this node is on the losing side of. Once another node holds the resource, an
    /// adapter still running here is duplicate processing — so it has to go, even though the data
    /// source is perfectly active and perfectly healthy.
    ///
    /// StopAsync releases the leases but leaves the supervisor's idea of what it is running intact,
    /// which is exactly the state a node is in after a pause: still consuming, no longer entitled
    /// to.
    /// </summary>
    [Fact]
    public async Task A_data_source_taken_over_by_another_node_is_stopped_here()
    {
        var dataSourceId = await CreateDataSourceAsync();
        await AddGatewayAsync(dataSourceId, Unique("sup-taken"));

        await using var supervisor = Supervisor();
        await supervisor.ReconcileAsync();
        Assert.NotNull(InstanceOf(dataSourceId));

        // Give up the lock without stopping the adapter, then let another node take it.
        await supervisor.StopAsync(default);

        using var otherNode = Node();
        await using var theirs = await WaitForAcquireAsync(otherNode, $"datasource.{dataSourceId}");
        Assert.NotNull(theirs);

        await supervisor.ReconcileAsync();

        Assert.Null(InstanceOf(dataSourceId));
    }

    /// <summary>
    /// One broker being unreachable is a normal Tuesday. If it could stop the pass, a single
    /// customer's expired credentials would take every other integration on the node down with it.
    /// </summary>
    [Fact]
    public async Task One_data_source_that_cannot_start_does_not_stop_the_others()
    {
        var broken = await CreateDataSourceAsync(adapterId: "bitween.bus.doesnotexist");
        await AddGatewayAsync(broken, Unique("sup-broken"));

        var queue = Unique("sup-healthy");
        var healthy = await CreateDataSourceAsync();
        var documentId = await AddGatewayAsync(healthy, queue);

        await using var supervisor = Supervisor();

        // Not "does not throw" — the pass has to complete far enough to start the good one.
        await supervisor.ReconcileAsync();

        Assert.Null(InstanceOf(broken));
        Assert.NotNull(InstanceOf(healthy));

        Publish(queue, """{"still":"working"}""");
        Assert.NotNull(await WaitForXchangeAsync(documentId));
    }

    /// <summary>
    /// A node that cannot start a data source — it can't reach the broker, say — used to keep the
    /// lease and retry forever, so the node that could have run it never got the chance. After a
    /// few failed starts it lets go.
    /// </summary>
    [Fact]
    public async Task A_data_source_that_keeps_failing_to_start_gives_up_its_lease()
    {
        var broken = await CreateDataSourceAsync(adapterId: "bitween.bus.doesnotexist");
        await AddGatewayAsync(broken, Unique("sup-yield"));

        await using var supervisor = Supervisor();
        await supervisor.ReconcileAsync();

        using var other = Node();
        Assert.Null(await other.TryAcquireAsync($"datasource.{broken}"));

        await supervisor.ReconcileAsync();
        await supervisor.ReconcileAsync();

        var lease = await WaitForAcquireAsync(other, $"datasource.{broken}");
        Assert.NotNull(lease);

        // And while it waits out the pause, this node does not grab it back.
        await supervisor.ReconcileAsync();
        Assert.Null(InstanceOf(broken));
        await lease!.DisposeAsync();
    }

    /// <summary>
    /// Health has to land in the database, because that is the only place the UI and the notifiers
    /// can see it. An operator should not need to tail logs to find out a broker went away.
    /// </summary>
    [Fact]
    public async Task Health_and_ownership_are_written_back_to_the_data_source()
    {
        var dataSourceId = await CreateDataSourceAsync();
        await AddGatewayAsync(dataSourceId, Unique("sup-health"));

        await using var supervisor = Supervisor();
        await supervisor.ReconcileAsync();

        // The first pass starts the adapter; health arrives on a heartbeat, so it is the pass
        // after the first heartbeat that records it.
        DataSource row = null;
        await WaitAsync(async () =>
        {
            await supervisor.ReconcileAsync();
            row = await ReadAsync(dataSourceId);
            return row.LastHeartbeatOn != null;
        }, TimeSpan.FromSeconds(30), "the heartbeat never reached the data source row");

        Assert.NotNull(row!.LastKnownState);
        Assert.NotNull(row.OwnedByNode);
        Assert.Contains("term", row.OwnedByNode);
    }

    /// <summary>
    /// Shutdown is a handover, not an abandonment. Releasing on the way out is what makes a rolling
    /// restart take seconds instead of waiting for the broker to time the old connection out.
    /// </summary>
    [Fact]
    public async Task Stopping_the_supervisor_releases_what_it_held()
    {
        var dataSourceId = await CreateDataSourceAsync();
        await AddGatewayAsync(dataSourceId, Unique("sup-handover"));

        var supervisor = Supervisor();
        await supervisor.ReconcileAsync();
        Assert.NotNull(InstanceOf(dataSourceId));

        await supervisor.StopAsync(default);
        supervisor.Dispose();

        using var other = Node();
        var lease = await WaitForAcquireAsync(other, $"datasource.{dataSourceId}");
        Assert.NotNull(lease);
        await lease!.DisposeAsync();

        await StopInstanceAsync(dataSourceId);
    }

    /// <summary>
    /// An inactive gateway must not contribute its endpoint. Otherwise deactivating a gateway would
    /// leave Bitween still consuming its queue and acking messages nobody routes anywhere — a
    /// silent drop, which is worse than an error.
    /// </summary>
    [Fact]
    public async Task An_inactive_gateway_does_not_contribute_its_endpoint()
    {
        var live = Unique("sup-live");
        var dead = Unique("sup-dead");

        var dataSourceId = await CreateDataSourceAsync();
        var liveDocument = await AddGatewayAsync(dataSourceId, live);
        var deadGatewayId = await AddGatewayAsync(dataSourceId, dead, returnGatewayId: true);

        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var gateway = await db.Set<BusGateway>().FirstAsync(g => g.Id == deadGatewayId);
            gateway.Inactive = true;
            await db.SaveChangesAsync();
            scope.ServiceProvider.GetRequiredService<IInfolinkCache>().Revoke();
        }

        await using var supervisor = Supervisor();
        await supervisor.ReconcileAsync();

        var details = string.Join(",", (await DetailsOf(dataSourceId)).Values);
        Assert.Contains(live, details);
        Assert.DoesNotContain(dead, details);

        // And prove it by behaviour: the live queue still works.
        Publish(live, """{"gateway":"live"}""");
        Assert.NotNull(await WaitForXchangeAsync(liveDocument));
    }

    // ---------------------------------------------------------------- helpers

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..24];

    /// <summary>
    /// A supervisor with its own election instance, so it stands in for one node. Disposing stops
    /// whatever it started, which keeps a failing test from leaving a live broker connection behind
    /// for the rest of the collection.
    /// </summary>
    private TestSupervisor Supervisor() => new(
        new BusProviderSupervisor(
            fixture.App.Services,
            fixture.App.Services.GetRequiredService<IResidentAdapterHost>(),
            Node(),
            fixture.App.Services.GetRequiredService<ILoggerFactory>()
                .CreateLogger<BusProviderSupervisor>()),
        fixture.App.Services.GetRequiredService<IResidentAdapterHost>());

    private sealed class TestSupervisor : IAsyncDisposable
    {
        private readonly BusProviderSupervisor _supervisor;
        private readonly IResidentAdapterHost _adapters;

        // Every test in the collection shares one resident host, so cleaning up "whatever is
        // running" would stop another test's adapter. Only what appeared after this supervisor
        // was built is ours to stop.
        private readonly HashSet<string> _preexisting;
        private readonly HashSet<string> _started = new();

        public TestSupervisor(BusProviderSupervisor supervisor, IResidentAdapterHost adapters)
        {
            _supervisor = supervisor;
            _adapters = adapters;
            _preexisting = Keys();
        }

        private HashSet<string> Keys() =>
            _adapters.Describe().Select(h => $"{h.AdapterId}|{h.InstanceKey}").ToHashSet();

        public async Task ReconcileAsync()
        {
            await _supervisor.ReconcileAsync();
            foreach (var key in Keys().Where(k => !_preexisting.Contains(k)))
                _started.Add(key);
        }

        public Task StopAsync(System.Threading.CancellationToken token) => _supervisor.StopAsync(token);

        public void Dispose() => _supervisor.Dispose();

        public async ValueTask DisposeAsync()
        {
            try { await _supervisor.StopAsync(default); } catch { /* best effort */ }

            var live = Keys();
            foreach (var key in _started.Where(live.Contains))
            {
                var parts = key.Split('|');
                try { await _adapters.StopAsync(parts[0], parts[1], drain: false); } catch { }
            }

            _supervisor.Dispose();
        }
    }

    private RabbitMqLeaderElection Node() => new(
        fixture.App.Services.GetRequiredService<IConfiguration>(),
        fixture.App.Services,
        fixture.App.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger<RabbitMqLeaderElection>());

    private static async Task<IResourceLease> WaitForAcquireAsync(ILeaderElection node, string resource)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            var lease = await node.TryAcquireAsync(resource);
            if (lease != null) return lease;
            await Task.Delay(250);
        }
        return null;
    }

    /// <summary>
    /// A data source with connection settings ONLY. No Endpoints key: the whole point of these
    /// tests is that the supervisor derives that from the gateways.
    /// </summary>
    private async Task<int> CreateDataSourceAsync(string adapterId = null)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        var dataSource = new DataSource
        {
            Name = Unique("ds"),
            AdapterId = adapterId ?? BusAdapters.RabbitMq,
            Kind = DataSourceKind.Broker,
            Properties = new Dictionary<string, string>(fixture.ExternalRabbitProperties)
        };

        db.Add(dataSource);
        await db.SaveChangesAsync();
        return dataSource.Id;
    }

    private async Task<int> AddGatewayAsync(int dataSourceId, string endpoint, bool returnGatewayId = false)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        var document = new Document(null, Unique("doc"), DocumentFormat.Json);
        db.Add(document);
        await db.SaveChangesAsync();

        var gateway = new BusGateway
        {
            Name = Unique("gw"),
            DocumentId = document.Id,
            DataSourceId = dataSourceId,
            Endpoint = endpoint
        };
        db.Add(gateway);
        await db.SaveChangesAsync();

        // The infolink cache is a singleton holding a ten-minute snapshot; without revoking it a
        // brand-new Document resolves to null and the message is acked and silently dropped.
        scope.ServiceProvider.GetRequiredService<IInfolinkCache>().Revoke();

        return returnGatewayId ? gateway.Id : document.Id;
    }

    private async Task MutateAsync(int dataSourceId, Action<DataSource> mutate)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var row = await db.Set<DataSource>().FirstAsync(d => d.Id == dataSourceId);
        mutate(row);
        await db.SaveChangesAsync();
    }

    private async Task<long> TermOf(string resource)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var row = await db.Set<ClusterLease>().AsNoTracking().FirstOrDefaultAsync(l => l.Id == resource);
        return row?.Term ?? 0;
    }

    private async Task<DataSource> ReadAsync(int dataSourceId)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        return await db.Set<DataSource>().AsNoTracking().FirstAsync(d => d.Id == dataSourceId);
    }

    private InstanceHealth InstanceOf(int dataSourceId) =>
        fixture.App.Services.GetRequiredService<IResidentAdapterHost>()
            .Describe().FirstOrDefault(h => h.InstanceKey == dataSourceId.ToString());

    /// <summary>
    /// What the adapter says it is consuming. Read from the heartbeat rather than from the startup
    /// values, so it reflects what actually reached the broker.
    /// </summary>
    private async Task<IDictionary<string, string>> DetailsOf(int dataSourceId)
    {
        IDictionary<string, string> details = new Dictionary<string, string>();
        await WaitAsync(() =>
        {
            var instance = InstanceOf(dataSourceId);
            if (instance?.Details is not { Count: > 0 }) return Task.FromResult(false);
            details = instance.Details;
            return Task.FromResult(true);
        }, TimeSpan.FromSeconds(30), $"no heartbeat detail arrived for data source {dataSourceId}");

        return details;
    }

    private async Task StopInstanceAsync(int dataSourceId)
    {
        var instance = InstanceOf(dataSourceId);
        if (instance == null) return;

        var host = fixture.App.Services.GetRequiredService<IResidentAdapterHost>();
        try { await host.StopAsync(instance.AdapterId, instance.InstanceKey, drain: false); } catch { }
    }

    private void Publish(string queue, string body)
    {
        using var connection = ExternalConnection();
        using var channel = connection.CreateModel();

        channel.QueueDeclare(queue, durable: true, exclusive: false, autoDelete: false);

        var properties = channel.CreateBasicProperties();
        properties.ContentType = "application/json";
        properties.MessageId = Guid.NewGuid().ToString("N");
        properties.DeliveryMode = 2;

        channel.BasicPublish("", queue, properties, Encoding.UTF8.GetBytes(body));
    }

    private IConnection ExternalConnection() => new ConnectionFactory
    {
        HostName = fixture.ExternalRabbitHost,
        Port = fixture.ExternalRabbitPort,
        UserName = fixture.ExternalRabbitUser,
        Password = fixture.ExternalRabbitPassword
    }.CreateConnection("supervisor-tests");

    private async Task<Xchange> WaitForXchangeAsync(int documentId)
    {
        Xchange found = null;
        await WaitAsync(async () =>
        {
            await using var scope = fixture.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            found = await db.Set<Xchange>().AsNoTracking()
                .FirstOrDefaultAsync(x => x.DocumentId == documentId);
            return found != null;
        }, TimeSpan.FromSeconds(45), $"no Xchange was created for document {documentId}");

        return found;
    }

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
