using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.EfCoreExtensions;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Xchanges;

/// <summary>
/// The exchange filters that cannot be handed to Searchy as-is, because they do not correspond to
/// one column: an id that should also match relatives, a status assembled from two columns and the
/// absence of a result row, a promoted-property substring that has to be case-folded.
/// </summary>
/// <remarks>
/// Shared by the search and by bulk retry, which needs "every exchange this filter matches" to mean
/// exactly the set the person was looking at when they chose it. The two drifting apart would be
/// invisible until a bulk retry quietly acted on a different set of exchanges than the one on
/// screen.
/// </remarks>
internal static class XchangeFilters
{
    /// <summary>
    /// The exchanges a filter matches, filtered exactly as the search would have filtered them, as
    /// lean rows: the columns the list can filter on and nothing else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What a "select all matching" bulk action and the summary both start from. Dates are moved to
    /// UTC and a run filter is resolved here, inside the one entry point, so neither caller can
    /// forget a step and select a different set than the list showed for the same filter.
    /// </para>
    /// <para>
    /// The projection carries the columns the exchange list can filter on (see
    /// <c>buildExchangeQuery</c> in the client) and nothing else, so this stays translatable and
    /// composable - the search's own projection cannot be reused for that, as it builds file URLs
    /// in C#. Filtering on a column that is not here would silently match nothing, so a new filter
    /// on the list needs a column here too.
    /// </para>
    /// <para>
    /// The special filters are removed from <paramref name="searchyRequest"/> as they are applied,
    /// as they are for the search; what is left in it afterwards is only what Searchy handled.
    /// </para>
    /// </remarks>
    internal static async Task<IQueryable<XchangeRow>> FilteredRowsAsync(BitweenDbContext dbContext,
        SearchyRequest searchyRequest)
    {
        searchyRequest.DatesToUtc();
        // Async, and inside here rather than in the callers, so a "select all matching" over a run
        // selects exactly the rows the list showed for it.
        await ResolveReceiveAttemptFilterAsync(searchyRequest, dbContext);

        var query = from xchange in dbContext.Set<Xchange>()
                    join result in dbContext.Set<XchangeResult>() on xchange.Id equals result.Id into xr
                    from result in xr.DefaultIfEmpty()
                    join agg in dbContext.Set<XchangeAggregation>() on xchange.Id equals agg.Id into xa
                    from agg in xa.DefaultIfEmpty()
                    join promoted in dbContext.Set<XchangePromotedProperties>() on xchange.Id equals promoted.Id into xp
                    from promoted in xp.DefaultIfEmpty()
                    join subscriber in dbContext.Set<Subscription>() on xchange.SubscriptionId equals subscriber.Id into xs
                    from subscriber in xs.DefaultIfEmpty()
                    select new XchangeRow
                    {
                        Id = xchange.Id,
                        SubscriptionId = xchange.SubscriptionId,
                        DocumentId = xchange.DocumentId,
                        StartedOn = xchange.StartedOn,
                        CorrelationId = xchange.CorrelationId,
                        Status = result.Success,
                        ResponseBad = result.ResponseBad,
                        // For the ErrorContains filter. Only read when that filter is set, so it
                        // costs nothing to the selections that do not use it.
                        Exception = result.Exception,
                        RetryFor = xchange.RetryFor,
                        AggregationXchangeId = agg.AggregationXchangeId,
                        PromotedPropertiesRaw = promoted.PropertiesRaw,
                        // Same fallback the search makes for exchanges written before the column
                        // existed, so a partner filter selects the same rows it listed.
                        PartnerId = xchange.PartnerId ?? subscriber.PartnerId
                    };

        query = query.ApplySpecialFilters(searchyRequest, dbContext);
        return query.AsNoTracking().Search(searchyRequest.Conditions);
    }

