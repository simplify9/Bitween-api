using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.PrimitiveTypes;

namespace SW.Bitween.Domain;

/// <summary>
/// Which of a partner's properties their people see in the portal (<see cref="Partner.PortalAccess"/>),
/// and which they may change. A secret one they may replace but never read.
/// </summary>
public static class PartnerPortalAccess
{
    public const string Visible = "visible";
    public const string Editable = "editable";

    public static HashSet<string> VisibleKeys(Partner partner) =>
        Keys(partner, Visible, Editable);

    /// <summary>The properties a partner's people see, read for one partner.</summary>
    public static async Task<HashSet<string>> VisibleKeysAsync(BitweenDbContext db, int partnerId) =>
        VisibleKeys(await db.Set<Partner>().AsNoTracking().SingleOrDefaultAsync(p => p.Id == partnerId));

    public static HashSet<string> EditableKeys(Partner partner) =>
        Keys(partner, Editable);

    /// <summary>
    /// The properties named at these levels, matched exactly: a name differing only in case is
    /// another property, which saving no longer allows (<see cref="EnsureNoCaseTwins"/>).
    /// </summary>
    private static HashSet<string> Keys(Partner partner, params string[] levels)
    {
        var present = partner?.AdapterProperties ?? new Dictionary<string, string>();
        return (partner?.PortalAccess ?? new Dictionary<string, string>())
            .Where(a => levels.Contains(a.Value) && present.Keys.Any(k => string.Equals(k, a.Key, StringComparison.Ordinal)))
            .Select(a => a.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Refuses properties whose names differ only in case: adapters read them ignoring case, so one
    /// of the two would never be used, and which the partner sees in the portal would be unclear.
    /// </summary>
    public static void EnsureNoCaseTwins(IDictionary<string, string> adapterProperties)
    {
        var twins = (adapterProperties ?? new Dictionary<string, string>()).Keys
            .GroupBy(k => k, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => string.Join(" and ", g))
            .ToList();
        if (twins.Count > 0)
            throw new SWValidationException("DUPLICATE_PROPERTY",
                "Property names must differ by more than case: " + string.Join("; ", twins) + ".");
    }

    /// <summary>
    /// What a save asks for, kept to the properties the partner has and the two levels; anything else
    /// is refused, so a typo never hides or shows the wrong thing silently.
    /// </summary>
    public static Dictionary<string, string> Validate(IDictionary<string, string> access,
        IDictionary<string, string> adapterProperties)
    {
        var present = adapterProperties ?? new Dictionary<string, string>();
        var result = new Dictionary<string, string>();
        foreach (var (key, level) in access ?? new Dictionary<string, string>())
        {
            if (string.IsNullOrEmpty(level) || level == "hidden") continue;
            if (level != Visible && level != Editable)
                throw new SWValidationException("UNKNOWN_PORTAL_ACCESS",
                    $"'{level}' isn't one of hidden, visible or editable.");
            // A property removed in the same save takes its setting with it.
            if (present.ContainsKey(key)) result[key] = level;
        }
        return result;
    }
}
