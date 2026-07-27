using System;
using System.Linq;
using System.Threading.Tasks;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Accounts;

/// <summary>Replaces the whole set of roles a member holds.</summary>
[HandlerName("setRoles")]
public class SetRoles : ICommandHandler<int, SetAccountRolesModel, object>
{
    private readonly BitweenDbContext _dbContext;
    private readonly RequestContext _requestContext;

    public SetRoles(BitweenDbContext dbContext, RequestContext requestContext)
    {
        _dbContext = dbContext;
        _requestContext = requestContext;
    }

    public async Task<object> Handle(int key, SetAccountRolesModel request)
    {
        await _requestContext.EnsurePermission(_dbContext, Model.Permissions.Users.Edit);

        var account = await _dbContext.Set<Account>().FindAsync(key);
        if (account is null)
            throw new SWValidationException("ACCOUNT_NOT_FOUND", $"No account exists with the id {key}");

        var roleIds = (request.RoleIds ?? []).Distinct().ToList();

        // Don't let the last administrator be demoted — including by themselves. Otherwise an
        // instance ends up with nobody able to manage members or roles.
        if (!roleIds.Contains(Role.AdministratorId))
            await Administrators.EnsureNotTheLast(_dbContext, key);

        await AccountRoles.Set(_dbContext, key, roleIds);
        account.SetRole(AccountRoles.LegacyRoleFor(roleIds));
        await _dbContext.SaveChangesAsync();

        return null;
    }
}
