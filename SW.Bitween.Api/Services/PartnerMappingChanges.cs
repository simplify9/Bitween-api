using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;

namespace SW.Bitween.Services;

/// <summary>
/// How a change to a partner's own mapping is made: used at once, or kept waiting for our team when
/// the partner's changes need approval (<see cref="Partner.ChangesNeedApproval"/>). Either way it is
/// recorded as a <see cref="PartnerMappingChange"/>, the mapping's history. The caller saves.
/// </summary>
public static class PartnerMappingChanges
{
    /// <summary>
    /// A partner's change: their rules, or none to go back to ours. True when it waits for approval.
    /// A change already waiting is replaced by this one; one that goes live withdraws it.
    /// </summary>
    public static async Task<bool> RequestAsync(BitweenDbContext db, int subscriptionId, int partnerId,
        IDictionary<string, string> mapperProperties, string by)
    {
        var needsApproval = await db.Set<Partner>().Where(p => p.Id == partnerId)
            .Select(p => p.ChangesNeedApproval).SingleAsync();
        var waiting = await WaitingAsync(db, subscriptionId, partnerId);

        if (needsApproval)
        {
            if (waiting is null)
                db.Add(new PartnerMappingChange(subscriptionId, partnerId, mapperProperties,
                    PartnerMappingChangeStatus.Waiting, by));
            else
                waiting.Replace(mapperProperties, by);
            return true;
        }

        waiting?.Withdraw(by);
        await ApplyAsync(db, subscriptionId, partnerId, mapperProperties?.ToDictionary());
        db.Add(new PartnerMappingChange(subscriptionId, partnerId, mapperProperties, PartnerMappingChangeStatus.Live, by));
        return false;
    }

    /// <summary>The change waiting for approval, if there is one.</summary>
    public static Task<PartnerMappingChange> WaitingAsync(BitweenDbContext db, int subscriptionId, int partnerId) =>
        db.Set<PartnerMappingChange>()
            .Where(c => c.SubscriptionId == subscriptionId && c.PartnerId == partnerId &&
                        c.Status == PartnerMappingChangeStatus.Waiting)
            .OrderByDescending(c => c.Id)
            .FirstOrDefaultAsync();

    /// <summary>Makes the partner's mapping what the change says: theirs, or gone so ours is used.</summary>
    public static async Task ApplyAsync(BitweenDbContext db, int subscriptionId, int partnerId,
        IReadOnlyDictionary<string, string> mapperProperties)
    {
        var theirs = await db.Set<PartnerMapping>()
            .SingleOrDefaultAsync(m => m.SubscriptionId == subscriptionId && m.PartnerId == partnerId);
        if (mapperProperties is null || mapperProperties.Count == 0)
        {
            if (theirs is not null) db.Remove(theirs);
        }
        else if (theirs is null)
            db.Add(new PartnerMapping(subscriptionId, partnerId, mapperProperties.ToDictionary()));
        else
            theirs.SetMapperProperties(mapperProperties.ToDictionary());
    }
}
