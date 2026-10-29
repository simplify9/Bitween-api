using Microsoft.EntityFrameworkCore;
using System.Linq;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Domain;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

// The run flag is claimed with one conditional UPDATE; these prove it against a real database,
// including what happens when runners race and when a run dies holding it.
[Collection("Bitween")]
public class RunFlagUpdaterTests(BitweenFixture fixture)
{
    [Fact]
    public async Task Run_flag_claims_once_then_blocks_until_idle()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var runFlag = scope.ServiceProvider.GetRequiredService<RunFlagUpdater>();

        var document = new Document(6101, "Run Flag Test Doc");
        db.Set<Document>().Add(document);
        var subscription = new Subscription("Run Flag Test", document.Id);
        subscription.Inactive = false;
        db.Set<Subscription>().Add(subscription);
        await db.SaveChangesAsync();

        Assert.True(await runFlag.MarkAsRunning(subscription.Id));   // first claim wins
        Assert.False(await runFlag.MarkAsRunning(subscription.Id));  // already running

        await runFlag.MarkAsIdle(subscription.Id);

        Assert.True(await runFlag.MarkAsRunning(subscription.Id));   // claimable again
        await runFlag.MarkAsIdle(subscription.Id);
    }

    // Guards the parameter binding: a broken placeholder would either match no rows
    // or every row, and both would show up here.
    [Fact]
    public async Task Run_flag_only_affects_the_requested_subscription()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var runFlag = scope.ServiceProvider.GetRequiredService<RunFlagUpdater>();

        var document = new Document(6102, "Run Flag Isolation Doc");
        db.Set<Document>().Add(document);
        var a = new Subscription("Run Flag A", document.Id);
        a.Inactive = false;
        var b = new Subscription("Run Flag B", document.Id);
        b.Inactive = false;
        db.Set<Subscription>().AddRange(a, b);
        await db.SaveChangesAsync();

        Assert.True(await runFlag.MarkAsRunning(a.Id));
        Assert.True(await runFlag.MarkAsRunning(b.Id)); // b untouched by a's update

        await runFlag.MarkAsIdle(a.Id);
        Assert.False(await runFlag.MarkAsRunning(b.Id)); // b still running, a's idle did not clear it

        await runFlag.MarkAsIdle(b.Id);
    }

    /// <summary>
    /// A run killed with its process never clears the flag. It used to stay set for good, and the
    /// subscription never ran again until someone edited the database.
    /// </summary>
    [Fact]
    public async Task A_flag_left_by_a_run_that_died_is_taken_over()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var runFlag = scope.ServiceProvider.GetRequiredService<RunFlagUpdater>();

        var document = new Document(6103, "Run Flag Stale Doc");
        db.Set<Document>().Add(document);
        var subscription = new Subscription("Run Flag Stale", document.Id) { Inactive = false };
        db.Set<Subscription>().Add(subscription);
        await db.SaveChangesAsync();

        Assert.True(await runFlag.MarkAsRunning(subscription.Id));
        // The run dies: nobody calls MarkAsIdle, and the clock moves past the limit.
        var longAgo = DateTime.UtcNow.AddHours(-5);
        await db.Set<Subscription>().Where(s => s.Id == subscription.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.RunningSince, longAgo));

        Assert.True(await runFlag.MarkAsRunning(subscription.Id));
        Assert.False(await runFlag.MarkAsRunning(subscription.Id)); // the new run now holds it
        await runFlag.MarkAsIdle(subscription.Id);
    }

    /// <summary>One conditional UPDATE, so however many runners race, exactly one claims it.</summary>
    [Fact]
    public async Task Of_many_concurrent_claims_exactly_one_wins()
    {
        int id;
        await using (var setup = fixture.CreateScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var document = new Document(6104, "Run Flag Race Doc");
            db.Set<Document>().Add(document);
            var subscription = new Subscription("Run Flag Race", document.Id) { Inactive = false };
            db.Set<Subscription>().Add(subscription);
            await db.SaveChangesAsync();
            id = subscription.Id;
        }

        var claims = await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
        {
            await using var scope = fixture.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<RunFlagUpdater>().MarkAsRunning(id);
        }));

        Assert.Equal(1, claims.Count(c => c));

        await using var cleanup = fixture.CreateScope();
        await cleanup.ServiceProvider.GetRequiredService<RunFlagUpdater>().MarkAsIdle(id);
    }
}
