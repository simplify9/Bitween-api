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
        /// Grants everything to an identity with no account behind it, so there are no roles to
        /// resolve from the database. Only the integration test fixture mints it.
        /// </summary>
        /// <remarks>
        /// It used to mark the token from <c>POST /login</c>, which signed in against a username
        /// and password held in configuration. That defaulted to a working pair published in our
        /// public repository, neither UI ever called it, and a penetration test used it to take
        /// full control of a deployment. The endpoint is gone.
        /// <para>
        /// The claim stays because it is how a caller with no account is granted anything at all.
        /// Minting one needs the signing key, which is enough to impersonate anybody anyway.
        /// </para>
        /// </remarks>
        public const string SuperuserClaim = "bitween_superuser";

        /// <summary>
        /// Present on a token issued to an account whose password nobody has chosen. Such a token
        /// authenticates but grants nothing, so the account can reach self-service — changing the
        /// password — and nothing else.
        /// </summary>
        /// <remarks>
        /// A sign-in has to succeed for the password to be changeable at all: the change requires
        /// the current password and the caller's own identity, so refusing the sign-in outright
        /// would leave the account with no way out but an administrator who may not exist. Granting
        /// nothing is the same thing said in the only place that can act on it.
        /// </remarks>
        public const string MustChangePasswordClaim = "bitween_must_change_password";

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
            // Checked ahead of everything, superuser included: a password nobody chose is not a
            // basis for any grant, whatever else the token claims.
            if (requestContext.User?.FindFirst(MustChangePasswordClaim) is not null)
                return [];

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
