using System;
using System.Threading.Tasks;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Accounts;

public class Update(BitweenDbContext dbContext, RequestContext requestContext)
    : ICommandHandler<int, UpdateAccountModel, object>
{
    public async Task<object> Handle(int key, UpdateAccountModel request)
    {
        var loggedInUserId = Convert.ToInt32(requestContext.GetNameIdentifier());

        // Anyone may edit their own name; editing someone else needs the grant.
        if (key != loggedInUserId)
            await requestContext.EnsurePermission(dbContext, Model.Permissions.Users.Edit);

        var account = await dbContext.Set<Account>().FindAsync(key);
        if (account is null)
            throw new SWValidationException("ACCOUNT_NOT_FOUND", $"No account exists with the id {key}");

        account.UpdateProfile(request.Name);

        // Changing a role is never self-service, or anyone could promote themselves.
        if (request.Role is not null && (AccountRole)request.Role != account.Role)
        {
            await requestContext.EnsurePermission(dbContext, Model.Permissions.Users.Edit);
            var role = (AccountRole)request.Role;
            await GrantLimits.EnsureCallerOutranks(dbContext, requestContext, key);
            await GrantLimits.EnsureCallerHoldsRoles(dbContext, requestContext, [BuiltInRoleFor(role)]);
            account.SetRole(role);
            await AccountRoles.Set(dbContext, key, [BuiltInRoleFor(role)]);
        }

        await dbContext.SaveChangesAsync();

        return null;
    }

    /// <summary>Maps the legacy coarse role onto the built-in role that reproduces it.</summary>
    private static int BuiltInRoleFor(AccountRole role) => role switch
    {
        AccountRole.Admin => Role.AdministratorId,
        AccountRole.Member => Role.MemberId,
        _ => Role.ViewerId
    };
}
