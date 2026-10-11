using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.Resources.Accounts;
using SW.HttpExtensions;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.ServiceAccounts;

/// <summary>Removes a service account; its tokens are revoked with it.</summary>
public class Delete(BitweenDbContext dbContext, RequestContext requestContext) : IDeleteHandler<int, object>
{
    public async Task<object> Handle(int key)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.ServiceAccounts.Delete);

        var account = await ServiceAccountStore.Load(dbContext, key);
        await GrantLimits.EnsureCallerOutranks(dbContext, requestContext, key);

        var by = requestContext.GetNameIdentifier();
        foreach (var token in await dbContext.Set<ServiceToken>().Where(t => t.AccountId == key && t.RevokedOn == null).ToListAsync())
            token.Revoke(by);

        dbContext.Remove(account);
        await dbContext.SaveChangesAsync();
        return null;
    }
}
