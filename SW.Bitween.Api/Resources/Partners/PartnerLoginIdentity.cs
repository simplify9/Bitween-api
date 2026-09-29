using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Partners;

/// <summary>
/// A JWT gateway finds the calling partner by this value alone, so two partners holding the same
/// one would make a token mean either of them. The column is uniquely indexed too; this says which
/// partner has it rather than failing the save as a constraint violation.
/// </summary>
internal static class PartnerLoginIdentity
{
    /// <returns>The identity to store: trimmed, and null when blank.</returns>
    public static async Task<string> Validate(BitweenDbContext dbContext, string identity, int? partnerId = null)
    {
        var value = string.IsNullOrWhiteSpace(identity) ? null : identity.Trim();
        if (value == null)
            return null;

        var holder = await dbContext.Set<Partner>().AsNoTracking()
            .Where(p => p.LoginIdentity == value && p.Id != partnerId)
            .Select(p => p.Name)
            .FirstOrDefaultAsync();

        return holder == null
            ? value
            : throw new SWValidationException("LOGIN_IDENTITY_TAKEN",
                $"'{holder}' already has the login server identity '{value}'. A token names exactly one partner.");
    }
}
