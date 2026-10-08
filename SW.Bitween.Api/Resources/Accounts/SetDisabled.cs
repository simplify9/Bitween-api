using Microsoft.EntityFrameworkCore;
using System.Linq;
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
public class SetDisabled(BitweenDbContext dbContext, RequestContext requestContext)
    : ICommandHandler<int, SetAccountDisabledModel, object>
{
    public async Task<object> Handle(int key, SetAccountDisabledModel request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Users.Edit);

        var account = await dbContext.Set<Account>().FindAsync(key);
        if (account is null)
            throw new SWValidationException("ACCOUNT_NOT_FOUND", $"No account exists with the id {key}");

        await GrantLimits.EnsureCallerOutranks(dbContext, requestContext, key);

        if (request.Disabled)
        {
            if (key == Convert.ToInt32(requestContext.GetNameIdentifier()))
                throw new SWValidationException("CANNOT_DISABLE_SELF", "You can't disable your own account.");

            await Administrators.EnsureNotTheLast(dbContext, key);
        }

        account.SetDisabled(request.Disabled);
        await dbContext.SaveChangesAsync();

        // A refresh token re-issues a Jwt without asking for the password, so a disabled account's
        // sessions have to go with it — the same reasoning as SetPassword.
        if (request.Disabled)
            await dbContext.Set<RefreshToken>()
                .Where(t => t.AccountId == account.Id)
                .ExecuteDeleteAsync();

        return null;
    }
}
