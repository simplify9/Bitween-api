using System;
using System.Collections.Generic;
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
public class DelayedRetriesTests
{
    private readonly BitweenFixture _fixture;

    public DelayedRetriesTests(BitweenFixture fixture)
    {
        _fixture = fixture;
    }

    private static SearchyRequest EmptySearch() => new()
    {
        PageSize = 50,
        PageIndex = 0
    };

    private async Task<(Document doc, Subscription sub, Xchange xchange)> CreateSubscriptionWithXchange(
        BitweenDbContext db, XchangeService xs, int docId, string name)
    {
        var doc = new Document(docId, name);
        db.Set<Document>().Add(doc);
        await db.SaveChangesAsync();

        var sub = new Subscription(name, doc.Id);
        sub.Inactive = false;
        db.Set<Subscription>().Add(sub);
        await db.SaveChangesAsync();

        var xchange = await xs.CreateXchange(sub, new XchangeFile("{}"));
        await db.SaveChangesAsync();

        return (doc, sub, xchange);
    }

    // ─── Manual retry guard ─────────────────────────────────────────────────

    [Fact]
    public async Task Retry_throws_when_auto_retry_already_scheduled()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var xs = scope.ServiceProvider.GetRequiredService<XchangeService>();
        var (_, _, xchange) = await CreateSubscriptionWithXchange(db, xs, 9001, "Retry Guard Doc");

        db.Set<DelayedRetry>().Add(new DelayedRetry { Id = xchange.Id, On = DateTime.UtcNow.AddMinutes(5) });
        await db.SaveChangesAsync();

        var retry = new SW.Bitween.Resources.Xchanges.Retry(db, xs);

