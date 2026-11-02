using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Xchanges;

/// <summary>
/// The exchange list's filter, summarised: how many exchanges are in each status, and, over a date
/// range, when they started. <c>GET /api/xchanges/summary</c>, with the same <c>filter=</c>
/// parameters the search takes.
/// </summary>
/// <remarks>
/// <para>
/// A status filter in the request is ignored. This is what the status tabs above the list show -
/// each status's count under every other filter - so applying the selected tab first would turn
/// the other three into zeros.
/// </para>
/// <para>
/// Filters through <see cref="XchangeFilters.FilteredRowsAsync"/>, the lean query bulk retry and
/// export select with, rather than the search's own projection: nothing here needs a file link or
/// a promoted value, and the search's projection builds those in C#, which cannot be grouped on.
/// Every special filter the search understands is applied the same way, so a count is never of a
/// different set than the list it sits over.
/// </para>
/// <para>
/// A count or a histogram that takes longer than <see cref="Budget"/> is finished in the background
/// by <see cref="SlowCounts"/>, which keeps it for the next ask: until then it is missing from the
/// answer, and <see cref="XchangeSummary.Counting"/> says so.
/// </para>
/// </remarks>
[HandlerName("summary")]
public class Summary(BitweenDbContext dbContext, RequestContext requestContext, SlowCounts slowCounts) : ISearchyHandler
{
    /// <summary>The status numbers <see cref="XchangeFilters.WhereStatus"/> reads.</summary>
    private const string Processing = "0", Success = "1", BadResponse = "2", Failed = "3";

    public async Task<object> Handle(SearchyRequest searchyRequest, bool lookup = false, string searchPhrase = null)
    {
        // The same read the list itself is: whoever may see the exchanges, or the dashboard that
        // summarises them, may see how many there are.
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Exchanges.View, Model.Permissions.Dashboard.View);

        // Removed before anything else reads the request, from every condition rather than only
        // the first the special filters look at: a status filter left in a later condition would
        // reach Searchy as a column, and XchangeRow.StatusFilter is not one the database has.
        foreach (var condition in searchyRequest.Conditions)
            foreach (var statusFilter in condition.Filters.Where(f => f.Field == "StatusFilter").ToList())
                condition.Filters.Remove(statusFilter);

        // Taken before FilteredRowsAsync takes the special filters out of the request: a count finished
        // in the background filters again from this, with a database of its own.
        var asked = (SearchyRequest)searchyRequest.Clone();
        var view = ViewKey(searchyRequest, DateTime.UtcNow);

        var rows = await XchangeFilters.FilteredRowsAsync(dbContext, searchyRequest);

