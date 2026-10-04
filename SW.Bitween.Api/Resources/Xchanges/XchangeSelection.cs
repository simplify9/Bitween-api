using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.EfCoreExtensions;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Xchanges;

/// <summary>
/// The exchanges a bulk action on the exchange list covers — a bulk retry, a files export: the rows
/// picked by hand, or everything a "select all matching" filter matches minus the rows unticked after.
/// </summary>
/// <remarks>
/// Built by hand rather than injected, like <see cref="BulkRetryPlanner"/>: a piece of one request's
/// work, not a service.
/// </remarks>
internal sealed class XchangeSelection(BitweenDbContext dbContext)
{
    /// <summary>
    /// The selected ids. For a filter, at most <paramref name="limit"/> + 1 of them: one past the limit
    /// is all it takes to know the selection is too big, and stops a "select all" over a wide filter
    /// from reading a million ids to refuse them.
    /// </summary>
    internal async Task<List<string>> Resolve(IXchangeSelection request, int limit)
    {
        if (string.IsNullOrWhiteSpace(request.Filter))
            return (request.Ids ?? new List<string>()).Where(id => id != null).Distinct().ToList();

        var exclude = request.ExcludeIds ?? new List<string>();

        return await (await Query(request))
            .Where(r => !exclude.Contains(r.Id))
            .Select(r => r.Id)
            .Take(limit + 1)
            .ToListAsync();
    }

    /// <summary>How many exchanges a filter's selection really holds, for saying how far past a limit it is.</summary>
    internal async Task<int> Count(IXchangeSelection request)
    {
        var exclude = request.ExcludeIds ?? new List<string>();
        return await (await Query(request))
            .Where(r => !exclude.Contains(r.Id))
            // Same reason the search caps its own count: counting every match has to visit every
            // matching row. The number is only being used to say "too many", so stopping early
            // costs the caller nothing.
            .Take(Search.CountCap + 1)
            .CountAsync();
    }

    /// <summary>
    /// The exchanges a "select all matching" came from, filtered exactly as the search would have
    /// filtered them.
    /// </summary>
    /// <remarks>
    /// The projection carries the columns the exchange list can filter on (see
    /// <c>buildExchangeQuery</c> in the client) and nothing else, so this stays translatable and
    /// composable — the search's own projection cannot be reused for that, as it builds file URLs
    /// in C#. Filtering on a column that is not here would silently match nothing, so a new filter
    /// on the list needs a column here too.
    /// </remarks>
    private async Task<IQueryable<XchangeRow>> Query(IXchangeSelection request)
    {
        var searchyRequest = new SearchyRequest(request.Filter);
        searchyRequest.DatesToUtc();
        // Async, and inside here rather than in the two callers, so a "select all matching" over a
        // run selects exactly the rows the list showed for it.
        await XchangeFilters.ResolveReceiveAttemptFilterAsync(searchyRequest, dbContext);

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
}