        await Assert.ThrowsAsync<SWValidationException>(() =>
            retry.Handle(xchange.Id, new XchangeRetry { Reset = false }));
    }

    [Fact]
    public async Task Retry_succeeds_when_no_auto_retry_scheduled()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var xs = scope.ServiceProvider.GetRequiredService<XchangeService>();
        var (_, _, xchange) = await CreateSubscriptionWithXchange(db, xs, 9002, "Retry OK Doc");

        var retry = new SW.Bitween.Resources.Xchanges.Retry(db, xs);
        await retry.Handle(xchange.Id, new XchangeRetry { Reset = false });

        var retryXchange = await db.Set<Xchange>().FirstOrDefaultAsync(x => x.RetryFor == xchange.Id);
        Assert.NotNull(retryXchange);
    }

    [Fact]
    public async Task BulkRetry_skips_ids_with_scheduled_auto_retry_and_processes_others()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var xs = scope.ServiceProvider.GetRequiredService<XchangeService>();

        var (_, _, xchangeScheduled) = await CreateSubscriptionWithXchange(db, xs, 9003, "Bulk Scheduled Doc");
        var (_, _, xchangeFree) = await CreateSubscriptionWithXchange(db, xs, 9004, "Bulk Free Doc");

        db.Set<DelayedRetry>().Add(new DelayedRetry { Id = xchangeScheduled.Id, On = DateTime.UtcNow.AddMinutes(5) });
        await db.SaveChangesAsync();

        var bulkRetry = new SW.Bitween.Resources.Xchanges.BulkRetry(db, xs);
        await bulkRetry.Handle(new XchangeBulkRetry
        {
            Reset = false,
            Ids = [xchangeScheduled.Id, xchangeFree.Id]
        });

        var retriedScheduled = await db.Set<Xchange>().AnyAsync(x => x.RetryFor == xchangeScheduled.Id);
        var retriedFree = await db.Set<Xchange>().AnyAsync(x => x.RetryFor == xchangeFree.Id);

        Assert.False(retriedScheduled, "An xchange with a scheduled auto-retry must be skipped by bulk retry.");
        Assert.True(retriedFree, "An xchange without a scheduled auto-retry must still be retried by bulk retry.");
    }

    // ─── DelayedRetries search ──────────────────────────────────────────────

    [Fact]
    public async Task DelayedRetries_Search_returns_expected_row()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var xs = scope.ServiceProvider.GetRequiredService<XchangeService>();
        var (doc, sub, xchange) = await CreateSubscriptionWithXchange(db, xs, 9005, "Search Row Doc");

        var scheduledOn = DateTime.UtcNow.AddMinutes(10);
        db.Set<DelayedRetry>().Add(new DelayedRetry { Id = xchange.Id, On = scheduledOn });
        await db.SaveChangesAsync();

        var search = new SW.Bitween.Resources.DelayedRetries.Search(db, scope.Superuser());
        var response = (SearchyResponse<DelayedRetryRow>)await search.Handle(EmptySearch());

        var row = response.Result.FirstOrDefault(r => r.Id == xchange.Id);
        Assert.NotNull(row);
        Assert.Equal(sub.Id, row.SubscriptionId);
        Assert.Equal(sub.Name, row.SubscriptionName);
        Assert.Equal(doc.Id, row.DocumentId);
        Assert.Equal(doc.Name, row.DocumentName);
        Assert.Equal(scheduledOn, row.On, TimeSpan.FromSeconds(1));
    }

    // ─── Run now ────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunNow_executes_immediately_even_when_not_yet_due_and_removes_record()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var xs = scope.ServiceProvider.GetRequiredService<XchangeService>();
        var ctx = scope.Superuser();
        var (_, _, xchange) = await CreateSubscriptionWithXchange(db, xs, 9006, "Run Now Doc");

        // Scheduled an hour from now — RunNow must still execute it immediately.
        db.Set<DelayedRetry>().Add(new DelayedRetry { Id = xchange.Id, On = DateTime.UtcNow.AddHours(1) });
        await db.SaveChangesAsync();

        var runNow = new SW.Bitween.Resources.DelayedRetries.RunNow(db, ctx, xs);
        await runNow.Handle(xchange.Id, new DelayedRetryRunNow());

        var stillScheduled = await db.Set<DelayedRetry>().AnyAsync(d => d.Id == xchange.Id);
        Assert.False(stillScheduled, "RunNow must remove the DelayedRetry record.");

        var retryXchange = await db.Set<Xchange>().FirstOrDefaultAsync(x => x.RetryFor == xchange.Id);
        Assert.NotNull(retryXchange);
    }

    [Fact]
    public async Task RunNow_throws_when_nothing_is_scheduled()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var xs = scope.ServiceProvider.GetRequiredService<XchangeService>();
        var ctx = scope.Superuser();

        var runNow = new SW.Bitween.Resources.DelayedRetries.RunNow(db, ctx, xs);

        await Assert.ThrowsAsync<SWValidationException>(() =>
            runNow.Handle("rjt-nonexistent-" + Guid.NewGuid().ToString("N")[..8], new DelayedRetryRunNow()));
    }

    // ─── Xchanges search surfacing ──────────────────────────────────────────

    [Fact]
    public async Task Xchanges_Search_includes_ScheduledRetryOn_when_delayed_retry_exists()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var xs = scope.ServiceProvider.GetRequiredService<XchangeService>();
        var (_, _, xchange) = await CreateSubscriptionWithXchange(db, xs, 9007, "Xchange Search Scheduled Doc");

        var scheduledOn = DateTime.UtcNow.AddMinutes(15);
        db.Set<DelayedRetry>().Add(new DelayedRetry { Id = xchange.Id, On = scheduledOn });
        await db.SaveChangesAsync();

        var search = new SW.Bitween.Resources.Xchanges.Search(db, xs, scope.Superuser());
        var response = (SearchyResponse<XchangeRow>)await search.Handle(EmptySearch());

        var row = response.Result.FirstOrDefault(r => r.Id == xchange.Id);
        Assert.NotNull(row);
        Assert.NotNull(row.ScheduledRetryOn);
        Assert.Equal(scheduledOn, row.ScheduledRetryOn!.Value, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Xchanges_Search_has_null_ScheduledRetryOn_when_no_delayed_retry_exists()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var xs = scope.ServiceProvider.GetRequiredService<XchangeService>();
        var (_, _, xchange) = await CreateSubscriptionWithXchange(db, xs, 9008, "Xchange Search Unscheduled Doc");

        var search = new SW.Bitween.Resources.Xchanges.Search(db, xs, scope.Superuser());
        var response = (SearchyResponse<XchangeRow>)await search.Handle(EmptySearch());

        var row = response.Result.FirstOrDefault(r => r.Id == xchange.Id);
        Assert.NotNull(row);
        Assert.Null(row.ScheduledRetryOn);
    }
}
