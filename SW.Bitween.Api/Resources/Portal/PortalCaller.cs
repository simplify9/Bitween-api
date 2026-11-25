using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain.Accounts;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Portal;

/// <summary>Who is asking the partner portal, for which partner, and what they may see.</summary>
internal record PortalCaller(int PartnerId, HashSet<string> Granted)
{
    public bool Can(string permission) => Granted.Contains(permission);

    /// <summary>
    /// Refuses unless the caller is a partner's person holding one of <paramref name="anyOf"/>. Our
    /// own administrators hold every key, portal ones too, but have no partner, so they are refused
    /// here as well: the portal only ever shows one partner's data, and that is the caller's own.
    /// </summary>
    public static async Task<PortalCaller> EnsureAsync(BitweenDbContext dbContext, RequestContext requestContext,
        params string[] anyOf)
    {
        var granted = await requestContext.GetPermissions(dbContext);
        if (!anyOf.Any(granted.Contains))
            throw new SWUnauthorizedException("INSUFFICIENT_PERMISSIONS");

        var accountId = Convert.ToInt32(requestContext.GetNameIdentifier());
        var partnerId = await dbContext.Set<Account>().AsNoTracking()
            .Where(a => a.Id == accountId)
            .Select(a => a.PartnerId)
            .SingleOrDefaultAsync();

        return partnerId is { } id
            ? new PortalCaller(id, granted)
            : throw new SWUnauthorizedException("NOT_A_PARTNER_ACCOUNT");
    }
}
