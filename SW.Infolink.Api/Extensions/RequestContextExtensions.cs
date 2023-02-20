using System.Linq;
using SW.Infolink.Domain.Accounts;
using SW.PrimitiveTypes;

namespace SW.Infolink
{
    public static class RequestContextExtensions
    {
        public static void EnsureAccess(this RequestContext requestContext, params AccountRole[] allowedRoles)
        {

            var jobRole = requestContext.User.GetRole();
            if (jobRole is not null && allowedRoles.All(a => a != jobRole))
                throw new SWUnauthorizedException("INSUFFICIENT_PERMISSIONS");
        }
    }
}