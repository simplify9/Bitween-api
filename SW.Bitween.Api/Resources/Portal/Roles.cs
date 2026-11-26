using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.Model;
using SW.Bitween.Resources.Roles;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Portal;

/// <summary>
/// The roles a partner's admin works with: the partner roles we made or built in, which they may
/// give but not change, and the ones they make themselves for their own company's people. Theirs
/// hold partner portal permissions only, never managing people (only our team makes their admins),
/// and never more than the admin holds.
/// </summary>
internal static class PortalRoles
{
    public static IQueryable<Role> Visible(BitweenDbContext db, PortalCaller caller) =>
        db.Set<Role>().AsNoTracking().Where(r => r.ForPartners && (r.PartnerId == null || r.PartnerId == caller.PartnerId));

    /// <summary>As our team's roles list shows them, with only their own company's people counted.</summary>
    public static async Task<List<RoleRow>> RowsAsync(BitweenDbContext db, PortalCaller caller, int? id = null)
    {
        var roles = await Visible(db, caller).Where(r => id == null || r.Id == id)
            .OrderBy(r => r.IsSystem ? 0 : r.PartnerId == null ? 1 : 2).ThenBy(r => r.Name)
            .ToListAsync();
        var ids = roles.Select(r => r.Id).ToList();
        var counts = await db.Set<AccountRoleLink>().AsNoTracking()
            .Where(l => ids.Contains(l.RoleId))
            .Join(db.Set<Account>().Where(a => a.PartnerId == caller.PartnerId && !a.Deleted), l => l.AccountId, a => a.Id, (l, a) => l.RoleId)
            .GroupBy(r => r)
            .Select(g => new { RoleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(c => c.RoleId, c => c.Count);
        return roles.Select(r => new RoleRow
        {
            Id = r.Id,
            Name = r.Name,
            Description = r.Description,
            IsSystem = r.IsSystem,
            ForPartners = true,
            PartnerId = r.PartnerId,
            Permissions = r.GetEffectivePermissions() ?? [],
            CreatedOn = r.CreatedOn,
            MemberCount = counts.GetValueOrDefault(r.Id)
        }).ToList();
    }

    public static void EnsureTheyMayHold(PortalCaller caller, List<string> permissions)
    {
        RoleValidation.EnsureKnownPermissions(permissions);
        RoleValidation.EnsureFitsAudience(true, permissions);
        if (permissions.Contains(Model.Permissions.Portal.PeopleEdit))
            throw new SWValidationException("ROLE_MANAGES_PEOPLE",
                "A role you make can't manage people. Ask us if someone else at your company should.");
        var beyond = permissions.Where(p => !caller.Granted.Contains(p)).Distinct().OrderBy(p => p).ToList();
        if (beyond.Count > 0)
            throw new SWValidationException("BEYOND_YOUR_PERMISSIONS",
                "You can only give what you can do yourself. Not yours: " + string.Join(", ", beyond) + ".");
    }

    /// <summary>One of their own roles; one of ours they may see but not change, and anyone else's isn't there.</summary>
    public static async Task<Role> OwnAsync(BitweenDbContext db, PortalCaller caller, int id)
    {
        var role = await db.Set<Role>().FindAsync(id);
        if (role is null || !role.ForPartners || (role.PartnerId != null && role.PartnerId != caller.PartnerId))
            throw new SWNotFoundException(id.ToString());
        if (role.PartnerId == null)
            throw new SWValidationException("ROLE_IS_OURS",
                $"'{role.Name}' is one of ours, so you can't change it. Duplicate it to make your own.");
        return role;
    }
}

/// <summary>The partner portal's permissions, the only ones their roles hold, for their role editor.</summary>
[HandlerName("permissions")]
public class PortalPermissions(BitweenDbContext dbContext, RequestContext requestContext) : IQueryHandler<object>
{
    public async Task<object> Handle()
    {
        await PortalCaller.EnsureAsync(dbContext, requestContext,
            Model.Permissions.Portal.PeopleView, Model.Permissions.Portal.PeopleEdit);
        return PermissionCatalog.Areas.Where(a => a.Group == PermissionCatalog.PortalGroup).ToList();
    }
}

/// <summary>The roles their admin sees and gives, with how many of their people hold each.</summary>
[HandlerName("roles")]
public class PortalRoleList(BitweenDbContext dbContext, RequestContext requestContext) : IQueryHandler<object>
{
    public async Task<object> Handle()
    {
        var caller = await PortalCaller.EnsureAsync(dbContext, requestContext,
            Model.Permissions.Portal.PeopleView, Model.Permissions.Portal.PeopleEdit);
        return await PortalRoles.RowsAsync(dbContext, caller);
    }
}

[HandlerName("role")]
public class PortalRoleGet(BitweenDbContext dbContext, RequestContext requestContext) : IQueryHandler<PortalRoleRequest, object>
{
    public async Task<object> Handle(PortalRoleRequest request)
    {
        var caller = await PortalCaller.EnsureAsync(dbContext, requestContext,
            Model.Permissions.Portal.PeopleView, Model.Permissions.Portal.PeopleEdit);
        return (await PortalRoles.RowsAsync(dbContext, caller, request.Id)).SingleOrDefault()
               ?? throw new SWNotFoundException(request.Id.ToString());
    }
}

/// <summary>Makes a role for their own company's people, or changes one of theirs.</summary>
[HandlerName("saverole")]
public class PortalRoleSaveHandler(BitweenDbContext dbContext, RequestContext requestContext)
    : ICommandHandler<PortalRoleSave, object>
{
    public async Task<object> Handle(PortalRoleSave request)
    {
        var caller = await PortalCaller.EnsureAsync(dbContext, requestContext, Model.Permissions.Portal.PeopleEdit);
        var permissions = request.Permissions ?? [];
        PortalRoles.EnsureTheyMayHold(caller, permissions);
        var name = request.Name.Trim();

        if (request.Id is not { } id)
        {
            await RoleValidation.EnsureNameIsFree(dbContext, name, ownerPartnerId: caller.PartnerId);
            var role = Role.OwnedBy(caller.PartnerId, name, request.Description, permissions);
            dbContext.Add(role);
            await dbContext.SaveChangesAsync();
            return role.Id;
        }

        var own = await PortalRoles.OwnAsync(dbContext, caller, id);
        // What it grants now counts too: changing a role past what they hold is as much a step as making one.
        PortalRoles.EnsureTheyMayHold(caller, own.Permissions ?? []);
        await RoleValidation.EnsureNameIsFree(dbContext, name, id, caller.PartnerId);
        own.Update(name, request.Description, permissions);
        await dbContext.SaveChangesAsync();
        return id;
    }

    private class Validate : AbstractValidator<PortalRoleSave>
    {
        public Validate()
        {
            RuleFor(i => i.Name).NotEmpty().MaximumLength(100);
            RuleFor(i => i.Description).MaximumLength(500);
        }
    }
}

/// <summary>Deletes one of their own roles nobody holds.</summary>
[HandlerName("deleterole")]
public class PortalRoleDelete(BitweenDbContext dbContext, RequestContext requestContext)
    : ICommandHandler<PortalRoleRequest, object>
{
    public async Task<object> Handle(PortalRoleRequest request)
    {
        var caller = await PortalCaller.EnsureAsync(dbContext, requestContext, Model.Permissions.Portal.PeopleEdit);
        var role = await PortalRoles.OwnAsync(dbContext, caller, request.Id);
        var holders = await dbContext.Set<AccountRoleLink>().CountAsync(l => l.RoleId == role.Id);
        if (holders > 0)
            throw new SWValidationException("ROLE_IN_USE",
                $"'{role.Name}' is still given to {holders} {(holders == 1 ? "person" : "people")}. Give them another role first.");
        dbContext.Remove(role);
        await dbContext.SaveChangesAsync();
        return null;
    }
}
