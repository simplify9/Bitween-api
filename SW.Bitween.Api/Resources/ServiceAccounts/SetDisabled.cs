using System.Threading.Tasks;
using SW.Bitween.Model;
using SW.Bitween.Resources.Accounts;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.ServiceAccounts;

/// <summary>
/// Stops or restores every token the account holds at once. Its sessions end too: permissions are
/// read per call, and a disabled account holds none.
/// </summary>
[HandlerName("setDisabled")]
public class SetDisabled(BitweenDbContext dbContext, RequestContext requestContext)
    : ICommandHandler<int, SetServiceAccountDisabledModel, object>
{
    public async Task<object> Handle(int key, SetServiceAccountDisabledModel request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.ServiceAccounts.Edit);

        var account = await ServiceAccountStore.Load(dbContext, key);
        await GrantLimits.EnsureCallerOutranks(dbContext, requestContext, key);

        account.SetDisabled(request.Disabled);
        await dbContext.SaveChangesAsync();
        return null;
    }
}
