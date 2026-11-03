using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Resources.Xchanges;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Dashboard;

/// <summary>
/// How many failures nothing has been retried from yet: the ends of chains, the problems still to
/// deal with. The number the exchange list's failed + newest-attempt view counts, counted here from
/// the failures rather than from the exchanges.
/// </summary>
/// <remarks>
/// The dashboard used to ask the list for it, and the list fetches its rows newest first: on a
/// database where nearly every failure gets retried, that walked back through millions of exchanges
/// before reaching one, past the 30s limit - for a row the dashboard never showed. Here the failures
/// come from their own index (ix_xchange_result_failed on Postgres) and the retry check from
/// RetryFor's, so no exchange row is read. Capped like the list's count, so the client shows
/// "10,000+" for either.
/// </remarks>
[HandlerName("failurestoacton")]
public class FailuresToActOn : IQueryHandler<object>
{
    private readonly BitweenDbContext _dbContext;
    private readonly RequestContext _requestContext;

    public FailuresToActOn(BitweenDbContext dbContext, RequestContext requestContext)
    {
        _dbContext = dbContext;
        _requestContext = requestContext;
    }

    public async Task<object> Handle()
    {
        await _requestContext.EnsurePermission(_dbContext, Model.Permissions.Dashboard.View);

        var total = await _dbContext.Set<XchangeResult>().AsNoTracking()
            .Where(result => !result.Success
                             && !_dbContext.Set<Xchange>().Any(child => child.RetryFor == result.Id))
            .Take(Search.CountCap + 1)
            .CountAsync();

        return new { Total = total };
    }
}
