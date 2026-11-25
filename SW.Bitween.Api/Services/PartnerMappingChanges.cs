using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;

namespace SW.Bitween.Services;

/// <summary>
/// How a change to a partner's own mapping is made: used at once, or kept waiting for our team when
/// the partner's changes need approval (<see cref="Partner.ChangesNeedApproval"/>). Either way it is
/// recorded as a <see cref="PartnerMappingChange"/>, the mapping's history.
/// </summary>
/// <remarks>
/// A waiting change is never edited: a newer ask is a row of its own and takes back the one before
/// it, so approving a change applies exactly what the reviewer read. Decisions are made by a single
/// statement that matches only a change still waiting, so of two decisions at once only one lands.
/// </remarks>
public static class PartnerMappingChanges
{
    /// <summary>
    /// A partner's change: their rules, or none to go back to ours. Saved here. True when it waits
    /// for approval. Any change of theirs waiting before it is taken back by it.
    /// </summary>
    public static async Task<bool> RequestAsync(BitweenDbContext db, int subscriptionId, int partnerId,
        IDictionary<string, string> mapperProperties, string by)
    {
        var needsApproval = await db.Set<Partner>().Where(p => p.Id == partnerId)
            .Select(p => p.ChangesNeedApproval).SingleAsync();

        var change = new PartnerMappingChange(subscriptionId, partnerId, mapperProperties,
            needsApproval ? PartnerMappingChangeStatus.Waiting : PartnerMappingChangeStatus.Live, by);
        db.Add(change);
        if (!needsApproval) await ApplyAsync(db, subscriptionId, partnerId, mapperProperties?.ToDictionary());
        // Changing a partner mapping clears the cache, this node's and the others'.
        await db.SaveChangesAsync();

        // After the insert, so two saves at once still leave only the newer one waiting.
        await WithdrawWaitingBeforeAsync(db, subscriptionId, partnerId, change.Id, by);
        return needsApproval;
    }

    /// <summary>The change waiting for approval, if there is one; the newest, should two be.</summary>
    public static Task<PartnerMappingChange> WaitingAsync(BitweenDbContext db, int subscriptionId, int partnerId) =>
        db.Set<PartnerMappingChange>().AsNoTracking()
            .Where(c => c.SubscriptionId == subscriptionId && c.PartnerId == partnerId &&
                        c.Status == PartnerMappingChangeStatus.Waiting)
            .OrderByDescending(c => c.Id)
            .FirstOrDefaultAsync();

    /// <summary>
    /// Decides a change, only while it still waits. False when it doesn't: someone else decided it,
    /// the partner took it back, or a newer change of theirs replaced it.
    /// </summary>
    public static async Task<bool> DecideAsync(BitweenDbContext db, int changeId, PartnerMappingChangeStatus status,
        string by, string note = null)
    {
        var now = DateTime.UtcNow;
        return await db.Set<PartnerMappingChange>()
            .Where(c => c.Id == changeId && c.Status == PartnerMappingChangeStatus.Waiting)
            .ExecuteUpdateAsync(set => set
                .SetProperty(c => c.Status, status)
                .SetProperty(c => c.DecidedBy, by)
                .SetProperty(c => c.DecidedOn, now)
                .SetProperty(c => c.Note, note)) == 1;
    }

    /// <summary>An approval that couldn't be applied goes back to waiting, so it can be decided again.</summary>
    public static Task UndecideAsync(BitweenDbContext db, int changeId) =>
        db.Set<PartnerMappingChange>()
            .Where(c => c.Id == changeId && c.Status == PartnerMappingChangeStatus.Approved)
            .ExecuteUpdateAsync(set => set
                .SetProperty(c => c.Status, PartnerMappingChangeStatus.Waiting)
                .SetProperty(c => c.DecidedBy, (string)null)
                .SetProperty(c => c.DecidedOn, (DateTime?)null));

    /// <summary>Takes back the changes still waiting from before this one, which replaces them.</summary>
    public static Task WithdrawWaitingBeforeAsync(BitweenDbContext db, int subscriptionId, int partnerId, int changeId,
        string by)
    {
        var now = DateTime.UtcNow;
        return db.Set<PartnerMappingChange>()
            .Where(c => c.SubscriptionId == subscriptionId && c.PartnerId == partnerId &&
                        c.Status == PartnerMappingChangeStatus.Waiting && c.Id < changeId)
            .ExecuteUpdateAsync(set => set
                .SetProperty(c => c.Status, PartnerMappingChangeStatus.Withdrawn)
                .SetProperty(c => c.DecidedBy, by)
                .SetProperty(c => c.DecidedOn, now));
    }

    /// <summary>Makes the partner's mapping what the change says: theirs, or gone so ours is used. The caller saves.</summary>
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
