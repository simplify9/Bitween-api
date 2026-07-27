using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Roles;

public class Delete : IDeleteHandler<int, object>
{
    private readonly BitweenDbContext _dbContext;
    private readonly RequestContext _requestContext;

    public Delete(BitweenDbContext dbContext, RequestContext requestContext)
    {
        _dbContext = dbContext;
        _requestContext = requestContext;
    }

    public async Task<object> Handle(int key)
    {
        await _requestContext.EnsurePermission(_dbContext, Model.Permissions.Roles.Delete);

        var role = await RoleValidation.Load(_dbContext, key);

        if (role.IsSystem)
            throw new SWValidationException("ROLE_IS_BUILT_IN",
                $"'{role.Name}' is a built-in role and can't be deleted.");

        var memberCount = await _dbContext.Set<AccountRoleLink>().CountAsync(l => l.RoleId == key);
        if (memberCount > 0)
            throw new SWValidationException("ROLE_IN_USE",
                $"'{role.Name}' is still assigned to {memberCount} member{(memberCount == 1 ? "" : "s")}. " +
                "Move them to another role first.");

        _dbContext.Remove(role);
        await _dbContext.SaveChangesAsync();
        return null;
    }
}
