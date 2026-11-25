using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Portal;

/// <summary>
/// The signed-in partner's name and their last 24 hours of exchanges by outcome. A day, not all
/// time, so it reads one day of the start-time index however long the history runs.
/// </summary>
[HandlerName("summary")]
public class Summary(BitweenDbContext dbContext, RequestContext requestContext) : IQueryHandler<object>
{
    public async Task<object> Handle()
    {
        var caller = await PortalCaller.EnsureAsync(dbContext, requestContext, Model.Permissions.Portal.ExchangesView);
        var partnerId = caller.PartnerId;
        var since = DateTime.UtcNow.AddDays(-1);

        var day = dbContext.Set<Xchange>().AsNoTracking().Where(x => x.PartnerId == partnerId && x.StartedOn >= since);

        // From the exchange's own status once it is filled in, with no join to the results: a busy
        // partner's day took 100 ms that way on 2M exchanges, against 2 ms on the status. A bad
        // response still delivered, so it counts as a success, as in the list.
        var outcomes = await Services.XchangeStates.XchangeStateReadiness.IsReadyAsync(dbContext)
            ? await day.GroupBy(x => x.Status == XchangeStatus.Processing ? 0 : x.Status == XchangeStatus.Failed ? 2 : 1)
                .Select(g => new { Outcome = g.Key, Count = g.Count() })
                .ToListAsync()
            : await (from x in day
                    join r in dbContext.Set<XchangeResult>() on x.Id equals r.Id into xr
                    from r in xr.DefaultIfEmpty()
                    group r by r == null ? 0 : r.Success ? 1 : 2 into g
                    select new { Outcome = g.Key, Count = g.Count() })
                .ToListAsync();

        return new PortalSummary
        {
            PartnerName = await dbContext.Set<Partner>().AsNoTracking()
                .Where(p => p.Id == partnerId).Select(p => p.Name).SingleOrDefaultAsync() ?? "",
            Running = outcomes.Where(o => o.Outcome == 0).Sum(o => o.Count),
            Succeeded = outcomes.Where(o => o.Outcome == 1).Sum(o => o.Count),
            Failed = outcomes.Where(o => o.Outcome == 2).Sum(o => o.Count)
        };
    }
}
