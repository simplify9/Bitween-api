using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Dashboard;

/// <summary>
/// The two things a retry chain can say that a count of failures cannot: which pieces of work
/// have been retried over and over and are still failing, and whether retrying is achieving
/// anything at all.
/// </summary>
/// <remarks>
/// Both in one handler because both belong to the same panel - a repeat offender means little
/// without knowing whether retries generally work here. Nine attempts against the same connection
/// error is a configuration problem someone has to fix; nine attempts where most retries do
/// eventually succeed is bad luck.
/// </remarks>
[HandlerName("retrysummary")]
public class RetrySummary : IQueryHandler<object>
{
    /// <summary>
    /// How many failures the chain lengths are measured over, newest first. The walk below costs a
    /// query per level for the whole set at once, so this bounds the work without narrowing the
    /// answer in practice: a chain long enough to be listed is recent, because something has been
    /// retrying it.
    /// </summary>
    private const int Candidates = 500;

    /// <summary>Enough to show a pattern; the full list is one link away.</summary>
    private const int Listed = 5;

    /// <summary>Guards the walk against a cycle in the data, as elsewhere.</summary>
    private const int MaxDepth = 100;

    private const int WindowDays = 7;

    private readonly BitweenDbContext _dbContext;
    private readonly RequestContext _requestContext;

    public RetrySummary(BitweenDbContext dbContext, RequestContext requestContext)
    {
        _dbContext = dbContext;
        _requestContext = requestContext;
    }

    public async Task<object> Handle()
    {
        await _requestContext.EnsurePermission(_dbContext, Model.Permissions.Dashboard.View);

        var since = DateTime.UtcNow.AddDays(-WindowDays);

        // Did retrying help? Every attempt that was itself a retry, and how it ended. A retry with
        // no result yet is counted in neither, so the two never add up to more than what finished.
        var retries = await (
            from xchange in _dbContext.Set<Xchange>()
            join result in _dbContext.Set<XchangeResult>() on xchange.Id equals result.Id
            where xchange.RetryFor != null && xchange.StartedOn >= since
            group result by result.Success && !result.ResponseBad into worked
            select new { Worked = worked.Key, Count = worked.Count() }).AsNoTracking().ToListAsync();

        var succeeded = retries.Where(r => r.Worked).Sum(r => r.Count);
        var finished = retries.Sum(r => r.Count);

        // Chains that keep failing: a failure nothing has been retried from - so it is where that
        // chain has got to - which is itself a retry, so the chain is at least two attempts long.
        var candidates = await (
            from xchange in _dbContext.Set<Xchange>()
            join result in _dbContext.Set<XchangeResult>() on xchange.Id equals result.Id
            where xchange.RetryFor != null
                  && !result.Success
                  && !_dbContext.Set<Xchange>().Any(child => child.RetryFor == xchange.Id)
            orderby xchange.StartedOn descending
            select new
            {
                xchange.Id,
                xchange.RetryFor,
                xchange.SubscriptionId,
                xchange.DocumentId,
                xchange.StartedOn,
                result.Exception
            }).AsNoTracking().Take(Candidates).ToListAsync();

        var attempts = await CountAttempts(candidates.ToDictionary(c => c.Id, c => c.RetryFor));

        var worst = candidates
            .OrderByDescending(c => attempts[c.Id])
            .ThenByDescending(c => c.StartedOn)
            .Take(Listed)
            .ToList();

        // Names for the few that are actually listed, rather than for all five hundred.
        var subscriptionIds = worst.Where(w => w.SubscriptionId != null).Select(w => w.SubscriptionId!.Value).ToList();
        var subscriptionNames = await _dbContext.Set<Subscription>().AsNoTracking()
            .Where(s => subscriptionIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name);

        var documentIds = worst.Select(w => w.DocumentId).Distinct().ToList();
        var documentNames = await _dbContext.Set<Document>().AsNoTracking()
            .Where(d => documentIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, d => d.Name);

        return new
        {
            RetriesLast7Days = new { Finished = finished, Succeeded = succeeded },
            FailingChains = worst.Select(w => new
            {
                w.Id,
                Attempts = attempts[w.Id],
                w.SubscriptionId,
                SubscriptionName = w.SubscriptionId != null && subscriptionNames.ContainsKey(w.SubscriptionId.Value)
                    ? subscriptionNames[w.SubscriptionId.Value]
                    : null,
                InformationTypeCode = documentNames.GetValueOrDefault(w.DocumentId),
                w.StartedOn,
                w.Exception
            })
        };
    }

    /// <summary>
    /// How many attempts each chain has taken, counting the original as one.
    /// </summary>
    /// <remarks>
    /// A level of every chain per query rather than a walk per chain - five hundred chains four
    /// attempts deep is four queries, not two thousand. <c>XchangeResult.AttemptNumber</c> holds
    /// this already, but only for failures a retry policy's group matched, so a chain retried by
    /// hand has none and it cannot be read from there.
    /// </remarks>
    private async Task<Dictionary<string, int>> CountAttempts(Dictionary<string, string> parentOf)
    {
        // Every chain starts at two: the candidate is a retry, so something came before it.
        var attempts = parentOf.Keys.ToDictionary(id => id, _ => 2);
        var frontier = new Dictionary<string, string>(parentOf);

        for (var depth = 0; depth < MaxDepth; depth++)
        {
            var ancestors = frontier.Values.Where(id => id != null).Distinct().ToList();
            if (ancestors.Count == 0) break;

            var next = await _dbContext.Set<Xchange>().AsNoTracking()
                .Where(x => ancestors.Contains(x.Id))
                .Select(x => new { x.Id, x.RetryFor })
                .ToDictionaryAsync(x => x.Id, x => x.RetryFor);

            var moved = false;
            foreach (var id in frontier.Keys.ToList())
            {
                var at = frontier[id];
                if (at == null || !next.TryGetValue(at, out var parent) || parent == null)
                {
                    frontier[id] = null;
                    continue;
                }

                frontier[id] = parent;
                attempts[id]++;
                moved = true;
            }

            if (!moved) break;
        }

        return attempts;
    }
}
