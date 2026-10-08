using System;
using System.Linq;
using System.Threading.Tasks;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Accounts;

/// <summary>Replaces the whole set of roles a member holds.</summary>
[HandlerName("setRoles")]
public class SetRoles(BitweenDbContext dbContext, RequestContext requestContext)
    : ICommandHandler<int, SetAccountRolesModel, object>
{
    public async Task<object> Handle(int key, SetAccountRolesModel request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Users.Edit);

        var account = await dbContext.Set<Account>().FindAsync(key);
        if (account is null)
            throw new SWValidationException("ACCOUNT_NOT_FOUND", $"No account exists with the id {key}");

        var roleIds = (request.RoleIds ?? []).Distinct().ToList();

        await GrantLimits.EnsureCallerOutranks(dbContext, requestContext, key);
        await GrantLimits.EnsureCallerHoldsRoles(dbContext, requestContext, roleIds);

        // Don't let the last administrator be demoted — including by themselves. Otherwise an
        // instance ends up with nobody able to manage members or roles.
        if (!roleIds.Contains(Role.AdministratorId))
            await Administrators.EnsureNotTheLast(dbContext, key);

        await AccountRoles.Set(dbContext, key, roleIds);
        account.SetRole(AccountRoles.LegacyRoleFor(roleIds));
        await dbContext.SaveChangesAsync();

        return null;
    }
}
