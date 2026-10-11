using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Domain;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.Bitween.Resources.Xchanges;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// The exchange list's newer filters (retries, scheduled retries, aggregated, error text) and the
/// summary over the list: per-status counts under every filter but the status itself, and a
/// histogram of when the matching exchanges started.
/// </summary>
/// <remarks>
/// Every test scopes itself to an information type of its own with a <c>DocumentId</c> filter: the
/// collection shares one database, so a count over anything wider would count other tests' rows.
/// </remarks>
[Collection("Bitween")]
public class ExchangeSummaryTests(BitweenFixture fixture)
{
    /// <summary>One exchange of each status on a fresh information type, plus a second failure.</summary>
    private sealed record Seeded(int DocumentId, Subscription Subscription, Xchange Running, Xchange Succeeded,
        Xchange BadResponse, Xchange Failed, Xchange FailedAgain);

    private static async Task<Seeded> OneOfEach(BitweenDbContext db, XchangeService xs, string name)
    {
        var doc = new Document(null, name, DocumentFormat.Json);
        db.Set<Document>().Add(doc);
        await db.SaveChangesAsync();

        var sub = new Subscription(name, doc.Id) { Inactive = false };
        db.Set<Subscription>().Add(sub);
        await db.SaveChangesAsync();

        var running = await xs.CreateXchange(sub, new XchangeFile("{}"));
        var succeeded = await xs.CreateXchange(sub, new XchangeFile("{}"));
        var badResponse = await xs.CreateXchange(sub, new XchangeFile("{}"));
        var failed = await xs.CreateXchange(sub, new XchangeFile("{}"));
        var failedAgain = await xs.CreateXchange(sub, new XchangeFile("{}"));
        await db.SaveChangesAsync();

        db.Set<XchangeResult>().AddRange(
            new XchangeResult(succeeded.Id, null, null),
            new XchangeResult(badResponse.Id, null, null, new XchangeFile("{}", "response.json", true)),
            new XchangeResult(failed.Id, null, null, exception: "Timeout Talking To ACME after 30s"),
            new XchangeResult(failedAgain.Id, null, null, exception: "Mapping failed: no such field"));
        await db.SaveChangesAsync();

        return new Seeded(doc.Id, sub, running, succeeded, badResponse, failed, failedAgain);
    }

