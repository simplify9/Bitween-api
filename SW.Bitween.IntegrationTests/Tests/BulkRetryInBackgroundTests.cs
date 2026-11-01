using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Domain;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.Bitween.Services.Background;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// A bulk retry the admin UI carries on in the background: the whole selection at once rather than in
/// rounds of 500, saved as it goes, saying how far it got, and stopping between exchanges when cancelled.
/// </summary>
[Collection("Bitween")]
public class BulkRetryInBackgroundTests(BitweenFixture fixture)
{
    /// <summary>One more than a retry asked directly takes.</summary>
    const int PastOneRound = 501;

    [Fact]
    public async Task Asked_directly_it_still_takes_one_round_at_most()
    {
        await using var scope = fixture.CreateScope();
        var (db, xs, ctx) = Services(scope);
        var ids = await FailedXchangesAsync(db, xs, PastOneRound);

        var refused = await Assert.ThrowsAsync<SWValidationException>(() =>
            new Resources.Xchanges.BulkRetry(db, ctx, xs).Handle(new XchangeBulkRetry { Ids = ids }));

        Assert.Contains(refused.Validations, v => v.Key == "TOO_MANY");
    }

    [Fact]
    public async Task In_the_background_it_takes_the_whole_selection_and_says_how_far_it_got()
    {
        await using var scope = fixture.CreateScope();
        var (db, xs, ctx) = Services(scope);
        var ids = await FailedXchangesAsync(db, xs, PastOneRound);
        var said = new List<(int Done, int Total, string Summary)>();

        using var stop = new CancellationTokenSource();
        BackgroundRequestScope.Open(DateTime.UtcNow + BackgroundRequests.Limit, stop.Token, default,
            (done, total, summary) => said.Add((done, total, summary)));
        var plan = (XchangeBulkRetryPlan)await new Resources.Xchanges.BulkRetry(db, ctx, xs)
            .Handle(new XchangeBulkRetry { Ids = ids });

        Assert.Equal(PastOneRound, plan.WillRetry);
        Assert.Null(plan.StoppedAfter);
        Assert.Equal(PastOneRound, await db.Set<Xchange>().CountAsync(x => ids.Contains(x.RetryFor)));
        // Once at the start, after each save of a hundred, and at the end.
        Assert.Equal([0, 100, 200, 300, 400, 500, PastOneRound], said.Select(s => s.Done));
        Assert.All(said, s => Assert.Equal(PastOneRound, s.Total));
        Assert.Equal($"{PastOneRound} retried, 0 skipped", said[^1].Summary);
    }

    [Fact]
    public async Task Cancelled_it_stops_between_exchanges_and_keeps_what_it_did()
    {
        await using var scope = fixture.CreateScope();
        var (db, xs, ctx) = Services(scope);
        var ids = await FailedXchangesAsync(db, xs, 250);

        using var stop = new CancellationTokenSource();
        using var cancel = new CancellationTokenSource();
        // Cancelled by its owner once the first hundred are saved, as the header's Cancel would.
        BackgroundRequestScope.Open(DateTime.UtcNow + BackgroundRequests.Limit, stop.Token, cancel.Token,
            (done, _, _) => { if (done >= 100) cancel.Cancel(); });
        var plan = (XchangeBulkRetryPlan)await new Resources.Xchanges.BulkRetry(db, ctx, xs)
            .Handle(new XchangeBulkRetry { Ids = ids });

        Assert.Equal(100, plan.StoppedAfter);
        Assert.Equal(100, plan.WillRetry);
        Assert.Equal(100, await db.Set<Xchange>().CountAsync(x => ids.Contains(x.RetryFor)));
    }

