using System;
using System.Threading.Tasks;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Accounts;

/// <summary>
/// Suspends or restores an account. A disabled account keeps its roles and history but can't sign
/// in — see the Disabled check in the login handler.
/// </summary>
[HandlerName("setDisabled")]
public class SetDisabled : ICommandHandler<int, SetAccountDisabledModel, object>
{
    private readonly BitweenDbContext _dbContext;
    private readonly RequestContext _requestContext;

    public SetDisabled(BitweenDbContext dbContext, RequestContext requestContext)
    {
        _dbContext = dbContext;
        _requestContext = requestContext;
    }

    public async Task<object> Handle(int key, SetAccountDisabledModel request)
    {
        await _requestContext.EnsurePermission(_dbContext, Model.Permissions.Users.Edit);

        var account = await _dbContext.Set<Account>().FindAsync(key);
        if (account is null)
            throw new SWValidationException("ACCOUNT_NOT_FOUND", $"No account exists with the id {key}");

        if (request.Disabled)
        {
            if (key == Convert.ToInt32(_requestContext.GetNameIdentifier()))
                throw new SWValidationException("CANNOT_DISABLE_SELF", "You can't disable your own account.");

            await Administrators.EnsureNotTheLast(_dbContext, key);
        }

        account.SetDisabled(request.Disabled);
        await _dbContext.SaveChangesAsync();

        return null;
    }
}
