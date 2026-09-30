using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.EfCoreExtensions;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.RetryPolicies;

public class Get(BitweenDbContext dbContext, RequestContext requestContext)
    : IGetHandler<int, object>
{
    public async Task<object> Handle(int key)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.RetryPolicies.View);

        var policy = await dbContext.Set<RetryPolicy>()
            .AsNoTracking()
            .Search("Id", key)
            .SingleOrDefaultAsync();

        if (policy == null) return null;

        return new RetryPolicyUpdate
        {
            Name = policy.Name,
            Groups = policy.Groups,
            AlertChannelId = policy.AlertChannelId
        };
    }
}
