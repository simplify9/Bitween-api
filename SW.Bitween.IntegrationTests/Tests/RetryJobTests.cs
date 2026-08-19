using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
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
        new(db, xchangeService, NullLogger<RetryJob>.Instance);

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

    // ─── One bad row must not stop the rest ───────────────────────────────────

    /// <summary>
    /// An Xchange whose input file was never uploaded: reading it fails, which is what a retry whose
    /// file has since been deleted from storage looks like.
    /// </summary>
    private static async Task<Xchange> AddUnreadableXchange(BitweenDbContext db, Subscription sub)
    {
        var xchange = new Xchange(sub, new XchangeFile("{}"));
        db.Set<Xchange>().Add(xchange);
        db.Set<XchangeResult>().Add(new XchangeResult(xchange.Id, null, null, exception: "boom"));
        await db.SaveChangesAsync();
        return xchange;
    }

    [Fact]
    public async Task RetryJob_drops_a_retry_whose_input_is_gone_and_still_runs_the_others()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var xs = scope.ServiceProvider.GetRequiredService<XchangeService>();

        var doc = new Document(8010, "RetryJob Missing Input Doc");
        db.Set<Document>().Add(doc);
        await db.SaveChangesAsync();

        var sub = new Subscription("RetryJob Missing Input Sub", doc.Id) { Inactive = false };
        db.Set<Subscription>().Add(sub);
        await db.SaveChangesAsync();

        var unreadable = await AddUnreadableXchange(db, sub);
        var healthy = await xs.CreateXchange(sub, new XchangeFile("{}"));

        db.Set<DelayedRetry>().AddRange(
            new DelayedRetry { Id = unreadable.Id, On = DateTime.UtcNow.AddMinutes(-2) },
            new DelayedRetry { Id = healthy.Id, On = DateTime.UtcNow.AddMinutes(-1) });
        await db.SaveChangesAsync();

        await BuildJob(db, xs).Execute();

        // With one commit per row, the retry that could not be made does not undo the one that could.
        // Committing the batch in one go would have lost both and left both schedules behind.
        Assert.False(await db.Set<DelayedRetry>().AnyAsync(r => r.Id == healthy.Id));
        Assert.True(await db.Set<Xchange>().AnyAsync(x => x.RetryFor == healthy.Id));

        // The unusable one leaves the queue too, rather than being tried again every minute for good.
        Assert.False(await db.Set<DelayedRetry>().AnyAsync(r => r.Id == unreadable.Id));

        // And it says so where a reader already looks for "why is this not being retried?".
        var result = await db.Set<XchangeResult>().AsNoTracking().SingleAsync(r => r.Id == unreadable.Id);
        Assert.Contains("input file could not be read", result.RetryBlockedReason);
    }

    [Fact]
    public async Task RetryJob_works_through_more_than_one_batch_in_a_single_run()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var xs = scope.ServiceProvider.GetRequiredService<XchangeService>();

        // Schedules pointing at exchanges that no longer exist: the cheapest row to process, and enough
        // of them to need more than one batch of 100.
        var ids = Enumerable.Range(0, 105)
            .Select(i => $"rjt-batch-{Guid.NewGuid():N}-{i}")
            .ToList();

        db.Set<DelayedRetry>().AddRange(ids.Select((id, i) => new DelayedRetry
        {
            Id = id,
            On = DateTime.UtcNow.AddMinutes(-(i + 1))
        }));
        await db.SaveChangesAsync();

        await BuildJob(db, xs).Execute();

        // All of them, not the first hundred: a backlog should not have to wait a minute per hundred.
        var left = await db.Set<DelayedRetry>().CountAsync(r => ids.Contains(r.Id));
        Assert.Equal(0, left);
    }

    // ─── Bulk retry with no subscription ──────────────────────────────────────

    [Fact]
    public async Task BulkRetry_handles_an_exchange_with_no_subscription()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var xs = scope.ServiceProvider.GetRequiredService<XchangeService>();

        var doc = new Document(8012, "BulkRetry No Sub Doc");
        db.Set<Document>().Add(doc);
        await db.SaveChangesAsync();

        // A document-only exchange: SubscriptionId is null from the start, which is also what an
        // exchange whose subscription was later deleted looks like. Created through the service so its
        // input file is really in storage, since bulk retry reads it before looking anything else up.
        var orphan = await xs.CreateXchange(doc, WorkGroup.None, new XchangeFile("{}"));
        await db.SaveChangesAsync();

        var healthy = await xs.CreateXchange(doc, WorkGroup.None, new XchangeFile("{}"));
        await db.SaveChangesAsync();

        // One selection containing both. This threw before, so the whole bulk retry failed — including
        // for the exchanges that were perfectly retryable.
        await new Resources.Xchanges.BulkRetry(db, xs).Handle(new XchangeBulkRetry
        {
            Ids = [orphan.Id, healthy.Id],
            Reset = false
        });
        await db.SaveChangesAsync();

        Assert.True(await db.Set<Xchange>().AnyAsync(x => x.RetryFor == orphan.Id));
        Assert.True(await db.Set<Xchange>().AnyAsync(x => x.RetryFor == healthy.Id));
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

        var delayedRetry = new DelayedRetry
        {
            Id = originalXchange.Id,
            On = DateTime.UtcNow.AddMinutes(-1)
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
