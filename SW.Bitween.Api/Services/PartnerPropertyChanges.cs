using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.PrimitiveTypes;

namespace SW.Bitween.Services;

/// <summary>
/// How a partner's change to their own properties is made: used at once, or kept waiting for our team
/// when their changes need approval. The same rules as <see cref="PartnerMappingChanges"/>.
/// </summary>
public static class PartnerPropertyChanges
{
    /// <summary>
    /// A partner's change, only properties they may edit. Saved here. True when it waits for approval.
    /// Any change of theirs waiting before it is taken back by it.
    /// </summary>
    public static async Task<bool> RequestAsync(BitweenDbContext db, Partner partner, IDictionary<string, string> values,
        string by)
    {
        var change = new PartnerPropertyChange(partner.Id, values,
            partner.ChangesNeedApproval ? PartnerChangeStatus.Waiting : PartnerChangeStatus.Live, by);
        db.Add(change);
        if (!partner.ChangesNeedApproval) Apply(partner, change.Values);
        // Changing a partner clears the cache, this node's and the others'.
        await db.SaveChangesAsync();
        await WithdrawWaitingBeforeAsync(db, partner.Id, change.Id, by);
        return partner.ChangesNeedApproval;
    }

    public static Task<PartnerPropertyChange> WaitingAsync(BitweenDbContext db, int partnerId) =>
        db.Set<PartnerPropertyChange>().AsNoTracking()
            .Where(c => c.PartnerId == partnerId && c.Status == PartnerChangeStatus.Waiting)
            .OrderByDescending(c => c.Id)
            .FirstOrDefaultAsync();

    /// <summary>
    /// Refuses values the partner may no longer change, as things are now: our team may have changed
    /// what they may edit, or removed the property, since they asked.
    /// </summary>
    public static void EnsureEditable(Partner partner, IReadOnlyDictionary<string, string> values)
    {
        var editable = PartnerPortalAccess.EditableKeys(partner);
        var beyond = values.Keys.Where(k => !editable.Contains(k)).OrderBy(k => k).ToList();
        if (beyond.Count > 0)
            throw new SWValidationException("NOT_EDITABLE",
                "These can't be changed by the partner: " + string.Join(", ", beyond) + ".");
    }

    /// <summary>Puts the values on the partner. The caller saves.</summary>
    public static void Apply(Partner partner, IReadOnlyDictionary<string, string> values)
    {
        // A new dictionary, so the change is seen and saved.
        var next = new Dictionary<string, string>(partner.AdapterProperties ?? new Dictionary<string, string>());
        foreach (var (key, value) in values)
        {
            var stored = next.Keys.FirstOrDefault(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) ?? key;
            next[stored] = value;
        }
        partner.AdapterProperties = next;
    }

    /// <summary>Decides a change, only while it still waits. False when it doesn't.</summary>
    public static async Task<bool> DecideAsync(BitweenDbContext db, int changeId, PartnerChangeStatus status,
        string by, string note = null)
    {
        var now = DateTime.UtcNow;
        return await db.Set<PartnerPropertyChange>()
            .Where(c => c.Id == changeId && c.Status == PartnerChangeStatus.Waiting)
            .ExecuteUpdateAsync(set => set
                .SetProperty(c => c.Status, status)
                .SetProperty(c => c.DecidedBy, by)
                .SetProperty(c => c.DecidedOn, now)
                .SetProperty(c => c.Note, note)) == 1;
    }

    public static Task UndecideAsync(BitweenDbContext db, int changeId) =>
        db.Set<PartnerPropertyChange>()
            .Where(c => c.Id == changeId && c.Status == PartnerChangeStatus.Approved)
            .ExecuteUpdateAsync(set => set
                .SetProperty(c => c.Status, PartnerChangeStatus.Waiting)
                .SetProperty(c => c.DecidedBy, (string)null)
                .SetProperty(c => c.DecidedOn, (DateTime?)null));

    private static Task WithdrawWaitingBeforeAsync(BitweenDbContext db, int partnerId, int changeId, string by)
    {
        var now = DateTime.UtcNow;
        return db.Set<PartnerPropertyChange>()
            .Where(c => c.PartnerId == partnerId && c.Status == PartnerChangeStatus.Waiting && c.Id < changeId)
            .ExecuteUpdateAsync(set => set
                .SetProperty(c => c.Status, PartnerChangeStatus.Withdrawn)
                .SetProperty(c => c.DecidedBy, by)
                .SetProperty(c => c.DecidedOn, now));
    }
}
