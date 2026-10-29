using System.Linq;
using System.Threading.Tasks;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.ServiceAccounts;

public class Search(BitweenDbContext dbContext, RequestContext requestContext)
    : IQueryHandler<SearchServiceAccountsModel, object>
{
    public async Task<object> Handle(SearchServiceAccountsModel request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.ServiceAccounts.View);
        return await ServiceAccountStore.Describe(dbContext,
            dbContext.Set<Account>().Where(a => a.Kind == AccountKind.Service));
    }
}
