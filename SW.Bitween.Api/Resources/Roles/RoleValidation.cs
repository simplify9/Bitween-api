using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Roles;

internal static class RoleValidation
{
    /// <summary>
    /// Rejects keys the catalog doesn't define. The entity also sanitizes, but failing loudly at
    /// the boundary surfaces a stale UI instead of silently dropping the grants it asked for.
    /// </summary>
    public static void EnsureKnownPermissions(List<string> permissions)
    {
        var unknown = (permissions ?? [])
            .Where(k => !PermissionCatalog.AllKeys.Contains(k))
            .Distinct()
            .ToList();

        if (unknown.Count > 0)
            throw new SWValidationException("UNKNOWN_PERMISSIONS",
                $"These permissions don't exist: {string.Join(", ", unknown)}.");
    }

    public static async Task EnsureNameIsFree(BitweenDbContext dbContext, string name, int? exceptId = null)
    {
        var taken = await dbContext.Set<Role>()
            .AnyAsync(r => r.Name == name && (exceptId == null || r.Id != exceptId));

        if (taken)
            throw new SWValidationException("ROLE_EXISTS", $"A role named '{name}' already exists.");
    }

    public static async Task<Role> Load(BitweenDbContext dbContext, int key)
    {
        var role = await dbContext.Set<Role>().FindAsync(key);
        if (role is null)
            throw new SWValidationException("ROLE_NOT_FOUND", $"No role exists with the id {key}.");
        return role;
    }
}
