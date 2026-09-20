using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
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
    /// Turns a <c>ReceiveAttemptId</c> filter into the <c>Id</c> filter the rest of the pipeline
    /// already understands, by reading the run's own record of the exchanges it created.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="ApplySpecialFilters"/>, and async, because the run has to be read
    /// before the query can be built. <c>ReceiveAttempt.ExchangeIds</c> is persisted as a single
    /// separator-delimited string rather than an array (see <c>BitweenDbContext</c>), so no
    /// subquery can reach inside it — and with three database providers in the solution there is
    /// no one translation that would.
    ///
    /// It rewrites rather than filters here so the result stays identical to the long
    /// <c>?ids=a,b,c</c> URL this replaces, relatives and all — the only thing that changes is that
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
            // that as "matches no row" — which is the honest answer. Dropping the filter instead
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
            switch (statusFilter.Value)
            {
                case "0":
                    // "Still running" means no result row exists yet. Asking for it as
                    // Status == null reads as `x0.success IS NULL` on the left join, and
                    // Postgres cannot estimate that: it guesses one row, plans every join
                    // above it for one row, and picks per-row sequential scans of the small
                    // side tables. Measured on 1M exchanges that was 22.8s for 25 rows.
                    // NOT EXISTS asks the same question as an anti-join, which it can
                    // estimate — 34ms. Equivalent because success is NOT NULL, so a result
                    // row can never itself carry a null status.
                    query = query.Where(i =>
                        !dbContext.Set<XchangeResult>().Any(r => r.Id == i.Id));
                    break;
                case "1":
                    query = query.Where(i => i.Status == true && i.ResponseBad != true);
                    break;

                case "2":
                    query = query.Where(i => i.Status == true && i.ResponseBad == true);
                    break;

                case "3":
                    query = query.Where(i => i.Status == false);
                    break;

                default:
                    // The filter is removed below whether or not it matched, so falling through
                    // here used to drop it silently and widen the selection to everything. A
                    // search returning too much is merely wrong; bulk retry runs over whatever
                    // this selects, so an unreadable status has to be refused rather than ignored.
                    throw new SWValidationException("NOT_SUPPORTED",
                        $"'{statusFilter.Value}' is not an exchange status.");
            }

            condition.Filters.Remove(statusFilter);
        }

        // "Only the newest attempt of each chain." An exchange that has been retried is superseded
        // by that retry, so a list of failures otherwise counts one piece of work as many rows —
        // a chain retried nine times fills nine of them, none of which is the current state.
        //
        // NOT EXISTS rather than a left join for the same reason the still-running status uses it:
        // Postgres estimates an anti-join, and RetryFor is indexed.
        var latestFilters = condition.Filters.Where(f => f.Field == "LatestOnly").ToList();
        foreach (var latestFilter in latestFilters)
        {
            if (latestFilter.Value?.ToString()?.ToLower() is "true" or "1")
                query = query.Where(i => !dbContext.Set<Xchange>().Any(r => r.RetryFor == i.Id));

            condition.Filters.Remove(latestFilter);
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
}
