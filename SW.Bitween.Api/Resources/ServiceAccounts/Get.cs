using System.Linq;
using System.Threading.Tasks;
using SW.Bitween.Domain.Accounts;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.ServiceAccounts;

public class Get(BitweenDbContext dbContext, RequestContext requestContext) : IGetHandler<int, object>
{
    public async Task<object> Handle(int key)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.ServiceAccounts.View);
        var found = await ServiceAccountStore.Describe(dbContext,
            dbContext.Set<Account>().Where(a => a.Kind == AccountKind.Service && a.Id == key));
        return found.FirstOrDefault() ?? throw new SWNotFoundException(key.ToString());
    }
}