    /// <summary>
    /// Turns a <c>ReceiveAttemptId</c> filter into the <c>Id</c> filter the rest of the pipeline
    /// already understands, by reading the run's own record of the exchanges it created.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="ApplySpecialFilters"/>, and async, because the run has to be read
    /// before the query can be built. <c>ReceiveAttempt.ExchangeIds</c> is persisted as a single
    /// separator-delimited string rather than an array (see <c>BitweenDbContext</c>), so no
    /// subquery can reach inside it - and with three database providers in the solution there is
    /// no one translation that would.
    ///
    /// It rewrites rather than filters here so the result stays identical to the long
    /// <c>?ids=a,b,c</c> URL this replaces, relatives and all - the only thing that changes is that
    /// the ids no longer have to fit in a web address.
    /// </remarks>
    internal static async Task ResolveReceiveAttemptFilterAsync(SearchyRequest searchyRequest,
        BitweenDbContext dbContext)
    {
        var condition = searchyRequest.Conditions.FirstOrDefault();
        if (condition == null)
            return;

        var attemptFilters = condition.Filters.Where(f => f.Field == "ReceiveAttemptId").ToList();
        foreach (var attemptFilter in attemptFilters)
        {
            if (!int.TryParse(attemptFilter.Value?.ToString(), out var attemptId))
                throw new SWValidationException("NOT_SUPPORTED",
                    $"'{attemptFilter.Value}' is not a receive attempt id.");

            var exchangeIds = await dbContext.Set<ReceiveAttempt>().AsNoTracking()
                .Where(a => a.Id == attemptId)
                .Select(a => a.ExchangeIds)
                .SingleOrDefaultAsync();

            if (exchangeIds == null)
                throw new SWValidationException("NOT_FOUND",
                    $"Run {attemptId} no longer exists. Its history may have been cleaned up.");

            // A run that created nothing leaves an empty array, and the Contains branch below reads
            // that as "matches no row" - which is the honest answer. Dropping the filter instead
            // would widen the selection to every exchange, and bulk retry acts on whatever this
            // selects.
            condition.Filters.Add(new SearchyFilter
            {
                Field = "Id",
                Rule = SearchyRule.Contains,
                ValueStringArray = exchangeIds,
            });
            condition.Filters.Remove(attemptFilter);
        }
    }

    /// <summary>
    /// Applies the special filters and removes them from <paramref name="searchyRequest"/>, leaving
    /// the plain per-column ones for Searchy to handle.
    /// </summary>
    internal static IQueryable<XchangeRow> ApplySpecialFilters(this IQueryable<XchangeRow> query,
        SearchyRequest searchyRequest, BitweenDbContext dbContext)
    {
        var condition = searchyRequest.Conditions.FirstOrDefault();
        if (condition == null)
            return query;

        var idFilters = condition.Filters.Where(f => f.Field == "Id").ToList();
        foreach (var idFilter in idFilters)
        {
            var value = idFilter.Value.ToString();
            switch (idFilter.Rule)
            {
                case SearchyRule.EqualsTo:
                    query = query.Where(i =>
                        i.Id == value || i.RetryFor == value || i.AggregationXchangeId == value);
                    break;
                case SearchyRule.Contains:
                    {
                        var valueAsArray = idFilter.ValueStringArray;
                        query = query.Where(i =>
                            valueAsArray.Any(v => i.RetryFor == v) ||
                            valueAsArray.Any(v => i.AggregationXchangeId == v) ||
                            valueAsArray.Any(v => i.Id == v)
                        );
                        break;
                    }


                default:
                    throw new SWValidationException("NOT_SUPPORTED", "Search query not supported");
            }

            condition.Filters.Remove(idFilter);
        }

        var statusFilters = condition.Filters.Where(f => f.Field == "StatusFilter").ToList();
        foreach (var statusFilter in statusFilters)
        {
            query = query.WhereStatus(statusFilter.Value, dbContext);
            condition.Filters.Remove(statusFilter);
        }

        // "Only the newest attempt of each chain." An exchange that has been retried is superseded
        // by that retry, so a list of failures otherwise counts one piece of work as many rows -
        // a chain retried nine times fills nine of them, none of which is the current state.
        //
        // NOT EXISTS rather than a left join for the same reason the still-running status uses it:
        // Postgres estimates an anti-join, and RetryFor is indexed.
        var latestFilters = condition.Filters.Where(f => f.Field == "LatestOnly").ToList();
        foreach (var latestFilter in latestFilters)
        {
            if (IsOn(latestFilter))
                query = query.Where(i => !dbContext.Set<Xchange>().Any(r => r.RetryFor == i.Id));

            condition.Filters.Remove(latestFilter);
        }

        // "Only retries": attempts made from an earlier exchange, whether a person asked for them or
        // a retry policy did. RetryFor is on the exchange itself, so no second table is involved.
        var retryFilters = condition.Filters.Where(f => f.Field == "IsRetry").ToList();
        foreach (var retryFilter in retryFilters)
        {
            if (IsOn(retryFilter))
                query = query.Where(i => i.RetryFor != null);

            condition.Filters.Remove(retryFilter);
        }

        // "Waiting for a scheduled retry": a DelayedRetry row exists for it. That row is keyed by the
        // exchange's own id and deleted when the retry runs, so its presence is the whole question.
        // EXISTS rather than reading the search's left join to DelayedRetry, for the same reason
        // the still-running status uses NOT EXISTS - and because the lean projection bulk actions
        // and the summary filter over does not join DelayedRetry at all.
        var scheduledFilters = condition.Filters.Where(f => f.Field == "HasScheduledRetry").ToList();
        foreach (var scheduledFilter in scheduledFilters)
        {
            if (IsOn(scheduledFilter))
                query = query.Where(i => dbContext.Set<DelayedRetry>().Any(d => d.Id == i.Id));

            condition.Filters.Remove(scheduledFilter);
        }

        // "Rolled up into an aggregation": the exchange was taken into an aggregation exchange,
        // which is what carries its data onwards from there.
        var aggregatedFilters = condition.Filters.Where(f => f.Field == "Aggregated").ToList();
        foreach (var aggregatedFilter in aggregatedFilters)
        {
            if (IsOn(aggregatedFilter))
                query = query.Where(i => i.AggregationXchangeId != null);

            condition.Filters.Remove(aggregatedFilter);
        }

        // Text inside the error, case-insensitive. Named apart from the Exception column rather than
        // reusing it, because a plain Searchy filter on Exception would compare case as stored, and
        // an error message's case is whatever the adapter that threw it chose. Folded on both sides
        // like the promoted properties below; a leading-wildcard LIKE could use no index either way.
        // An empty value is no filter at all rather than "has any error", which is what Contains("")
        // would quietly turn into.
        var errorFilters = condition.Filters.Where(f => f.Field == "ErrorContains").ToList();
        foreach (var errorFilter in errorFilters)
        {
            var value = errorFilter.Value?.ToString();
            if (!string.IsNullOrEmpty(value))
            {
                value = value.ToLower();
                query = query.Where(i => i.Exception.ToLower().Contains(value));
            }

            condition.Filters.Remove(errorFilter);
        }

        var propertiesFilters = condition.Filters
            .Where(f => f.Field == "PromotedPropertiesRaw").ToList();
        foreach (var propertyFilter in propertiesFilters)
        {
            var value = propertyFilter.Value.ToString()!.ToLower();

            // Both sides lower-cased at query time. Promoted values keep the case the
            // payload had (see FilterService), so the column has to be folded here for
            // the search to stay case-insensitive. No index is lost: a Contains is a
            // leading-wildcard LIKE, which the b-tree on this column could never serve.
            query = query.Where(i => i.PromotedPropertiesRaw.ToLower().Contains(value));
            condition.Filters.Remove(propertyFilter);
        }

        return query;
    }

