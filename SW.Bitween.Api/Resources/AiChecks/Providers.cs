using System.Threading.Tasks;
using SW.Bitween.Model;
using SW.Bitween.Services.Ai;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.AiChecks;

/// <summary>
/// The providers Settings, AI offers, with how each one's address is made - its own, a region to
/// pick, or a part to fill in - so the page can ask for that rather than a whole address.
/// </summary>
[HandlerName("providers")]
public class Providers(BitweenDbContext dbContext, RequestContext requestContext) : IQueryHandler<object>
{
    public async Task<object> Handle()
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Settings.View);
        return AiOptions.Catalog;
    }
}
