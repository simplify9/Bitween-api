using System.Text.RegularExpressions;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.ApiGateways;

/// <summary>
/// The url name is a path segment — partners call <c>/api/Gateway/{urlName}/sync</c> — so
/// anything needing escaping there makes a gateway that reads as configured and cannot be
/// reached. A space is the one that actually happens: it saves, the endpoint shown on the
/// page is the one the partner copies, and the call 404s with nothing on screen to explain it.
/// </summary>
internal static partial class GatewayUrlName
{
    [GeneratedRegex("^[a-z0-9]+(?:[-_][a-z0-9]+)*$")]
    private static partial Regex Allowed();

    public static void Validate(string urlName)
    {
        if (string.IsNullOrWhiteSpace(urlName))
            throw new SWException("UrlName is required");

        if (!Allowed().IsMatch(urlName))
            throw new SWValidationException("GATEWAY_URL_NAME_INVALID",
                $"'{urlName}' cannot be used in a URL. Use lowercase letters, digits, hyphens " +
                "and underscores only — no spaces, and not starting or ending with a separator.");
    }
}
