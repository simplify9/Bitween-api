using System.Text.RegularExpressions;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain.Gateway;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.ApiGateways;

/// <summary>
/// The url name is the path partners call — <c>/api/gateway/{urlName}/sync</c> — so anything
/// needing escaping there makes a gateway that reads as configured and cannot be reached. A
/// space is the one that actually happens: it saves, the endpoint shown on the page is the one
/// the partner copies, and the call 404s with nothing on screen to explain it.
/// </summary>
/// <remarks>
/// It may run to several segments (<c>logistics/slim/orders</c>): clients lay out their own
/// URL scheme, and the shape differs between them, so no position means anything to us.
/// </remarks>
internal static partial class GatewayUrlName
{
    [GeneratedRegex(@"^[a-z0-9]+(?:[-_][a-z0-9]+)*(?:/[a-z0-9]+(?:[-_][a-z0-9]+)*)*\z")]
    private static partial Regex Allowed();

    public static void Validate(string urlName)
    {
        if (string.IsNullOrWhiteSpace(urlName))
            throw new SWException("UrlName is required");

        if (!Allowed().IsMatch(urlName))
            throw new SWValidationException("GATEWAY_URL_NAME_INVALID",
                $"'{urlName}' cannot be used in a URL. Use lowercase letters, digits, hyphens " +
                "and underscores, with / between parts — no spaces, and no part starting or " +
                "ending with a separator.");

        // The call ends in /sync or /async, and it is the last segment that says which. A name
        // ending in one would make "orders/sync" and "orders" the same address.
        var last = urlName[(urlName.LastIndexOf('/') + 1)..];
        if (last is "sync" or "async")
            throw new SWValidationException("GATEWAY_URL_NAME_INVALID",
                $"'{urlName}' cannot end in '{last}' — partners add /sync or /async after it.");
    }

    /// <summary>
    /// Refuses a url name another gateway already answers on. Pass the gateway's own id when
    /// updating, so saving a gateway without touching its url name isn't a collision with itself.
    /// </summary>
    /// <remarks>
    /// The column is uniquely indexed, so this was already refused — as a constraint violation
    /// that reached the screen as "Request failed (500)", with nothing to say the name was taken
    /// or which gateway has it.
    /// </remarks>
    public static async Task EnsureIsFree(BitweenDbContext dbContext, string urlName, int? existingId = null)
    {
        var taken = await dbContext.Set<ApiGateway>().AsNoTracking()
            // Lowered because rows saved before names had to be lowercase may not be, and the
            // partner's call is matched without regard to case.
            .Where(gateway => gateway.UrlName.ToLower() == urlName && gateway.Id != existingId)
            .Select(gateway => gateway.Name)
            .FirstOrDefaultAsync();

        if (taken != null)
            throw new SWValidationException("GATEWAY_URL_NAME_TAKEN",
                $"'{urlName}' is already the address of the gateway '{taken}'. " +
                "Partners reach a gateway by this name, so two can't share one.");
    }
}
