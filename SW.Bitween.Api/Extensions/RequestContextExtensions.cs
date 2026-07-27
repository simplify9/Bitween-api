using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween
{
    public static class RequestContextExtensions
    {
        /// <summary>
        /// Marks the break-glass token minted by POST /login from configured AdminCredentials. That
        /// token has no account behind it, so its grants can't be resolved from the database. It
        /// used to clear every check only because the old role guard failed open on a missing
        /// claim; this claim makes the same grant deliberate instead of accidental.
        /// </summary>
        public const string SuperuserClaim = "bitween_superuser";

        /// <summary>
        /// Throws unless the caller holds at least one of <paramref name="anyOf"/>. This is really a
        /// "forbidden" — the caller is signed in and simply isn't allowed — but CqApi renders
        /// SWForbiddenException as a 401 that's byte-identical to sending no token, so there is no
        /// distinction to be had on the wire. The client answers a 401 by refreshing the token and
        /// retrying once, which means every denial costs a wasted round trip. Worth revisiting if
        /// CqApi ever maps forbidden to 403.
        /// </summary>
        public static async Task EnsurePermission(this RequestContext requestContext, BitweenDbContext dbContext,
            params string[] anyOf)
        {
            var granted = await requestContext.GetPermissions(dbContext);
            if (!anyOf.Any(granted.Contains))
                throw new SWUnauthorizedException("INSUFFICIENT_PERMISSIONS");
        }

        public static async Task<bool> HasPermission(this RequestContext requestContext, BitweenDbContext dbContext,
            string permission)
        {
            var granted = await requestContext.GetPermissions(dbContext);
            return granted.Contains(permission);
        }

        /// <summary>
        /// The union of every permission the caller's roles grant. Resolved from the database on
        /// each call rather than carried in the token, so revoking a role takes effect immediately
        /// instead of at token expiry. Handlers guard once, so that's one small indexed query.
        /// </summary>
        public static async Task<HashSet<string>> GetPermissions(this RequestContext requestContext,
            BitweenDbContext dbContext)
        {
            if (requestContext.User?.FindFirst(SuperuserClaim) is not null)
                return PermissionCatalog.AllKeys.ToHashSet();

            // Fail closed: no identifiable account means no grants.
            if (!int.TryParse(requestContext.GetNameIdentifier(), out var accountId))
                throw new SWUnauthorizedException("INSUFFICIENT_PERMISSIONS");

            return await GetPermissionsOf(dbContext, accountId);
        }

        public static async Task<HashSet<string>> GetPermissionsOf(BitweenDbContext dbContext, int accountId)
        {
            var roles = await (from link in dbContext.Set<AccountRoleLink>()
                    join role in dbContext.Set<Role>() on link.RoleId equals role.Id
                    where link.AccountId == accountId
                    select new { role.Id, role.IsSystem, role.Permissions })
                .AsNoTracking()
                .ToListAsync();

            var granted = new HashSet<string>();
            foreach (var role in roles)
                granted.UnionWith(role.IsSystem ? Role.SystemPermissions(role.Id) : role.Permissions ?? []);
            return granted;
        }
    }
}
