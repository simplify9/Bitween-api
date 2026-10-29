using SW.Bitween.Resources.Accounts;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Roles;

public class Delete(BitweenDbContext dbContext, RequestContext requestContext) : IDeleteHandler<int, object>
{
    public async Task<object> Handle(int key)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Roles.Delete);

        var role = await RoleValidation.Load(dbContext, key);

        if (role.IsSystem)
            throw new SWValidationException("ROLE_IS_BUILT_IN",
                $"'{role.Name}' is a built-in role and can't be deleted.");

        await GrantLimits.EnsureCallerHoldsPermissions(dbContext, requestContext, role.Permissions);

        var memberCount = await dbContext.Set<AccountRoleLink>().CountAsync(l => l.RoleId == key);
        if (memberCount > 0)
            throw new SWValidationException("ROLE_IN_USE",
                $"'{role.Name}' is still assigned to {memberCount} member{(memberCount == 1 ? "" : "s")}. " +
                "Move them to another role first.");

        dbContext.Remove(role);
        await dbContext.SaveChangesAsync();
        return null;
    }
}
