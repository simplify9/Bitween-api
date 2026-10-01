using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Xchanges;

/// <summary>
/// Every attempt in one exchange's retry chain: back to the original, and forward through each
/// retry made from it.
/// </summary>
/// <remarks>
/// <para>
/// Its own endpoint rather than part of the exchange search, because a chain cannot be had from the
/// same query that lists exchanges — it takes a walk of unknown length, and the search is already
/// the expensive one. Callers avoid asking altogether for the common case: <see
/// cref="XchangeRow.RetryFor"/> and <see cref="XchangeRow.HasRetry"/> come back with every row, and
/// an exchange with neither has no chain to fetch.
/// </para>
/// <para>
/// Walked iteratively, a level at a time, rather than as a recursive query — the three supported
/// databases would each need their own SQL for that, to save queries on a chain that a retry budget
/// keeps to single digits in practice. Both walks are indexed lookups on <c>RetryFor</c>.
/// </para>
/// </remarks>
[HandlerName("retrytree")]
public class RetryTree : IQueryHandler<XchangeRetryTreeRequest, object>
{
    /// <summary>
    /// How far the walk goes in each direction before giving up and saying so. Far past any real
    /// chain — a retry policy's budget is the practical limit — so this exists to keep a cycle in
    /// the data from becoming an endless loop, not to shorten honest answers.
    /// </summary>
    private const int MaxDepth = 100;

    /// <summary>
    /// How many attempts one answer carries. Depth alone does not bound the work: an exchange
    /// retried many times over before the one-retry rule existed has all of those as direct
    /// children, and every one would be loaded and serialized. Far above any real chain, so
    /// reaching it means the data is unusual — and the answer then says it is partial rather than
    /// quietly costing the database more the older the exchange is.
    /// </summary>
    private const int MaxNodes = 500;

    private readonly BitweenDbContext _dbContext;
    private readonly RequestContext _requestContext;

    public RetryTree(BitweenDbContext dbContext, RequestContext requestContext)
    {
        _dbContext = dbContext;
        _requestContext = requestContext;
    }

    public async Task<object> Handle(XchangeRetryTreeRequest request)
    {
        await _requestContext.EnsurePermission(_dbContext, Model.Permissions.Exchanges.View);

        if (string.IsNullOrWhiteSpace(request?.Id))
            throw new SWValidationException("ID_REQUIRED", "Which exchange's retries?");

        var truncated = false;

        // Up to the original. Each step reads one pointer, so the exchange asked about does not
        // have to be the newest attempt — opening any of them shows the same chain.
        var rootId = request.Id;
        var ancestors = new HashSet<string> { rootId };
        for (var depth = 0; depth < MaxDepth; depth++)
        {
            var parent = await _dbContext.Set<Xchange>().AsNoTracking()
                .Where(x => x.Id == rootId)
                .Select(x => x.RetryFor)
                .FirstOrDefaultAsync();

            // Also covers the exchange not existing at all: no row, so no parent, and the walk
            // down then finds nothing either — an empty tree rather than a 404, because a caller
            // asking about an exchange it is looking at wants the chain, not another error to
            // handle.
            if (parent == null || !ancestors.Add(parent))
                break;

            rootId = parent;
            if (depth == MaxDepth - 1) truncated = true;
        }

        // Then down from the original, taking a whole level per query. From the root rather than
        // from the exchange asked about, so an exchange that forked before one-retry-per-exchange
        // was enforced shows both of its branches instead of only the one that leads here.
        var ids = new List<string> { rootId };
        // Its own visited set: the walk down passes back through the ancestors the walk up just
        // collected, so sharing one would drop the whole stretch between the original and the
        // exchange asked about.
        var visited = new HashSet<string> { rootId };
        var level = new List<string> { rootId };
        for (var depth = 0; depth < MaxDepth && level.Count > 0; depth++)
        {
            var children = await _dbContext.Set<Xchange>().AsNoTracking()
                .Where(x => level.Contains(x.RetryFor))
                .Select(x => x.Id)
                .ToListAsync();

            level = children.Where(visited.Add).ToList();

            if (ids.Count + level.Count > MaxNodes)
            {
                ids.AddRange(level.Take(MaxNodes - ids.Count));
                truncated = true;
                break;
            }

            ids.AddRange(level);

            if (depth == MaxDepth - 1 && level.Count > 0) truncated = true;
        }

        var nodes = await (
            from xchange in _dbContext.Set<Xchange>()
            join result in _dbContext.Set<XchangeResult>() on xchange.Id equals result.Id into xr
            from result in xr.DefaultIfEmpty()
            join delayed in _dbContext.Set<DelayedRetry>() on xchange.Id equals delayed.Id into dr
            from delayed in dr.DefaultIfEmpty()
            join promoted in _dbContext.Set<XchangePromotedProperties>() on xchange.Id equals promoted.Id into xp
            from promoted in xp.DefaultIfEmpty()
            join document in _dbContext.Set<Document>() on xchange.DocumentId equals document.Id
            where ids.Contains(xchange.Id)
            orderby xchange.StartedOn
            select new XchangeRetryNode
            {
                Id = xchange.Id,
                RetryFor = xchange.RetryFor,
                StartedOn = xchange.StartedOn,
                FinishedOn = result.FinishedOn,
                Status = result.Success,
                ResponseBad = result.ResponseBad,
                Exception = result.Exception,
                ManualRetry = xchange.ManualRetry,
                ScheduledRetryOn = delayed != null ? delayed.On : (System.DateTime?)null,
                RetryBlockedReason = result.RetryBlockedReason,
                PromotedProperties = promoted == null ? null : promoted.Properties.InDefinedOrder(document.PromotedProperties)
            }).AsNoTracking().ToListAsync();

        return new XchangeRetryTree
        {
            RootId = rootId,
            Nodes = nodes,
            Truncated = truncated
        };
    }
}
