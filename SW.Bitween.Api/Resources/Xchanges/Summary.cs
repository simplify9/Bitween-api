using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
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
/// </remarks>
[HandlerName("summary")]
public class Summary(BitweenDbContext dbContext, RequestContext requestContext) : ISearchyHandler
{
    /// <summary>The status numbers <see cref="XchangeFilters.WhereStatus"/> reads, in the order they are counted.</summary>
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

        var rows = await XchangeFilters.FilteredRowsAsync(dbContext, searchyRequest);

        // Read after FilteredRowsAsync, which is what moves the request's dates to UTC. The
        // StartedOn filters are plain columns, so Searchy has applied them and left them in place.
        var (rangeStart, rangeEnd) = StartedOnRange(searchyRequest);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadUncommitted);

        // One capped count per status rather than a single grouped count: each can stop at the
        // cap, as the search's total does, where a grouping has to visit every matching row. Four
        // short queries over a broad filter cost far less than one exhaustive one.
        var counts = new XchangeStatusCounts
        {
            Processing = await CountCapped(rows, Processing),
            Success = await CountCapped(rows, Success),
            BadResponse = await CountCapped(rows, BadResponse),
            Failed = await CountCapped(rows, Failed),
        };

        var summary = new XchangeSummary { Counts = counts };
        if (rangeStart is not { } start)
            return summary;

        var end = rangeEnd ?? DateTime.UtcNow;
        if (end < start)
            return summary;

        var bucketMinutes = XchangeHistogram.ChooseBucketMinutes(start, end);
        summary.BucketMinutes = bucketMinutes;
        summary.Buckets = XchangeHistogram.Fold(start, end, bucketMinutes,
            await StartedCounts(rows, bucketMinutes));
        return summary;
    }

    private Task<int> CountCapped(IQueryable<XchangeRow> rows, string status) =>
        rows.WhereStatus(status, dbContext).Take(Search.CountCap + 1).CountAsync();

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
        int bucketMinutes)
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
                .ToListAsync();

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
                .ToListAsync();

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
            .ToListAsync();
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
