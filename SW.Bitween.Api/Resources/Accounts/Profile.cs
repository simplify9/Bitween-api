using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Accounts;

/// <summary>
/// Who am I and what may I do. Deliberately ungated — every signed-in caller needs it, and the
/// permissions it returns are what the UI uses to decide which pages and actions to show.
/// </summary>
[HandlerName("profile")]
public class Profile(BitweenDbContext dbContext, RequestContext requestContext) : IQueryHandler<object>
{
    private readonly BitweenDbContext dbContext = dbContext;
    private readonly RequestContext requestContext = requestContext;

    public async Task<object> Handle()
    {
        var accountId = Convert.ToInt32(requestContext.GetNameIdentifier());

        var profile = await dbContext.Set<Account>()
            .AsNoTracking()
            .Where(a => a.Id == accountId)
            .Select(a => new ProfileModel
            {
                CreatedOn = a.CreatedOn,
                Email = a.Email,
                Name = a.DisplayName,
                Id = a.Id,
                Disabled = a.Disabled,
                Role = a.Role.ToString(),
                MustChangePassword = a.MustChangePassword
            })
            .SingleOrDefaultAsync();

        if (profile is null)
            return null;

        profile.Roles = await AccountRoles.Of(dbContext, accountId);
        profile.Permissions = (await requestContext.GetPermissions(dbContext)).OrderBy(p => p).ToList();
        return profile;
    }
}
