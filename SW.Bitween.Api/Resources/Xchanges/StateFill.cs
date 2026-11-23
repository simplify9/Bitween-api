using System.Threading.Tasks;
using SW.Bitween.Services.XchangeStates;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Xchanges;

/// <summary>
/// How far the one-time exchange status fill has got, for the note the Exchanges page shows until
/// it is done: until then some of its counts are still worked out the slow way.
/// </summary>
[HandlerName("statefill")]
public class StateFill(BitweenDbContext dbContext, RequestContext requestContext, BitweenOptions options)
    : IQueryHandler<object>
{
    public async Task<object> Handle()
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Exchanges.View, Model.Permissions.Dashboard.View);
        return await XchangeStateProgress.ReadAsync(dbContext, options);
    }
}
