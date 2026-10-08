using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Domain;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// The nightly clean-up of receive-run history, against the real database: runs older than the
/// retention go, and everything inside it stays.
/// </summary>
[Collection("Bitween")]
public class ReceiveAttemptCleanupTests(BitweenFixture fixture)
{
    [Fact]
    public async Task Runs_past_the_retention_are_deleted_and_recent_ones_kept()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var options = scope.ServiceProvider.GetRequiredService<BitweenOptions>();
        var now = DateTime.UtcNow;

        ReceiveAttempt Run(DateTime startedOn) => new()
        {
            SubscriptionId = 1, StartedOn = startedOn, FinishedOn = startedOn.AddSeconds(1),
            Outcome = ReceiveOutcome.NoNewData, ExchangeIds = [],
        };
        var expired = Run(now.AddDays(-options.ReceiveAttemptRetentionDays - 1));
        var justInside = Run(now.AddDays(-options.ReceiveAttemptRetentionDays).AddHours(1));
        var today = Run(now);
        db.AddRange(expired, justInside, today);
        await db.SaveChangesAsync();

        await ActivatorUtilities.CreateInstance<ReceiveAttemptCleanupJob>(scope.ServiceProvider).Execute();

        var left = await db.Set<ReceiveAttempt>().AsNoTracking()
            .Where(a => a.Id == expired.Id || a.Id == justInside.Id || a.Id == today.Id)
            .Select(a => a.Id).ToListAsync();
        Assert.DoesNotContain(expired.Id, left);
        Assert.Contains(justInside.Id, left);
        Assert.Contains(today.Id, left);
    }
}
