using System;
using System.Threading.Tasks;
using SW.Bitween.Domain.Accounts;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Accounts;

[HandlerName("remove")]
public class RemoveAccountModel(BitweenDbContext dbContext, RequestContext requestContext)
    : ICommandHandler<int, RemoveAccountModel,object>
{
    public async Task<object> Handle(int key, RemoveAccountModel request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Users.Delete);

        var account = await dbContext.Set<Account>().FindAsync(key);

        if (account is null)
            throw new SWValidationException("ACCOUNT_NOT_FOUND", $"Account with {key} was not found");

        if (key == Convert.ToInt32(requestContext.GetNameIdentifier()))
            throw new SWValidationException("CANNOT_REMOVE_SELF", "You can't remove your own account.");

        await Administrators.EnsureNotTheLast(dbContext, key);
        await GrantLimits.EnsureCallerOutranks(dbContext, requestContext, key);

        dbContext.Remove(account);
        await dbContext.SaveChangesAsync();

        return null;
    }
}