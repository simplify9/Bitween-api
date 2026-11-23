using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Domain;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.Bitween.Services.XchangeStates;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// Exchanges from before the status columns get their labels from a background fill, which works
/// in batches, carries on after a restart, and checks once more what ran meanwhile.
/// </summary>
[Collection("Bitween")]
public class XchangeStateFillTests(BitweenFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>
    /// A finished fill would switch the views to the columns, which the other tests leave as their
    /// seeding finds them. They stay on the result and retry tables.
    /// </summary>
    public async Task DisposeAsync()
    {
        await using var scope = fixture.CreateScope();
        await scope.ServiceProvider.GetRequiredService<BitweenDbContext>().Set<XchangeStateFill>().ExecuteDeleteAsync();
        XchangeStateReadiness.Forget();
    }

    [Fact]
    public async Task The_fill_labels_exchanges_from_before_the_columns_existed()
    {
        var x = await ArrangeAsync("Fill Labels");
        var failed = await x.Exchange(id => new XchangeResult(id, null, null, exception: "boom"));
        var badResponse = await x.Exchange(id => new XchangeResult(id, null, null, new XchangeFile("no", "r.json", true)));
        var success = await x.Exchange(id => new XchangeResult(id, null, null));
        var processing = await x.Exchange(null);
        var retried = await x.Exchange(id => new XchangeResult(id, null, null, exception: "boom"));
        var retry = await x.Retry(retried);
        var flagged = await x.Exchange(id => new XchangeResult(id, null, null));
        await x.ForgetLabels(failed, badResponse, success, processing, retried, retry);
        // Set but with no retry behind it, which the fill must leave alone: it never clears the flag.
        await x.InDb(db => db.Set<Xchange>().Where(e => e.Id == flagged)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Retried, (bool?)true)));

        Assert.Equal(XchangeStateFillPhase.Ready, await RunToTheEndAsync());

        Assert.Equal((XchangeStatus.Failed, (bool?)null), await LabelsOf(failed));
        Assert.Equal((XchangeStatus.BadResponse, (bool?)null), await LabelsOf(badResponse));
        Assert.Equal(((XchangeStatus?)null, (bool?)null), await LabelsOf(success));
        Assert.Equal((XchangeStatus.Processing, (bool?)null), await LabelsOf(processing));
        Assert.Equal((XchangeStatus.Failed, (bool?)true), await LabelsOf(retried));
        Assert.Equal((XchangeStatus.Processing, (bool?)null), await LabelsOf(retry));
        Assert.Equal(((XchangeStatus?)null, (bool?)true), await LabelsOf(flagged));
    }

    [Fact]
    public async Task A_run_carries_on_where_the_last_one_stopped()
    {
        var x = await ArrangeAsync("Fill Resume");
        var ids = new List<string>();
        for (var i = 0; i < 5; i++)
            ids.Add(await x.Exchange(id => new XchangeResult(id, null, null, exception: "boom")));
        await x.ForgetLabels(ids.ToArray());

        // One batch of two, newest first, then stop: what a run cut short by its time budget leaves.
        Assert.Equal(XchangeStateFillPhase.Fill, await RunOnceAsync(TimeSpan.Zero, batchSize: 2));
        Assert.Equal(new XchangeStatus?[] { XchangeStatus.Failed, XchangeStatus.Failed, null, null, null },
            await Task.WhenAll(ids.AsEnumerable().Reverse().Select(async id => (await LabelsOf(id)).Status)));
        var progress = await x.InDb(db => db.Set<XchangeStateFill>().AsNoTracking().SingleAsync());
        Assert.Equal(ids[^2], progress.PositionId);

        // Each in a new scope, as each scheduled run is.
        Assert.Equal(XchangeStateFillPhase.Ready, await RunToTheEndAsync());
        foreach (var id in ids)
            Assert.Equal(XchangeStatus.Failed, (await LabelsOf(id)).Status);
    }

    [Fact]
    public async Task The_recheck_corrects_what_ran_while_the_fill_did()
    {
        var x = await ArrangeAsync("Fill Recheck");

        // Started an hour ago, so everything made below counts as having run during it.
        await x.InDb(async db =>
        {
            db.Add(new XchangeStateFill(DateTime.UtcNow.AddHours(-1)));
            return await db.SaveChangesAsync();
        });

        // Finished by a server without the columns: a result, but still labeled processing.
        var finishedUnlabeled = await x.Exchange(id => new XchangeResult(id, null, null, exception: "boom"));
        // Retried by such a server: a retry exists, but the original isn't flagged.
        var retriedUnflagged = await x.Exchange(id => new XchangeResult(id, null, null, exception: "boom"));
        await x.Retry(retriedUnflagged);
        await x.InDb(db => db.Set<Xchange>().Where(e => e.Id == retriedUnflagged)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Retried, (bool?)null)));

        Assert.Equal(XchangeStatus.Processing, (await LabelsOf(finishedUnlabeled)).Status);

        Assert.Equal(XchangeStateFillPhase.Ready, await RunToTheEndAsync());

        Assert.Equal(XchangeStatus.Failed, (await LabelsOf(finishedUnlabeled)).Status);
        Assert.Equal((XchangeStatus.Failed, (bool?)true), await LabelsOf(retriedUnflagged));
    }

    [Fact]
    public async Task The_fill_builds_a_partial_index_over_the_exchanges_with_a_status()
    {
        var x = await ArrangeAsync("Fill Index");
        // Something to take statistics of, on a database no other test has written to yet.
        await x.Exchange(id => new XchangeResult(id, null, null));
        await x.Exchange(id => new XchangeResult(id, null, null, exception: "boom"));
        Assert.Equal(XchangeStateFillPhase.Ready, await RunToTheEndAsync());

        var definition = await x.InDb(db => db.Database.SqlQueryRaw<string>(
            $"SELECT indexdef AS \"Value\" FROM pg_indexes WHERE schemaname = 'infolink' AND indexname = '{XchangeStateIndex.Name}'")
            .SingleAsync());
        Assert.Contains("(status, started_on, retried)", definition);
        Assert.Contains("WHERE (status IS NOT NULL)", definition);
        Assert.True(await IndexIsValidAsync(x));

        // Statistics on the new columns, without which Postgres guesses how rare each status is.
        Assert.Equal(2, await x.InDb(db => db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*)::int AS \"Value\" FROM pg_stats " +
            "WHERE schemaname = 'infolink' AND tablename = 'xchange' AND attname IN ('status', 'retried')")
            .SingleAsync()));
    }

    [Fact]
    public async Task An_index_left_unusable_by_a_build_cut_short_is_built_again()
    {
        var x = await ArrangeAsync("Fill Index Rebuild");
        Assert.Equal(XchangeStateFillPhase.Ready, await RunToTheEndAsync());

        // What CREATE INDEX CONCURRENTLY leaves when its run stops part-way, and the fill back at the
        // step that builds it, as after the restart that follows.
        await x.InDb(db => db.Database.ExecuteSqlRawAsync(
            $"UPDATE pg_index SET indisvalid = false WHERE indexrelid = 'infolink.{XchangeStateIndex.Name}'::regclass"));
        await x.InDb(db => db.Set<XchangeStateFill>()
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.Phase, XchangeStateFillPhase.Index)));
        Assert.False(await IndexIsValidAsync(x));

        Assert.Equal(XchangeStateFillPhase.Ready, await RunToTheEndAsync());

        Assert.True(await IndexIsValidAsync(x));
        Assert.Equal(0, await x.InDb(db => db.Database.SqlQueryRaw<int>(
            $"SELECT COUNT(*)::int AS \"Value\" FROM pg_indexes WHERE indexname = '{XchangeStateIndex.Name}_invalid'")
            .SingleAsync()));
    }

    [Fact]
    public async Task Progress_says_which_step_the_fill_is_on_and_how_far_it_has_got()
    {
        var x = await ArrangeAsync("Fill Progress");
        Assert.Equal(new XchangeStateProgress("NotStarted", 0, null, 0), await ProgressAsync());

        for (var i = 0; i < 3; i++)
            await x.Exchange(id => new XchangeResult(id, null, null));
        await RunOnceAsync(TimeSpan.Zero, batchSize: 2);
        var filling = await ProgressAsync();
        Assert.Equal("Filling", filling.Phase);
        Assert.InRange(filling.Percent, 0, 99);

        foreach (var (phase, said) in new[]
                 {
                     (XchangeStateFillPhase.Index, "BuildingIndex"),
                     (XchangeStateFillPhase.Recheck, "Rechecking"),
                 })
        {
            await x.InDb(db => db.Set<XchangeStateFill>().ExecuteUpdateAsync(s => s.SetProperty(f => f.Phase, phase)));
            Assert.Equal(new XchangeStateProgress(said, 99, null, 0), await ProgressAsync());
        }

        Assert.Equal(XchangeStateFillPhase.Ready, await RunToTheEndAsync());
        Assert.Equal(new XchangeStateProgress("Done", 100, null, 0), await ProgressAsync());
    }

    // ---------------------------------------------------------------- helpers

    private async Task<XchangeStateProgress> ProgressAsync()
    {
        await using var scope = fixture.CreateScope();
        return await XchangeStateProgress.ReadAsync(scope.ServiceProvider.GetRequiredService<BitweenDbContext>(),
            scope.ServiceProvider.GetRequiredService<BitweenOptions>());
    }

    private static Task<bool> IndexIsValidAsync(Arrangement x) => x.InDb(db => db.Database.SqlQueryRaw<bool>(
        $"SELECT indisvalid AS \"Value\" FROM pg_index WHERE indexrelid = 'infolink.{XchangeStateIndex.Name}'::regclass")
        .SingleAsync());

    private async Task<Arrangement> ArrangeAsync(string name)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        // Every test starts the fill afresh.
        await db.Set<XchangeStateFill>().ExecuteDeleteAsync();

        var doc = new Document(null, $"{name} Doc", DocumentFormat.Json);
        db.Set<Document>().Add(doc);
        await db.SaveChangesAsync();
        var sub = new Subscription($"{name} Sub", doc.Id) { Inactive = false };
        db.Set<Subscription>().Add(sub);
        await db.SaveChangesAsync();
        return new Arrangement(fixture, sub.Id);
    }

    private sealed class Arrangement(BitweenFixture fixture, int subscriptionId)
    {
        /// <summary>
        /// A new exchange with the result <paramref name="resultFor"/> makes saved straight to the table,
        /// as results were before the columns: nothing labels it.
        /// </summary>
        public async Task<string> Exchange(Func<string, XchangeResult>? resultFor)
        {
            await using var scope = fixture.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var xs = scope.ServiceProvider.GetRequiredService<XchangeService>();
            var sub = await db.Set<Subscription>().SingleAsync(s => s.Id == subscriptionId);
            var xchange = await xs.CreateXchange(sub, new XchangeFile("{}"));
            await db.SaveChangesAsync();
            if (resultFor != null)
            {
                db.Add(resultFor(xchange.Id));
                await db.SaveChangesAsync();
            }

            return xchange.Id;
        }

        public async Task<string> Retry(string originalId)
        {
            await using var scope = fixture.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var xs = scope.ServiceProvider.GetRequiredService<XchangeService>();
            var sub = await db.Set<Subscription>().SingleAsync(s => s.Id == subscriptionId);
            var original = await db.Set<Xchange>().SingleAsync(e => e.Id == originalId);
            await xs.CreateXchange(sub, original, new XchangeFile("{}"));
            await db.SaveChangesAsync();
            return await db.Set<Xchange>().Where(e => e.RetryFor == originalId).Select(e => e.Id).SingleAsync();
        }

        /// <summary>Blanks both labels, as on an exchange from before the columns.</summary>
        public Task<int> ForgetLabels(params string[] ids) => InDb(db => db.Set<Xchange>()
            .Where(e => ids.Contains(e.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Status, (XchangeStatus?)null)
                .SetProperty(e => e.Retried, (bool?)null)));

        public async Task<T> InDb<T>(Func<BitweenDbContext, Task<T>> act)
        {
            await using var scope = fixture.CreateScope();
            return await act(scope.ServiceProvider.GetRequiredService<BitweenDbContext>());
        }
    }

    private async Task<XchangeStateFillPhase> RunOnceAsync(TimeSpan budget, int batchSize = XchangeStateFiller.BatchSize)
    {
        await using var scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<XchangeStateFiller>().RunAsync(budget, batchSize);
    }

    private async Task<XchangeStateFillPhase> RunToTheEndAsync()
    {
        var phase = XchangeStateFillPhase.Fill;
        for (var run = 0; run < 20 && phase != XchangeStateFillPhase.Ready; run++)
            phase = await RunOnceAsync(TimeSpan.FromSeconds(30));
        return phase;
    }

    private async Task<(XchangeStatus? Status, bool? Retried)> LabelsOf(string id)
    {
        await using var scope = fixture.CreateScope();
        var xchange = await scope.ServiceProvider.GetRequiredService<BitweenDbContext>()
            .Set<Xchange>().AsNoTracking().SingleAsync(e => e.Id == id);
        return (xchange.Status, xchange.Retried);
    }
}
