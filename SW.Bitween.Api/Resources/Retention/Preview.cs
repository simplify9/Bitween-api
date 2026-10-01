using System.Threading.Tasks;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Retention;

/// <summary>
/// What retention settings someone is about to save would do, in the same shape as <see cref="Get"/> —
/// shown before the change is confirmed. Changes nothing.
/// </summary>
[HandlerName("preview")]
public class Preview(BitweenDbContext dbContext, RequestContext requestContext, RetentionPlanner planner)
    : ICommandHandler<RetentionProposal, object>
{
    public async Task<object> Handle(RetentionProposal request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Settings.View);
        return await planner.Plan(request);
    }
}
