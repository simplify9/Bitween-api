using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Roles;

public class Get(BitweenDbContext dbContext, RequestContext requestContext) : IGetHandler<int, object>
{
    public async Task<object> Handle(int key)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Roles.View);

        var row = await dbContext.Set<Role>()
            .AsNoTracking()
            .Where(role => role.Id == key)
            .Select(role => new RoleRow
            {
                Id = role.Id,
                Name = role.Name,
                Description = role.Description,
                IsSystem = role.IsSystem,
                ForPartners = role.ForPartners,
                PartnerId = role.PartnerId,
                PartnerName = dbContext.Set<Partner>().Where(p => p.Id == role.PartnerId).Select(p => p.Name).FirstOrDefault(),
                Permissions = role.Permissions,
                CreatedOn = role.CreatedOn,
                MemberCount = dbContext.Set<AccountRoleLink>().Count(l => l.RoleId == role.Id)
            })
            .SingleOrDefaultAsync();

        if (row is not null && row.IsSystem)
            row.Permissions = Role.BuiltInPermissions(row.Id,
                await dbContext.Set<Role>().Where(r => r.Id == row.Id).Select(r => r.PresetKey).SingleAsync());

        return row;
    }
}
