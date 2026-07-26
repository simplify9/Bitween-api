using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Roles;

public class Get : IGetHandler<int, object>
{
    private readonly BitweenDbContext _dbContext;
    private readonly RequestContext _requestContext;

    public Get(BitweenDbContext dbContext, RequestContext requestContext)
    {
        _dbContext = dbContext;
        _requestContext = requestContext;
    }

    public async Task<object> Handle(int key)
    {
        await _requestContext.EnsurePermission(_dbContext, Model.Permissions.Roles.View);

        var row = await _dbContext.Set<Role>()
            .AsNoTracking()
            .Where(role => role.Id == key)
            .Select(role => new RoleRow
            {
                Id = role.Id,
                Name = role.Name,
                Description = role.Description,
                IsSystem = role.IsSystem,
                Permissions = role.Permissions,
                CreatedOn = role.CreatedOn,
                MemberCount = _dbContext.Set<AccountRoleLink>().Count(l => l.RoleId == role.Id)
            })
            .SingleOrDefaultAsync();

        if (row is not null && row.IsSystem)
            row.Permissions = Role.SystemPermissions(row.Id);

        return row;
    }
}
