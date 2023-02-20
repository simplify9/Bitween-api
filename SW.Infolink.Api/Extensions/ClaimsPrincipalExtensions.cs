using System;
using System.Security.Claims;
using SW.Infolink.Domain.Accounts;

namespace SW.Infolink
{
    static class ClaimsPrincipalExtensions
    {
        public static AccountRole? GetRole(this ClaimsPrincipal claimsPrincipal)
        {
            var role = claimsPrincipal?.FindFirst("Role");
            if (role is null)
                return null;
            return Enum.Parse<AccountRole>(role.Value);
        }
    }
}