        // Read after FilteredRowsAsync, which is what moves the request's dates to UTC. The
        // StartedOn filters are plain columns, so Searchy has applied them and left them in place.
        var (rangeStart, rangeEnd) = StartedOnRange(searchyRequest);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadUncommitted);

        // One capped count per status rather than a single grouped count: each can stop at the
        // cap, as the search's total does, where a grouping has to visit every matching row. Four
        // short queries over a broad filter cost far less than one exhaustive one.
        //
        // All four share one budget, so the answer never waits longer than it, however many are slow.
        // The ones with an index of their own go first; still running, which can't have one, last.
        var counts = new XchangeStatusCounts();
        using (var deadline = new CancellationTokenSource(Budget))
        {
            counts.BadResponse = await Count(BadResponse, deadline.Token);
            counts.Failed = await Count(Failed, deadline.Token);
            counts.Success = await Count(Success, deadline.Token);
            counts.Processing = await Count(Processing, deadline.Token);
        }

        var summary = new XchangeSummary
        {
            Counts = counts,
            Counting = counts.Processing == null || counts.Success == null || counts.BadResponse == null ||
                       counts.Failed == null
        };
        if (rangeStart is not { } start)
            return summary;

        var end = rangeEnd ?? DateTime.UtcNow;
        if (end < start)
            return summary;

        var bucketMinutes = XchangeHistogram.ChooseBucketMinutes(start, end);
        using var histogramDeadline = new CancellationTokenSource(Budget);
        var started = await Answer($"histogram:{bucketMinutes}|{view}", histogramDeadline.Token,
            ct => StartedCounts(rows, bucketMinutes, ct),
            async (db, ct) => await StartedCounts(await XchangeFilters.FilteredRowsAsync(db, Copy(asked)), bucketMinutes, ct));
        if (started == null)
        {
            summary.Counting = true;
            return summary;
        }

        summary.BucketMinutes = bucketMinutes;
        summary.Buckets = XchangeHistogram.Fold(start, end, bucketMinutes, started);
        return summary;

        Task<int?> Count(string status, CancellationToken deadline) =>
            Answer($"count:{status}|{view}", deadline,
                ct => CountCapped(rows, status, dbContext, ct),
                async (db, ct) => await CountCapped(await XchangeFilters.FilteredRowsAsync(db, Copy(asked)), status, db, ct));
    }

    /// <summary>
    /// The answer <see cref="SlowCounts"/> keeps for <paramref name="key"/>, when it is one that was too
    /// slow before. Otherwise worked out <paramref name="here"/> within the budget, and when that runs
    /// out, left to <see cref="SlowCounts"/> to work out <paramref name="later"/>: null until then.
    /// </summary>
    private async Task<T> Answer<T>(string key, CancellationToken deadline, Func<CancellationToken, Task<T>> here,
        Func<BitweenDbContext, CancellationToken, Task<T>> later)
    {
        if (slowCounts.TryGet(key, later, out var kept))
            return kept;

        var answer = await WithinBudget(dbContext, deadline, here);
        if (answer == null)
            slowCounts.Start(key, later);
        return answer;
    }

    private static SearchyRequest Copy(SearchyRequest request) => (SearchyRequest)request.Clone();

    /// <summary>
    /// The filters as text, naming what was counted for <see cref="SlowCounts"/>. A start with no end -
    /// what "last 24 hours" sends, a little later on every ask - is named by how far back it reaches,
    /// to five minutes, so the asks of one page find the one count rather than each starting another.
    /// </summary>
    internal static string ViewKey(SearchyRequest request, DateTime now) =>
        string.Join("|", request.Conditions.Select(condition =>
        {
            var openEnded = !condition.Filters.Any(f =>
                f.Field == nameof(XchangeRow.StartedOn) && f.Rule == SearchyRule.LessThanOrEquals);
            return string.Join("&", condition.Filters
                .Select(f =>
                    openEnded && f.Field == nameof(XchangeRow.StartedOn) && f.Rule == SearchyRule.GreaterThanOrEquals &&
                    DateTime.TryParse(f.Value?.ToString(), CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var since)
                        ? $"StartedOn:since:{Math.Round((now - since).TotalMinutes / 5)}"
                        : f.ToString())
                .OrderBy(f => f, StringComparer.Ordinal));
        }));

    /// <summary>How long the counts together, and then the histogram, may take before the rest is left to the background.</summary>
    /// <remarks>
    /// A status that is rare across a large table can only be counted by visiting every exchange:
    /// with no result row to look up, "processing" can't be found any other way. On millions of
    /// exchanges that ran past the database's own time limit and failed the whole request, taking
    /// every other tab's count with it.
    /// </remarks>
    internal static readonly TimeSpan Budget = TimeSpan.FromSeconds(5);

    /// <summary>
    /// What <paramref name="query"/> returns, or the default - null - when <paramref name="deadline"/>
    /// passes first. Runs inside the summary's transaction, under a savepoint: a statement cancelled
    /// part-way leaves Postgres refusing every later one in the transaction until it is rolled back to
    /// before it.
    /// </summary>
    internal static async Task<T> WithinBudget<T>(BitweenDbContext dbContext, CancellationToken deadline,
        Func<CancellationToken, Task<T>> query)
    {
        const string savepoint = "within_budget";
        var transaction = dbContext.Database.CurrentTransaction!;
        await transaction.CreateSavepointAsync(savepoint);

        try
        {
            return await query(deadline);
        }
        // Whatever the provider throws for a cancelled command: Npgsql an OperationCanceledException,
        // SqlClient a SqlException. Only once the deadline has passed, so a real error still surfaces.
        catch (Exception) when (deadline.IsCancellationRequested)
        {
            await transaction.RollbackToSavepointAsync(savepoint);
            return default;
        }
    }

    private static async Task<int?> CountCapped(IQueryable<XchangeRow> rows, string status, BitweenDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var matching = rows.WhereStatus(status, dbContext);

        // Nearly every exchange succeeds, so the newest 10,001 successes are among about the newest
        // 10,001 exchanges. Unordered, Postgres matched the two tables by id instead, and ids aren't in
        // the order exchanges were written, so nearly every row was read from a different place on
        // disk: 12s on 5.8M exchanges, against under a second this way. The rarer statuses stay
        // unordered, so they keep the plan that reads their own small index.
        if (status == Success)
            matching = matching.OrderByDescending(r => r.StartedOn);

        return await matching.Take(Search.CountCap + 1).CountAsync(cancellationToken);
    }

    /// <summary>
    /// The matching exchanges grouped by when they started, to the precision the bucket size
    /// needs, and by the two columns their status is made of.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Unlike the counts, this cannot stop early: grouping visits every row in the range. What
    /// keeps it affordable is that a histogram is only asked for over a date range, which the index
    /// on StartedOn narrows to, and that what comes back is at most one row per minute per status
    /// rather than one per exchange.
    /// </para>
    /// <para>
    /// Grouped on the date's parts rather than on a computed bucket number, because Year, Month,
    /// Day, Hour and Minute translate on every provider the solution supports, while date
    /// arithmetic is spelt differently by each of them. The folding into buckets wider than the
    /// grouping happens in <see cref="XchangeHistogram.Fold"/>. Status is grouped as its two
    /// nullable columns, not as the four statuses, for the same reason: a CASE over them would be
    /// one more thing to translate four ways.
    /// </para>
    /// </remarks>
    private static async Task<List<XchangeStartedCount>> StartedCounts(IQueryable<XchangeRow> rows,
        int bucketMinutes, CancellationToken cancellationToken)
    {
        if (bucketMinutes >= 1440)
            return await rows
                .GroupBy(r => new { r.StartedOn.Year, r.StartedOn.Month, r.StartedOn.Day, r.Status, r.ResponseBad })
                .Select(g => new XchangeStartedCount
                {
                    Year = g.Key.Year,
                    Month = g.Key.Month,
                    Day = g.Key.Day,
                    Status = g.Key.Status,
                    ResponseBad = g.Key.ResponseBad,
                    Count = g.Count()
                })
                .ToListAsync(cancellationToken);

        if (bucketMinutes >= 60)
            return await rows
                .GroupBy(r => new
                {
                    r.StartedOn.Year, r.StartedOn.Month, r.StartedOn.Day, r.StartedOn.Hour, r.Status,
                    r.ResponseBad
                })
                .Select(g => new XchangeStartedCount
                {
                    Year = g.Key.Year,
                    Month = g.Key.Month,
                    Day = g.Key.Day,
                    Hour = g.Key.Hour,
                    Status = g.Key.Status,
                    ResponseBad = g.Key.ResponseBad,
                    Count = g.Count()
                })
                .ToListAsync(cancellationToken);

        return await rows
            .GroupBy(r => new
            {
                r.StartedOn.Year, r.StartedOn.Month, r.StartedOn.Day, r.StartedOn.Hour, r.StartedOn.Minute,
                r.Status, r.ResponseBad
            })
            .Select(g => new XchangeStartedCount
            {
                Year = g.Key.Year,
                Month = g.Key.Month,
                Day = g.Key.Day,
                Hour = g.Key.Hour,
                Minute = g.Key.Minute,
                Status = g.Key.Status,
                ResponseBad = g.Key.ResponseBad,
                Count = g.Count()
            })
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// The StartedOn range the request's filters give, as the list sends it: a "from" as
    /// <c>StartedOn:6:</c> (greater than or equal) and a "to" as <c>StartedOn:8:</c> (less than or
    /// equal). Either may be missing. Two of the same kind narrow it to their overlap, as the
    /// filters themselves do.
    /// </summary>
    internal static (DateTime? Start, DateTime? End) StartedOnRange(SearchyRequest searchyRequest)
    {
        DateTime? start = null, end = null;
        foreach (var filter in searchyRequest.Conditions.SelectMany(c => c.Filters)
                     .Where(f => f.Field == nameof(XchangeRow.StartedOn)))
        {
            // DatesToUtc sets this only for a value that reads as a date; anything else was never a
            // range Searchy could have applied either.
            if (filter.ValueDateTime is not { } dateTime)
                continue;

            var value = DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
            if (filter.Rule == SearchyRule.GreaterThanOrEquals)
                start = start is { } s && s > value ? s : value;
            else if (filter.Rule == SearchyRule.LessThanOrEquals)
                end = end is { } e && e < value ? e : value;
        }

        return (start, end);
    }
}
