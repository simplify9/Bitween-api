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

    private static HashSet<string> Keys(Partner partner, params string[] levels)
    {
        var present = partner?.AdapterProperties ?? new Dictionary<string, string>();
        return (partner?.PortalAccess ?? new Dictionary<string, string>())
            .Where(a => levels.Contains(a.Value) && present.ContainsKey(a.Key))
            .Select(a => a.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
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
