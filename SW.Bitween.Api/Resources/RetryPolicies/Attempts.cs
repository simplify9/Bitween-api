using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.RetryPolicies;

/// <summary>
/// Lists the failures one group caught for one subscription — what a row of <see cref="Usage"/>
/// spent its budget on.
/// </summary>
/// <remarks>
/// <para>
/// Separate from <see cref="Usage"/> and asked for one pair at a time, because a policy with fifty
/// subscriptions would otherwise pay for fifty of these joins to answer a question about one row.
/// </para>
/// <para>
/// Only failures carrying a group id appear, so nothing recorded before the group was stamped onto
/// results is listed. A pair whose counter is well spent can therefore come back empty, which is
/// why <see cref="RetryGroupAttempts.Total"/> is the count of what is listable rather than the
/// counter's own value.
/// </para>
/// </remarks>
[HandlerName("attempts")]
public class Attempts : ICommandHandler<int, RetryGroupAttemptsRequest, object>
{
    /// <summary>
    /// Enough to show what keeps failing without turning one table row into a page. The caller is
    /// told the total, so a short list never reads as the whole story.
    /// </summary>
    private const int Limit = 10;

    private readonly BitweenDbContext _dbContext;
    private readonly RequestContext _requestContext;

    public Attempts(BitweenDbContext dbContext, RequestContext requestContext)
    {
        _dbContext = dbContext;
        _requestContext = requestContext;
    }

    public async Task<object> Handle(int key, RetryGroupAttemptsRequest request)
    {
        _requestContext.EnsureAccess(AccountRole.Admin, AccountRole.Member);

        // Both halves of the pair have to belong to the policy in the route. For the subscription
        // that keeps this from becoming a way to read any subscription's failures through any
        // policy id; for the group it is about the answer being readable — an unknown group would
        // otherwise report zero failures, which is indistinguishable from a group that genuinely
        // has none.
        var policy = await _dbContext.Set<RetryPolicy>().AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == key);
        if (policy == null) throw new SWNotFoundException(key.ToString());

        if (policy.Groups.All(g => g.Id != request.GroupId))
            throw new SWNotFoundException($"{key}/{request.GroupId}");

        var belongs = await _dbContext.Set<Subscription>().AsNoTracking()
            .AnyAsync(s => s.Id == request.SubscriptionId && s.RetryPolicyId == key);
        if (!belongs) throw new SWNotFoundException($"{key}/{request.SubscriptionId}");

        var query = from result in _dbContext.Set<XchangeResult>()
            join xchange in _dbContext.Set<Xchange>() on result.Id equals xchange.Id
            join pending in _dbContext.Set<DelayedRetry>() on result.Id equals pending.Id into scheduled
            from pending in scheduled.DefaultIfEmpty()
            where xchange.SubscriptionId == request.SubscriptionId
                  && result.RetryGroupId == request.GroupId
            select new RetryGroupAttemptRow
            {
                XchangeId = result.Id,
                AttemptNumber = result.AttemptNumber,
                FailedOn = result.FinishedOn,
                Exception = result.Exception,
                // A row survives here only until its retry runs, which is what separates a failure
                // still being worked on from one that has been given up.
                RetryPending = pending != null,
                RetryBlockedReason = result.RetryBlockedReason
            };

        query = query.AsNoTracking();

        return new RetryGroupAttempts
        {
            Total = await query.CountAsync(),
            // Pending first, so the ones still moving cannot be pushed out of the list by a long
            // history of failures that are already over.
            Attempts = await query
                .OrderByDescending(r => r.RetryPending)
                .ThenByDescending(r => r.FailedOn)
                .Take(Limit)
                .ToListAsync()
        };
    }
}