    [Fact]
    public async Task It_goes_at_the_pipelines_pace_holding_back_while_too_many_wait()
    {
        var (waiting, lookEvery) = (Resources.Xchanges.BulkRetry.Waiting, Resources.Xchanges.BulkRetry.LookEvery);
        Resources.Xchanges.BulkRetry.Waiting = 150;
        Resources.Xchanges.BulkRetry.LookEvery = TimeSpan.FromMilliseconds(50);
        try
        {
            await using var scope = fixture.CreateScope();
            var (db, xs, ctx) = Services(scope);
            var ids = await FailedXchangesAsync(db, xs, 250);
            var said = new List<string>();

            using var stop = new CancellationTokenSource();
            var retrying = Task.Run(async () =>
            {
                BackgroundRequestScope.Open(DateTime.UtcNow + BackgroundRequests.Limit, stop.Token, default,
                    (_, _, summary) => { lock (said) said.Add(summary); });
                return (XchangeBulkRetryPlan)await new Resources.Xchanges.BulkRetry(db, ctx, xs)
                    .Handle(new XchangeBulkRetry { Ids = ids });
            });

            // A second save of a hundred would make 200 waiting, past the 150 allowed: it holds at 100.
            await using var check = fixture.CreateScope();
            var checkDb = check.ServiceProvider.GetRequiredService<BitweenDbContext>();
            for (var i = 0; i < 100 && await Retries(checkDb, ids) < 100; i++) await Task.Delay(50);
            await Task.Delay(300);
            Assert.Equal(100, await Retries(checkDb, ids));
            Assert.False(retrying.IsCompleted);

            // The pipeline finishes what it sends, and it carries on to the end.
            var processed = new HashSet<string>();
            for (var i = 0; i < 300 && !retrying.IsCompleted; i++)
            {
                var made = await checkDb.Set<Xchange>().Where(x => ids.Contains(x.RetryFor)).Select(x => x.Id).ToListAsync();
                checkDb.AddRange(made.Where(processed.Add).Select(id => new XchangeResult(id, null, null, exception: "processed")));
                await checkDb.SaveChangesAsync();
                await Task.Delay(100);
            }

            var plan = await retrying.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(250, plan.WillRetry);
            Assert.Equal(250, await Retries(checkDb, ids));
            Assert.Contains(said, s => s.Contains("waiting for the pipeline"));
        }
        finally
        {
            (Resources.Xchanges.BulkRetry.Waiting, Resources.Xchanges.BulkRetry.LookEvery) = (waiting, lookEvery);
        }
    }

    [Fact]
    public async Task A_plan_counts_every_skip_and_names_the_first_thousand()
    {
        await using var scope = fixture.CreateScope();
        var (db, xs, ctx) = Services(scope);
        var ids = await FailedXchangesAsync(db, xs, 1_005);
        // Succeeded since, so all of them are skipped.
        await db.Set<XchangeResult>().Where(r => ids.Contains(r.Id))
            .ExecuteUpdateAsync(u => u.SetProperty(r => r.Success, true));

        var plan = (XchangeBulkRetryPlan)await new Resources.Xchanges.BulkRetryPreview(db, ctx)
            .Handle(new XchangeBulkRetry { Ids = ids });

        Assert.Equal(1_005, plan.SkippedCount);
        Assert.Equal(1_000, plan.Skipped.Count);
        Assert.Equal(1_000, plan.Listed);
        Assert.Equal(0, plan.WillRetry);
    }

    static Task<int> Retries(BitweenDbContext db, List<string> ids) =>
        db.Set<Xchange>().CountAsync(x => ids.Contains(x.RetryFor));

    static (BitweenDbContext Db, XchangeService Xs, RequestContext Ctx) Services(AsyncServiceScope scope) =>
        (scope.ServiceProvider.GetRequiredService<BitweenDbContext>(),
            scope.ServiceProvider.GetRequiredService<XchangeService>(),
            scope.Superuser());

    /// <summary>Exchanges that failed, on a subscription of their own, by id.</summary>
    static async Task<List<string>> FailedXchangesAsync(BitweenDbContext db, XchangeService xs, int count)
    {
        var name = $"Bulk background {Guid.NewGuid():N}"[..30];
        var doc = new Document(null, name, DocumentFormat.Json);
        db.Set<Document>().Add(doc);
        await db.SaveChangesAsync();
        var sub = new Subscription(name, doc.Id) { Inactive = false };
        db.Set<Subscription>().Add(sub);
        await db.SaveChangesAsync();

        var ids = new List<string>();
        for (var i = 0; i < count; i++)
        {
            var xchange = await xs.CreateXchange(sub, new XchangeFile($"{{\"n\":{i}}}"));
            db.Set<XchangeResult>().Add(new XchangeResult(xchange.Id, null, null, exception: "boom"));
            ids.Add(xchange.Id);
        }

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return ids;
    }
}
