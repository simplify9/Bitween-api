using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Domain;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

// The run flag is written with raw (parameterized) SQL that differs per database
// provider, so it needs a real round trip to prove the statement and its parameter
// binding are correct. There was no coverage here before.
[Collection("Bitween")]
public class RunFlagUpdaterTests
{
    private readonly BitweenFixture _fixture;

    public RunFlagUpdaterTests(BitweenFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Run_flag_claims_once_then_blocks_until_idle()
    {
        await using var scope = _fixture.CreateScope();
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
        await using var scope = _fixture.CreateScope();
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
}
