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
/// Works out what a bulk retry would do: which exchanges the selection comes to, which of them
/// have already been retried and so hand over to a later attempt, and which cannot be retried at
/// all.
/// </summary>
/// <remarks>
/// Shared by the preview and the retry itself so that what someone confirms is what runs. Built by
/// hand rather than injected, like <c>RetryGroupBudget</c> — it is a piece of one request's work,
/// not a service.
/// </remarks>
internal sealed class BulkRetryPlanner
{
    /// <summary>
    /// The largest selection one request will carry out. Every exchange retried means reading its
    /// input back out of storage and writing a new exchange, in sequence, inside the one request —
    /// so the ceiling is about what can finish before something upstream gives up waiting, not
    /// about the database. Past it the caller is asked to narrow the filter.
    /// </summary>
    internal const int Limit = 500;

    /// <summary>
    /// Guards the walk to the newest attempt against a cycle in the data, the same way
    /// <see cref="RetryTree"/> does. No honest chain approaches it.
    /// </summary>
    private const int MaxDepth = 100;

    private readonly BitweenDbContext _dbContext;

    internal BulkRetryPlanner(BitweenDbContext dbContext) => _dbContext = dbContext;

    /// <summary>The plan to show or carry out, and the exchanges it would actually retry.</summary>
    internal sealed class Prepared
    {
        internal XchangeBulkRetryPlan Plan { get; init; }
        internal List<string> Targets { get; init; } = new List<string>();
    }

    internal async Task<Prepared> Prepare(XchangeBulkRetry request)
    {
        var selected = await ResolveSelection(request);

        if (selected.Count > Limit)
            return new Prepared
            {
                Plan = new XchangeBulkRetryPlan
                {
                    // A hand-picked list is already counted. A filter's selection was cut off at
                    // Limit + 1 to notice it was too long without reading it all, so that one is
                    // counted properly now — the caller is told how far past the line it is rather
                    // than just "501", and never a count of rows it did not ask about.
                    Selected = string.IsNullOrWhiteSpace(request.Filter)
                        ? selected.Count
                        : await CountSelection(request),
                    Limit = Limit,
                    OverLimit = true
                }
            };

        var newestAttempt = await FindNewestAttempts(selected);
        var state = await ReadState(newestAttempt.Values.Distinct().ToList());
        var reset = request.Reset;

        var plan = new XchangeBulkRetryPlan { Selected = selected.Count, Limit = Limit };
        var targets = new List<string>();

        foreach (var selectedId in selected)
        {
            var targetId = newestAttempt[selectedId];

            if (targetId != selectedId)
                plan.Substituted.Add(new XchangeRetrySubstitution
                {
                    SelectedId = selectedId,
                    RetryId = targetId
                });

            var reason = WhyNot(state.GetValueOrDefault(targetId), targetId != selectedId, reset);
            if (reason != null)
            {
                plan.Skipped.Add(new XchangeRetrySkip { Id = targetId, Reason = reason });
                continue;
            }

            // Two selections in the same chain — an exchange and its own retry, say — come to the
            // same attempt, which is retried once.
            if (!targets.Contains(targetId))
                targets.Add(targetId);
        }

        plan.WillRetry = targets.Count;
        plan.Properties = await ReadPromotedProperties(plan);
        return new Prepared { Plan = plan, Targets = targets };
    }

    /// <summary>
    /// Why this attempt will be left alone, or <c>null</c> when it will be retried. The wording
    /// says whose fault it is: the exchange the caller picked, or the later attempt standing in
    /// for it.
    /// </summary>
    private static string WhyNot(SelectionState state, bool substituted, bool reset)
    {
        var subject = substituted ? "Its newest attempt" : "It";

        if (state == null)
            return "This exchange no longer exists.";

        // Only when the caller asked for the subscription's current configuration, which there is
        // no way to read once the subscription is gone. Checked here as well as at the moment of
        // retrying, because a plan that promises to retry something the retry then skips makes the
        // confirmation worthless — the whole point of showing it is that it is what will happen.
        if (reset && state.SubscriptionMissing)
            return $"{subject} cannot have its properties re-resolved: the subscription no longer exists.";

        if (state.ScheduledRetryOn != null)
            return $"{subject} already has an auto-retry scheduled. Run that now instead.";

        // An exchange with no result is deliberately not skipped as "still running". That is what
        // a broker outage leaves behind — hundreds of exchanges that were never processed and never
        // will be — and retrying those in bulk is the main thing a wide selection is for. Refusing
        // them would take the recovery path away to protect against double-running work that, in
        // the case anyone actually selects in bulk, is not running at all.

        if (state.Status == true && state.ResponseBad != true)
            return $"{subject} succeeded, so there is nothing to retry.";

        return null;
    }

    /// <summary>
    /// The end of each selection's chain: for an exchange already retried, the attempt a retry
    /// would actually run, since the selected one is refused a second retry.
    /// </summary>
    /// <remarks>
    /// One query per round for the whole selection at once, rather than a walk per exchange —
    /// five hundred selections down a chain of four is four queries, not two thousand.
    /// </remarks>
    private async Task<Dictionary<string, string>> FindNewestAttempts(List<string> selected)
    {
        var newest = selected.ToDictionary(id => id, id => id);

        for (var depth = 0; depth < MaxDepth; depth++)
        {
            // Where each selection has got to so far. Asking about that frontier — rather than
            // about one level of the tree, tracking which nodes the walk had already seen — is what
            // keeps selections that sit in the same chain consistent with each other. With a seen
            // set, a selection lagging behind another stopped dead on a node the leader had already
            // stepped over, and was reported as "already retried, its newest attempt runs instead"
            // naming an attempt that had itself been retried. Retrying that then threw
            // ALREADY_RETRIED and took the whole bulk retry with it.
            var frontier = newest.Values.Distinct().ToList();

            var children = await _dbContext.Set<Xchange>().AsNoTracking()
                .Where(x => frontier.Contains(x.RetryFor))
                .Select(x => new { x.Id, x.RetryFor, x.StartedOn })
                .ToListAsync();

            if (children.Count == 0) break;

            // One retry per exchange is the rule, but exchanges retried before it was enforced can
            // have more than one. Follow the newest, and let the retry tree in the drawer be where
            // the fork is shown — a bulk retry has to choose something, and the newest attempt is
            // the one that reflects where the work actually got to.
            var step = children
                .GroupBy(c => c.RetryFor)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.StartedOn).First().Id);