    /// <summary>
    /// Whether an on/off filter is on. Only "true" or "1" turn one on; anything else leaves it off
    /// rather than refusing it, which is what <c>LatestOnly</c> has always done - "select all
    /// matching" with no filters set sends <c>LatestOnly:1:false</c> to mean "everything".
    /// </summary>
    private static bool IsOn(SearchyFilter filter) =>
        filter.Value?.ToString()?.ToLower() is "true" or "1";

    /// <summary>
    /// Narrows <paramref name="query"/> to one exchange status, by the number the client's
    /// <c>StatusFilter</c> sends: 0 still running, 1 success, 2 success with a bad response, 3 failed.
    /// </summary>
    /// <remarks>
    /// Its own method so the summary can count each status under the same definition the list
    /// filters by. A count that defined "success" even slightly differently from the tab it sits on
    /// would show one number and list another.
    /// </remarks>
    internal static IQueryable<XchangeRow> WhereStatus(this IQueryable<XchangeRow> query, object status,
        BitweenDbContext dbContext)
    {
        switch (status)
        {
            case "0":
                // "Still running" means no result row exists yet. Asking for it as
                // Status == null reads as `x0.success IS NULL` on the left join, and
                // Postgres cannot estimate that: it guesses one row, plans every join
                // above it for one row, and picks per-row sequential scans of the small
                // side tables. Measured on 1M exchanges that was 22.8s for 25 rows.
                // NOT EXISTS asks the same question as an anti-join, which it can
                // estimate - 34ms. Equivalent because success is NOT NULL, so a result
                // row can never itself carry a null status.
                return query.Where(i =>
                    !dbContext.Set<XchangeResult>().Any(r => r.Id == i.Id));
            case "1":
                return query.Where(i => i.Status == true && i.ResponseBad != true);

            case "2":
                return query.Where(i => i.Status == true && i.ResponseBad == true);

            case "3":
                return query.Where(i => i.Status == false);

            default:
                // The filter is removed by the caller whether or not it matched, so falling through
                // here used to drop it silently and widen the selection to everything. A search
                // returning too much is merely wrong; bulk retry runs over whatever this selects,
                // so an unreadable status has to be refused rather than ignored.
                throw new SWValidationException("NOT_SUPPORTED",
                    $"'{status}' is not an exchange status.");
        }
    }
}
