using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain.Accounts;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Accounts;

/// <summary>
/// Nobody hands out more than they hold. Managing members and roles is delegable — a custom role
/// can carry users.edit or roles.edit without being Administrator — and without this a holder of
/// either could grant themselves everything: put themselves in Administrator, add every permission
/// to a role they hold, or set an administrator's password and sign in as them.
/// </summary>
/// <remarks>
/// The rule is a subset check against the caller's own grants, resolved the same way
/// <see cref="RequestContextExtensions.GetPermissions"/> resolves them for every guard. An
/// administrator holds the whole catalog, so nothing an administrator could do before is refused.
/// </remarks>
internal static class GrantLimits
{
    /// <summary>Refuses unless the caller holds every permission <paramref name="roleIds"/> grant.</summary>
    public static async Task EnsureCallerHoldsRoles(BitweenDbContext dbContext, RequestContext requestContext,
        IEnumerable<int> roleIds)
    {
        var ids = (roleIds ?? []).Distinct().ToList();
        if (ids.Count == 0) return;

        var roles = await dbContext.Set<Role>().AsNoTracking().Where(r => ids.Contains(r.Id)).ToListAsync();
        await EnsureCallerHoldsPermissions(dbContext, requestContext,
            roles.SelectMany(r => r.GetEffectivePermissions() ?? []));
    }

    /// <summary>Refuses unless the caller holds every one of <paramref name="permissions"/>.</summary>
    public static async Task EnsureCallerHoldsPermissions(BitweenDbContext dbContext, RequestContext requestContext,
        IEnumerable<string> permissions)
    {
        var granted = await requestContext.GetPermissions(dbContext);
        var beyond = (permissions ?? []).Distinct().Where(p => !granted.Contains(p)).OrderBy(p => p).ToList();
        if (beyond.Count > 0)
            throw new SWValidationException("BEYOND_YOUR_PERMISSIONS",
                "You can only grant permissions you hold yourself. Not held: " + string.Join(", ", beyond) + ".");
    }

    /// <summary>
    /// Refuses unless the caller holds everything the account holds — so a member can't reset,
    /// re-role, disable or remove someone with more access than they have.
    /// </summary>
    public static async Task EnsureCallerOutranks(BitweenDbContext dbContext, RequestContext requestContext,
        int accountId)
    {
        var target = await RequestContextExtensions.GetPermissionsOf(dbContext, accountId);
        var granted = await requestContext.GetPermissions(dbContext);
        if (!target.IsSubsetOf(granted))
            throw new SWValidationException("ACCOUNT_OUTRANKS_YOU",
                "This member holds permissions you don't, so you can't change their account.");
    }
}
