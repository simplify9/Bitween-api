using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Subscriptions;

/// <summary>
/// The paths in the last document a subscription received, for the editor of a response or bus
/// gateway subscription it feeds to offer as source values, and to warn about one that isn't there.
/// </summary>
/// <remarks>
/// Needs <see cref="Model.Permissions.Exchanges.View"/>: the examples are a partner's data, read out
/// of an exchange file. The last exchange that succeeded, because one that failed may have been
/// refused for what it carried.
/// </remarks>
[HandlerName("sourcepaths")]
public class GetSourcePaths(BitweenDbContext dbContext, RequestContext requestContext, XchangeService xchangeService)
    : IQueryHandler<SourcePathsRequest, SourcePathsModel>
{
    public async Task<SourcePathsModel> Handle(SourcePathsRequest request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Exchanges.View);

        var model = new SourcePathsModel { SubscriptionId = request.SubscriptionId };

        var last = await (
            from x in dbContext.Set<Xchange>().AsNoTracking()
            join r in dbContext.Set<XchangeResult>() on x.Id equals r.Id
            where x.SubscriptionId == request.SubscriptionId && r.Success
            orderby x.StartedOn descending
            select x
        ).FirstOrDefaultAsync();
        if (last == null) return model;

        model.XchangeId = last.Id;
        model.ReceivedOn = last.StartedOn;
        try
        {
            model.Paths = SourceDocument.PathsIn(await xchangeService.GetFile(last, XchangeFileType.Input));
        }
        catch (SWValidationException)
        {
            // Its file is gone — deleted by retention, most likely. Nothing to offer, which is
            // what the editor shows for a subscription that hasn't run yet.
        }

        return model;
    }
}
