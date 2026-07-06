using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Domain;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

[Collection("Bitween")]
public class RetryJobTests
{
    private readonly BitweenFixture _fixture;

    public RetryJobTests(BitweenFixture fixture)
    {
        _fixture = fixture;
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private RetryJob BuildJob(BitweenDbContext db, XchangeService xchangeService) =>
        new(db, xchangeService);

    // ─── Batch query ──────────────────────────────────────────────────────────

    [Fact]
    public async Task RetryJob_does_not_process_future_delayed_retry()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var xs = scope.ServiceProvider.GetRequiredService<XchangeService>();

        var delayedRetry = new DelayedRetry
        {
            Id = "rjt-future-" + Guid.NewGuid().ToString("N")[..8],
            On = DateTime.UtcNow.AddHours(1)
        };
        db.Set<DelayedRetry>().Add(delayedRetry);
        await db.SaveChangesAsync();

        await BuildJob(db, xs).Execute();

        var stillExists = await db.Set<DelayedRetry>().AnyAsync(r => r.Id == delayedRetry.Id);
        Assert.True(stillExists, "A future DelayedRetry must not be processed before its due time.");
    }

    // ─── Orphan cleanup ───────────────────────────────────────────────────────

    [Fact]
    public async Task RetryJob_removes_delayed_retry_when_xchange_is_missing()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var xs = scope.ServiceProvider.GetRequiredService<XchangeService>();

        // ID that has no matching Xchange row
        var delayedRetry = new DelayedRetry
        {
            Id = "rjt-orphan-" + Guid.NewGuid().ToString("N")[..8],
            On = DateTime.UtcNow.AddMinutes(-1)
        };
        db.Set<DelayedRetry>().Add(delayedRetry);
        await db.SaveChangesAsync();

        await BuildJob(db, xs).Execute();

