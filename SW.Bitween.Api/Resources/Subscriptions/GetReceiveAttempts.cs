using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Subscriptions;

/// <summary>
/// Paged, filterable history of one Receiving subscription's own <see cref="ReceiveAttempt"/>
/// rows — independent of <see cref="GetRuns"/>, which reads the scheduler's own (always-succeeds)
/// history instead.
/// </summary>
[HandlerName("receiveattempts")]
public class GetReceiveAttempts : IQueryHandler<SearchReceiveAttemptsModel, object>
{
    private readonly BitweenDbContext _dbContext;
    private readonly RequestContext _requestContext;

    public GetReceiveAttempts(BitweenDbContext dbContext, RequestContext requestContext)
    {
        _dbContext = dbContext;
        _requestContext = requestContext;
    }

    public async Task<object> Handle(SearchReceiveAttemptsModel request)
    {
        await _requestContext.EnsurePermission(_dbContext, Model.Permissions.Subscriptions.View);

        var offset = request.Offset ?? 0;
        var limit = request.Limit ?? 25;

        var query = _dbContext.Set<ReceiveAttempt>()
            .AsNoTracking()
            .Where(a => a.SubscriptionId == request.SubscriptionId);

        if (request.Outcome.HasValue)
            query = query.Where(a => a.Outcome == request.Outcome.Value);

        var totalCount = await query.CountAsync();

        var page = await query
            .OrderByDescending(a => a.StartedOn)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();

        var exchangeIds = page.SelectMany(a => a.ExchangeIds ?? Array.Empty<string>()).Distinct().ToList();

        // Left join: an id an attempt still points at but whose Xchange got cleaned up some
        // other way shows up with nulls rather than silently dropping the row's own history.
        var exchangesById = await (
            from x in _dbContext.Set<Xchange>()
            join r in _dbContext.Set<XchangeResult>() on x.Id equals r.Id into xr
            from r in xr.DefaultIfEmpty()
            join p in _dbContext.Set<XchangePromotedProperties>() on x.Id equals p.Id into xp
            from p in xp.DefaultIfEmpty()
            where exchangeIds.Contains(x.Id)
            select new ReceiveAttemptExchangeRef
            {
                Id = x.Id,
                Status = r.Success,
                ResponseBad = r.ResponseBad,
                PromotedProperties = p == null ? null : p.Properties.ToDictionary(),
            }
        ).ToDictionaryAsync(e => e.Id);

        var result = page.Select(a => new ReceiveAttemptModel
        {
            Id = a.Id,
            StartedOn = a.StartedOn,
            FinishedOn = a.FinishedOn,
            Outcome = a.Outcome,
            ErrorMessage = a.ErrorMessage,
            Exchanges = (a.ExchangeIds ?? Array.Empty<string>())
                .Select(id => exchangesById.TryGetValue(id, out var x) ? x : new ReceiveAttemptExchangeRef { Id = id })
                .ToList(),
        }).ToList();

        return new
        {
            Result = result,
            TotalCount = totalCount,
        };
    }
}
