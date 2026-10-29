using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SW.Bitween.Domain.Cluster;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Services.Cluster;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// Node placement, against a real broker.
///
/// A broker connection is exclusive: two nodes consuming one queue is duplicate processing, which
/// is the failure the whole external-bus design exists to prevent. So these tests are about one
/// question - can two nodes ever believe they own the same resource at the same time?
///
/// Each ILeaderElection instance here stands in for a NODE. They share the database and the
/// broker, which is exactly the situation a real cluster is in.
/// </summary>
[Collection("Bitween")]
public class LeaderElectionTests(BitweenFixture fixture)
{
    [Fact]
    public async Task Only_one_node_can_hold_a_resource()
    {
        var resource = Unique("ds");
        using var nodeA = Node();
        using var nodeB = Node();

        await using var first = await nodeA.TryAcquireAsync(resource);
        var second = await nodeB.TryAcquireAsync(resource);

        Assert.NotNull(first);
        Assert.Null(second);
        Assert.True(first!.IsHeld);
    }

    /// <summary>
    /// The race, rather than the sequence. Six nodes reaching for one resource at once must
    /// produce exactly one winner - a lock that only works when contention is polite is not a lock.
    /// </summary>
    [Fact]
    public async Task Concurrent_nodes_produce_exactly_one_owner()
    {
        var resource = Unique("race");
        var nodes = Enumerable.Range(0, 6).Select(_ => Node()).ToList();

        try
        {
            var leases = await Task.WhenAll(nodes.Select(n => n.TryAcquireAsync(resource)));
            var held = leases.Where(l => l != null).ToList();

            Assert.Single(held);

            // And exactly one term was issued, so no one else got as far as the fence.
            Assert.Equal(1, await TermOf(resource));

            foreach (var lease in held) await lease!.DisposeAsync();
        }
        finally
        {
            foreach (var node in nodes) node.Dispose();
        }
    }

    [Fact]
    public async Task Releasing_hands_the_resource_to_another_node()
    {
        var resource = Unique("handover");
        using var nodeA = Node();
        using var nodeB = Node();

        var first = await nodeA.TryAcquireAsync(resource);
        Assert.NotNull(first);
        Assert.Null(await nodeB.TryAcquireAsync(resource));

        await first!.DisposeAsync();

        // Available immediately: closing the channel releases the queue rather than leaving the
        // next node to wait for the broker to notice.
        var second = await WaitForAcquireAsync(nodeB, resource);

        Assert.NotNull(second);
        await second!.DisposeAsync();
    }

    /// <summary>
    /// The reason the database holds a term at all. The lock alone cannot tell a node that
    /// ownership moved while it was paused, so the term must increase on every acquisition and a
    /// holder of a stale term must fail validation.
    /// </summary>
    [Fact]
    public async Task The_term_increases_on_every_acquisition_and_fences_the_previous_holder()
    {
        var resource = Unique("fence");
        using var nodeA = Node();
        using var nodeB = Node();

        var first = await nodeA.TryAcquireAsync(resource);
        Assert.NotNull(first);
        Assert.True(await first!.ValidateAsync());

        var firstTerm = first.Term;
        await first.DisposeAsync();

        var second = await WaitForAcquireAsync(nodeB, resource);
        Assert.NotNull(second);

        Assert.True(second!.Term > firstTerm,
            $"term did not advance ({firstTerm} -> {second.Term}); a superseded node could not tell it had lost");

        // The first lease is now stale, and says so even though it once held the lock.
        Assert.False(await first.ValidateAsync(),
            "a lease whose term has been superseded must fail validation");

        await second.DisposeAsync();
    }

    /// <summary>
    /// Ownership is per data source, not global. One node holding everything would neither spread
    /// the connection load nor survive that node going away gracefully.
    /// </summary>
    [Fact]
    public async Task Different_resources_are_owned_independently()
    {
        using var nodeA = Node();
        using var nodeB = Node();

        await using var a = await nodeA.TryAcquireAsync(Unique("independent-a"));
        await using var b = await nodeB.TryAcquireAsync(Unique("independent-b"));

        Assert.NotNull(a);
        Assert.NotNull(b);
    }

    [Fact]
    public async Task A_lease_records_which_node_holds_it()
    {
        var resource = Unique("owner");
        using var node = Node();

        await using var lease = await node.TryAcquireAsync(resource);
        Assert.NotNull(lease);

        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var row = await db.Set<ClusterLease>().AsNoTracking().FirstAsync(l => l.Id == resource);

        Assert.Equal(node.NodeName, row.OwnerNode);
        Assert.Equal(lease!.Term, row.Term);
    }

    // ---------------------------------------------------------------- helpers

    private static string Unique(string prefix) => $"{prefix}.{Guid.NewGuid():N}"[..24];

    /// <summary>
    /// A node. Each gets its own election instance, and therefore its own broker connection -
    /// which is what makes the exclusive-queue lock meaningful between them.
    /// </summary>
    private RabbitMqLeaderElection Node() => new(
        fixture.App.Services.GetRequiredService<IConfiguration>(),
        fixture.App.Services,
        fixture.App.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger<RabbitMqLeaderElection>());

    private async Task<long> TermOf(string resource)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var row = await db.Set<ClusterLease>().AsNoTracking().FirstOrDefaultAsync(l => l.Id == resource);
        return row?.Term ?? 0;
    }

    /// <summary>
    /// The broker releases an exclusive queue promptly but not instantly, so a handover is retried
    /// briefly rather than asserted on the first attempt.
    /// </summary>
    private static async Task<IResourceLease> WaitForAcquireAsync(ILeaderElection node, string resource)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            var lease = await node.TryAcquireAsync(resource);
            if (lease != null) return lease;
            await Task.Delay(200);
        }
        return null;
    }
}
