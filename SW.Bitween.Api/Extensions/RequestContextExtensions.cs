using System.Linq;
using SW.Bitween.Domain.Accounts;
using SW.PrimitiveTypes;

namespace SW.Bitween
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