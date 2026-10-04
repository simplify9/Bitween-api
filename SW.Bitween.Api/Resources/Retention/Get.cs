using System.Threading.Tasks;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Retention;

/// <summary>How long exchanges and their files are kept under the settings in force, for the settings page.</summary>
public class Get(BitweenDbContext dbContext, RequestContext requestContext, RetentionPlanner planner)
    : IQueryHandler<RetentionStatusRequest, object>
{
    public async Task<object> Handle(RetentionStatusRequest request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Settings.View);
        return await planner.Plan(refresh: request?.Refresh ?? false);
    }
}