            var advanced = false;
            foreach (var key in newest.Keys.ToList())
                if (step.TryGetValue(newest[key], out var child) && child != newest[key])
                {
                    newest[key] = child;
                    advanced = true;
                }

            // Nothing moved, so every selection is at the end of its chain. This is also the way
            // out of a cycle in the data, which would otherwise keep advancing until MaxDepth.
            if (!advanced) break;
        }

        return newest;
    }

    /// <summary>
    /// What the payload promoted, for each exchange the plan names on either side of a
    /// substitution or in a skip — so the caller can describe them the way the exchange list does
    /// rather than by id alone.
    /// </summary>
    private async Task<Dictionary<string, IDictionary<string, string>>> ReadPromotedProperties(
        XchangeBulkRetryPlan plan)
    {
        var named = plan.Substituted.Select(s => s.SelectedId)
            .Concat(plan.Substituted.Select(s => s.RetryId))
            .Concat(plan.Skipped.Select(s => s.Id))
            .Distinct()
            .ToList();

        if (named.Count == 0)
            return new Dictionary<string, IDictionary<string, string>>();

        var rows = await _dbContext.Set<XchangePromotedProperties>().AsNoTracking()
            .Where(p => named.Contains(p.Id))
            .ToListAsync();

        return rows.ToDictionary(r => r.Id, r => (IDictionary<string, string>)r.Properties.ToDictionary());
    }

    private sealed class SelectionState
    {
        public string Id { get; set; }
        public bool? Status { get; set; }
        public bool? ResponseBad { get; set; }
        public System.DateTime? ScheduledRetryOn { get; set; }

        /// <summary>
        /// Covers an exchange that never had a subscription — a document-only one — as well as one
        /// whose subscription has since been deleted. Both leave a reset with nothing to read.
        /// </summary>
        public bool SubscriptionMissing { get; set; }
    }

    private async Task<Dictionary<string, SelectionState>> ReadState(List<string> ids)
    {
        var rows = await (
            from xchange in _dbContext.Set<Xchange>()
            join result in _dbContext.Set<XchangeResult>() on xchange.Id equals result.Id into xr
            from result in xr.DefaultIfEmpty()
            join delayed in _dbContext.Set<DelayedRetry>() on xchange.Id equals delayed.Id into dr
            from delayed in dr.DefaultIfEmpty()
            where ids.Contains(xchange.Id)
            select new SelectionState
            {
                Id = xchange.Id,
                Status = result.Success,
                ResponseBad = result.ResponseBad,
                ScheduledRetryOn = delayed != null ? delayed.On : (System.DateTime?)null,
                SubscriptionMissing = xchange.SubscriptionId == null ||
                    !_dbContext.Set<Subscription>().Any(sub => sub.Id == xchange.SubscriptionId)
            }).AsNoTracking().ToListAsync();

        return rows.ToDictionary(r => r.Id);
    }

    private async Task<List<string>> ResolveSelection(XchangeBulkRetry request)
    {
        if (string.IsNullOrWhiteSpace(request.Filter))
            return (request.Ids ?? new List<string>()).Where(id => id != null).Distinct().ToList();

        var exclude = request.ExcludeIds ?? new List<string>();

        // One past the limit is all it takes to know the selection is too big, and stops a
        // "select all" over a wide filter from reading a million ids to refuse them.
        return await (await SelectionQuery(request))
            .Where(r => !exclude.Contains(r.Id))
            .Select(r => r.Id)
            .Take(Limit + 1)
            .ToListAsync();
    }

    private async Task<int> CountSelection(XchangeBulkRetry request)
    {
        var exclude = request.ExcludeIds ?? new List<string>();
        return await (await SelectionQuery(request))
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
    private async Task<IQueryable<XchangeRow>> SelectionQuery(XchangeBulkRetry request)
    {
        var searchyRequest = new SearchyRequest(request.Filter);
        searchyRequest.DatesToUtc();
        // Async, and inside here rather than in the two callers, so a "select all matching" over a
        // run selects exactly the rows the list showed for it.
        await XchangeFilters.ResolveReceiveAttemptFilterAsync(searchyRequest, _dbContext);

        var query = from xchange in _dbContext.Set<Xchange>()
                    join result in _dbContext.Set<XchangeResult>() on xchange.Id equals result.Id into xr
                    from result in xr.DefaultIfEmpty()
                    join agg in _dbContext.Set<XchangeAggregation>() on xchange.Id equals agg.Id into xa
                    from agg in xa.DefaultIfEmpty()
                    join promoted in _dbContext.Set<XchangePromotedProperties>() on xchange.Id equals promoted.Id into xp
                    from promoted in xp.DefaultIfEmpty()
                    join subscriber in _dbContext.Set<Subscription>() on xchange.SubscriptionId equals subscriber.Id into xs
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

        query = query.ApplySpecialFilters(searchyRequest, _dbContext);
        return query.AsNoTracking().Search(searchyRequest.Conditions);
    }
}