        var gone = !await db.Set<DelayedRetry>().AnyAsync(r => r.Id == delayedRetry.Id);
        Assert.True(gone, "A DelayedRetry whose Xchange no longer exists must be removed.");
    }

    [Fact]
    public async Task RetryJob_removes_delayed_retry_when_subscription_is_missing()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var xs = scope.ServiceProvider.GetRequiredService<XchangeService>();

        // Create an Xchange with no subscription (SubscriptionId remains null)
        var doc = new Document(8003, "RetryJob Orphan Sub Doc");
        db.Set<Document>().Add(doc);
        await db.SaveChangesAsync();

        // The base Xchange constructor leaves SubscriptionId null
        var xchange = new Xchange(doc.Id, null, new XchangeFile("{}"));
        db.Set<Xchange>().Add(xchange);
        await db.SaveChangesAsync();

        var delayedRetry = new DelayedRetry { Id = xchange.Id, On = DateTime.UtcNow.AddMinutes(-1) };
        db.Set<DelayedRetry>().Add(delayedRetry);
        await db.SaveChangesAsync();

        await BuildJob(db, xs).Execute();

        var gone = !await db.Set<DelayedRetry>().AnyAsync(r => r.Id == delayedRetry.Id);
        Assert.True(gone,
            "A DelayedRetry whose Subscription no longer exists must be removed without creating a retry Xchange.");
    }

    // ─── Full execution path ──────────────────────────────────────────────────

    [Fact]
    public async Task RetryJob_processes_due_delayed_retry_and_creates_retry_xchange()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var xs = scope.ServiceProvider.GetRequiredService<XchangeService>();

        var doc = new Document(8001, "RetryJob Due Doc");
        db.Set<Document>().Add(doc);
        await db.SaveChangesAsync();

        var sub = new Subscription("RetryJob Sub", doc.Id);
        sub.Inactive = false;
        db.Set<Subscription>().Add(sub);
        await db.SaveChangesAsync();

        // Use CreateXchange so the input file is uploaded to real cloud storage
        var originalXchange = await xs.CreateXchange(sub, new XchangeFile("{}"));

        var groupCounts = new System.Collections.Generic.Dictionary<string, int>
        {
            [Guid.NewGuid().ToString()] = 1
        };
        var delayedRetry = new DelayedRetry
        {
            Id = originalXchange.Id,
            On = DateTime.UtcNow.AddMinutes(-1),
            GroupAttemptCounts = groupCounts
        };
        db.Set<DelayedRetry>().Add(delayedRetry);
        await db.SaveChangesAsync();

        await BuildJob(db, xs).Execute();

        // DelayedRetry must be gone
        var retryGone = !await db.Set<DelayedRetry>().AnyAsync(r => r.Id == originalXchange.Id);
        Assert.True(retryGone, "The processed DelayedRetry record must be deleted.");

        // A new Xchange with RetryFor pointing to the original must exist
        var retryXchange = await db.Set<Xchange>()
            .FirstOrDefaultAsync(x => x.RetryFor == originalXchange.Id);
        Assert.NotNull(retryXchange);
        Assert.Equal(originalXchange.Id, retryXchange.RetryFor);
        Assert.Equal(sub.Id, retryXchange.SubscriptionId);
    }

    [Fact]
    public async Task RetryJob_carries_group_attempt_counts_onto_retry_xchange()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var xs = scope.ServiceProvider.GetRequiredService<XchangeService>();

        var doc = new Document(8002, "RetryJob GroupCounts Doc");
        db.Set<Document>().Add(doc);
        await db.SaveChangesAsync();

        var sub = new Subscription("RetryJob GroupCounts Sub", doc.Id);
        sub.Inactive = false;
        db.Set<Subscription>().Add(sub);
        await db.SaveChangesAsync();

        var originalXchange = await xs.CreateXchange(sub, new XchangeFile("{}"));

        var groupId = Guid.NewGuid().ToString();
        var delayedRetry = new DelayedRetry
        {
            Id = originalXchange.Id,
            On = DateTime.UtcNow.AddMinutes(-1),
            GroupAttemptCounts = new System.Collections.Generic.Dictionary<string, int>
            {
                [groupId] = 2
            }
        };
        db.Set<DelayedRetry>().Add(delayedRetry);
        await db.SaveChangesAsync();

        await BuildJob(db, xs).Execute();

        var retryXchange = await db.Set<Xchange>()
            .FirstOrDefaultAsync(x => x.RetryFor == originalXchange.Id);
        Assert.NotNull(retryXchange);
        Assert.NotNull(retryXchange.GroupAttemptCounts);
        Assert.True(retryXchange.GroupAttemptCounts.TryGetValue(groupId, out var count));
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task RetryJob_processes_multiple_due_records_in_one_invocation()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var xs = scope.ServiceProvider.GetRequiredService<XchangeService>();

        // Create 3 subscriptions / xchanges
        var docs = new[] { 8005, 8006, 8007 };
        var originalIds = new string[3];

        for (var i = 0; i < 3; i++)
        {
            var doc = new Document(docs[i], $"RetryJob Batch Doc {i}");
            db.Set<Document>().Add(doc);
            await db.SaveChangesAsync();

            var sub = new Subscription($"RetryJob Batch Sub {i}", doc.Id);
            sub.Inactive = false;
            db.Set<Subscription>().Add(sub);
            await db.SaveChangesAsync();

            var xchange = await xs.CreateXchange(sub, new XchangeFile("{}"));
            originalIds[i] = xchange.Id;

            db.Set<DelayedRetry>().Add(new DelayedRetry
            {
                Id = xchange.Id,
                On = DateTime.UtcNow.AddMinutes(-1)
            });
        }

        // Also add a future record that must not be processed
        var futureId = "rjt-batch-future-" + Guid.NewGuid().ToString("N")[..8];
        db.Set<DelayedRetry>().Add(new DelayedRetry
        {
            Id = futureId,
            On = DateTime.UtcNow.AddHours(1)
        });
        await db.SaveChangesAsync();

        await BuildJob(db, xs).Execute();

        // All 3 due records removed
        foreach (var id in originalIds)
        {
            var removed = !await db.Set<DelayedRetry>().AnyAsync(r => r.Id == id);
            Assert.True(removed, $"Due DelayedRetry {id} must have been processed and removed.");
        }

        // Future record untouched
        var futureIntact = await db.Set<DelayedRetry>().AnyAsync(r => r.Id == futureId);
        Assert.True(futureIntact, "The future DelayedRetry must not be processed.");

        // 3 retry Xchanges created
        foreach (var originalId in originalIds)
        {
            var retryXchange = await db.Set<Xchange>()
                .FirstOrDefaultAsync(x => x.RetryFor == originalId);
            Assert.NotNull(retryXchange);
        }
    }
}