    private static async Task SetStartedOn(BitweenDbContext db, string id, DateTime startedOn) =>
        await db.Set<Xchange>().Where(x => x.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.StartedOn, startedOn));

    private static async Task<XchangeSummary> Summarise(AsyncServiceScope scope, params string[] filters)
    {
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        return (XchangeSummary)await new Summary(db, scope.Superuser()).Handle(new SearchyRequest(filters));
    }

    private static async Task<List<string>> Search(AsyncServiceScope scope, params string[] filters)
    {
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var xs = scope.ServiceProvider.GetRequiredService<XchangeService>();
        var response = (SearchyResponse<XchangeRow>)await new Resources.Xchanges.Search(db, xs, scope.Superuser())
            .Handle(new SearchyRequest(filters) { PageSize = 50 });
        Assert.Equal(response.Result.Count(), response.TotalCount);
        return response.Result.Select(r => r.Id).ToList();
    }

    /// <summary>What "select all matching" would act on for the same filter, as bulk retry and export resolve it.</summary>
    private static async Task<List<string>> Select(AsyncServiceScope scope, string filter)
    {
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        return await new XchangeSelection(db).Resolve(new XchangeFilesExport { Filter = filter }, 100);
    }

    // ─── The new filters ──────────────────────────────────────────────────────

    [Fact]
    public async Task IsRetry_keeps_only_attempts_made_from_another_exchange()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var xs = scope.ServiceProvider.GetRequiredService<XchangeService>();

        var seeded = await OneOfEach(db, xs, "Summary IsRetry Doc");
        await new Retry(db, scope.Superuser(), xs).Handle(seeded.Failed.Id, new XchangeRetry { Reset = false });
        await db.SaveChangesAsync();
        var retry = await db.Set<Xchange>().SingleAsync(x => x.RetryFor == seeded.Failed.Id);

        var doc = $"DocumentId:1:{seeded.DocumentId}";
        Assert.Equal([retry.Id], await Search(scope, doc, "IsRetry:1:true"));
        Assert.Equal([retry.Id], await Select(scope, $"filter={doc}&filter=IsRetry:1:1"));

        // Off is no filter, as LatestOnly:1:false is.
        Assert.Equal(6, (await Search(scope, doc, "IsRetry:1:false")).Count);
    }

    [Fact]
    public async Task HasScheduledRetry_keeps_only_exchanges_with_a_pending_delayed_retry()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var xs = scope.ServiceProvider.GetRequiredService<XchangeService>();

        var seeded = await OneOfEach(db, xs, "Summary Scheduled Doc");
        // A day out, so the retry job cannot pick it up and delete the row mid-test.
        db.Set<DelayedRetry>().Add(new DelayedRetry { Id = seeded.Failed.Id, On = DateTime.UtcNow.AddDays(1) });
        await db.SaveChangesAsync();

        var doc = $"DocumentId:1:{seeded.DocumentId}";
        Assert.Equal([seeded.Failed.Id], await Search(scope, doc, "HasScheduledRetry:1:true"));
        Assert.Equal([seeded.Failed.Id], await Select(scope, $"filter={doc}&filter=HasScheduledRetry:1:true"));
        Assert.Equal(5, (await Search(scope, doc, "HasScheduledRetry:1:no")).Count);
    }

    [Fact]
    public async Task Aggregated_keeps_only_exchanges_rolled_up_into_an_aggregation()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var xs = scope.ServiceProvider.GetRequiredService<XchangeService>();

        var seeded = await OneOfEach(db, xs, "Summary Aggregated Doc");
        db.Set<XchangeAggregation>().Add(new XchangeAggregation(seeded.Succeeded.Id, seeded.Running.Id));
        await db.SaveChangesAsync();

        var doc = $"DocumentId:1:{seeded.DocumentId}";
        Assert.Equal([seeded.Succeeded.Id], await Search(scope, doc, "Aggregated:1:true"));
        Assert.Equal([seeded.Succeeded.Id], await Select(scope, $"filter={doc}&filter=Aggregated:1:true"));
    }

    [Fact]
    public async Task ErrorContains_matches_error_text_whatever_its_case()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var xs = scope.ServiceProvider.GetRequiredService<XchangeService>();

        var seeded = await OneOfEach(db, xs, "Summary Error Doc");

        var doc = $"DocumentId:1:{seeded.DocumentId}";
        Assert.Equal([seeded.Failed.Id], await Search(scope, doc, "ErrorContains:4:talking to acme"));
        Assert.Equal([seeded.Failed.Id], await Select(scope, $"filter={doc}&filter=ErrorContains:4:TIMEOUT"));

        // Exchanges with no error at all are not matched by any text, and both failures are by
        // text they share.
        Assert.Equal(new[] { seeded.Failed.Id, seeded.FailedAgain.Id }.OrderBy(i => i),
            (await Search(scope, doc, "ErrorContains:4:A")).OrderBy(i => i));
    }

    // ─── The summary ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Summary_counts_every_status_and_ignores_a_status_filter()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var xs = scope.ServiceProvider.GetRequiredService<XchangeService>();

        var seeded = await OneOfEach(db, xs, "Summary Counts Doc");

        // The failed tab is selected; the summary still answers for all four tabs.
        var summary = await Summarise(scope, $"DocumentId:1:{seeded.DocumentId}", "StatusFilter:1:3");

        Assert.Equal(1, summary.Counts.Processing);
        Assert.Equal(1, summary.Counts.Success);
        Assert.Equal(1, summary.Counts.BadResponse);
        Assert.Equal(2, summary.Counts.Failed);

        // No date range, so nothing to divide into buckets.
        Assert.Empty(summary.Buckets);
        Assert.Equal(0, summary.BucketMinutes);

        // The other special filters still apply.
        var narrowed = await Summarise(scope, $"DocumentId:1:{seeded.DocumentId}", "ErrorContains:4:mapping");
        Assert.Equal(0, narrowed.Counts.Processing + narrowed.Counts.Success + narrowed.Counts.BadResponse);
        Assert.Equal(1, narrowed.Counts.Failed);
    }

    [Fact]
    public async Task Summary_buckets_a_range_by_status_zero_filled_and_aligned_to_the_clock()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var xs = scope.ServiceProvider.GetRequiredService<XchangeService>();

        var seeded = await OneOfEach(db, xs, "Summary Buckets Doc");
        var day = new DateTime(2025, 3, 4, 0, 0, 0, DateTimeKind.Utc);
        await SetStartedOn(db, seeded.Failed.Id, day.AddHours(10).AddMinutes(8));
        await SetStartedOn(db, seeded.Succeeded.Id, day.AddHours(10).AddMinutes(9));
        await SetStartedOn(db, seeded.Running.Id, day.AddHours(11).AddMinutes(31));
        await SetStartedOn(db, seeded.BadResponse.Id, day.AddHours(11).AddMinutes(59));
        // Outside the range: in neither the counts nor the buckets.
        await SetStartedOn(db, seeded.FailedAgain.Id, day.AddHours(13));

        // 10:07 to 12:00 is 114 one-minute buckets; five minutes, from 10:05, is 24.
        var summary = await Summarise(scope, $"DocumentId:1:{seeded.DocumentId}",
            "StartedOn:6:2025-03-04T10:07:00Z", "StartedOn:8:2025-03-04T12:00:00Z");

        Assert.Equal(5, summary.BucketMinutes);
        Assert.Equal(24, summary.Buckets.Count);
        Assert.Equal(day.AddHours(10).AddMinutes(5), summary.Buckets[0].Start);
        Assert.Equal(day.AddHours(12), summary.Buckets[^1].Start);
        Assert.All(summary.Buckets.Skip(1).Zip(summary.Buckets),
            pair => Assert.Equal(TimeSpan.FromMinutes(5), pair.First.Start - pair.Second.Start));

        var first = summary.Buckets[0];
        Assert.Equal((0, 1, 0, 1), (first.Processing, first.Success, first.BadResponse, first.Failed));
        Assert.Equal(1, summary.Buckets.Single(b => b.Start == day.AddHours(11).AddMinutes(30)).Processing);
        Assert.Equal(1, summary.Buckets.Single(b => b.Start == day.AddHours(11).AddMinutes(55)).BadResponse);
        Assert.Equal(4, summary.Buckets.Sum(b => b.Processing + b.Success + b.BadResponse + b.Failed));

        Assert.Equal(1, summary.Counts.Failed);
    }

    [Fact]
    public async Task Summary_groups_by_hour_and_by_day_for_longer_ranges()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var xs = scope.ServiceProvider.GetRequiredService<XchangeService>();

        var seeded = await OneOfEach(db, xs, "Summary Long Range Doc");
        var day = new DateTime(2025, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        await SetStartedOn(db, seeded.Failed.Id, day.AddHours(5).AddMinutes(40));
        await SetStartedOn(db, seeded.FailedAgain.Id, day.AddDays(1).AddHours(23).AddMinutes(59));
        await SetStartedOn(db, seeded.Succeeded.Id, day.AddDays(2).AddHours(3));
        await SetStartedOn(db, seeded.Running.Id, day.AddDays(20));
        await SetStartedOn(db, seeded.BadResponse.Id, day.AddDays(29).AddHours(12));

        var doc = $"DocumentId:1:{seeded.DocumentId}";

        // Three days: 73 hours is too many, two-hour buckets are 37 - grouped by the hour.
        var threeDays = await Summarise(scope, doc, "StartedOn:6:2025-05-01T00:00:00Z", "StartedOn:8:2025-05-04T00:00:00Z");
        Assert.Equal(120, threeDays.BucketMinutes);
        Assert.Equal(37, threeDays.Buckets.Count);
        Assert.Equal(1, threeDays.Buckets.Single(b => b.Start == day.AddHours(4)).Failed);
        Assert.Equal(1, threeDays.Buckets.Single(b => b.Start == day.AddDays(1).AddHours(22)).Failed);
        Assert.Equal(1, threeDays.Buckets.Single(b => b.Start == day.AddDays(2).AddHours(2)).Success);
        Assert.Equal(3, threeDays.Buckets.Sum(b => b.Processing + b.Success + b.BadResponse + b.Failed));

        // Five weeks: 71 half-days is too many, so a bucket a day - grouped by the day.
        var month = await Summarise(scope, doc, "StartedOn:6:2025-05-01T00:00:00Z", "StartedOn:8:2025-06-05T00:00:00Z");
        Assert.Equal(1440, month.BucketMinutes);
        Assert.Equal(36, month.Buckets.Count);
        Assert.Equal(1, month.Buckets[0].Failed);
        Assert.Equal(1, month.Buckets[1].Failed);
        Assert.Equal(1, month.Buckets[2].Success);
        Assert.Equal(1, month.Buckets[20].Processing);
        Assert.Equal(1, month.Buckets[29].BadResponse);
    }
}
