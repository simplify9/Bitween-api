using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SW.Bitween.Services;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Settings;

[Unprotect]
[HandlerName("Config")]
public class Config(BitweenOptions BitweenOptions, ThemeOptions themeOptions) : IQueryHandler<object>
{
    /// <summary>
    /// Brand links that name the vendor rather than the deployment, and so are withheld while they
    /// still hold the value we shipped.
    /// </summary>
    /// <remarks>
    /// This endpoint answers before anyone signs in, because the sign-in page brands itself from
    /// it. A penetration test followed the default <c>GithubLink</c> from here to our public
    /// repository, and from there to the sample signing key and the default administrator password
    /// — the whole chain started with a link nobody had configured.
    /// <para>
    /// Withheld only at the default: a deployment that sets its own link is publishing its own
    /// address and gets it back untouched. So the footer keeps working wherever it was meant to,
    /// and stops pointing strangers at our source everywhere else.
    /// </para>
    /// </remarks>
    private static readonly string[] VendorLinks = ["GithubLink"];

    public async Task<object> Handle()
    {
        var defaults = new ThemeOptions();
        var withheld = VendorLinks
            .Where(name => Equals(Read(themeOptions, name), Read(defaults, name)))
            .ToHashSet(StringComparer.Ordinal);

        return new
        {
            BitweenOptions.MsalClientId,
            BitweenOptions.MsalRedirectUri,
            BitweenOptions.MsalTenantId,
            BitweenOptions.DisableEmailPasswordLogin,
            IsRabbitMqManagementConfigured = !string.IsNullOrWhiteSpace(BitweenOptions.RabbitMqManagementUrl)
                                             && !string.IsNullOrWhiteSpace(BitweenOptions.RabbitMqManagementUsername)
                                             && !string.IsNullOrWhiteSpace(BitweenOptions.RabbitMqManagementPassword),
            Theme = ThemeWithout(withheld),
            // The product defaults, so the sign-in page — which has no session and can't read the
            // settings list — can tell a brand value someone chose from one nobody has touched.
            ThemeDefaults = SettingsService.DefaultsUnder("Theme.")
                .Where(kv => !withheld.Contains(Pascalize(kv.Key)))
                .ToDictionary(kv => kv.Key, kv => kv.Value)
        };
    }

    /// <summary>
    /// The theme as a dictionary so a key can be left out of it.
    /// </summary>
    /// <remarks>
    /// Built by reflection rather than written out property by property: a hand-written projection
    /// silently drops whatever is added to <see cref="ThemeOptions"/> next, and the symptom — one
    /// brand value that will not apply — looks nothing like its cause.
    /// </remarks>
    private Dictionary<string, object?> ThemeWithout(IReadOnlySet<string> withheld) =>
        typeof(ThemeOptions).GetProperties()
            .Where(p => p.CanRead && !withheld.Contains(p.Name))
            .ToDictionary(p => Camelize(p.Name), p => p.GetValue(themeOptions));

    private static object? Read(ThemeOptions theme, string propertyName) =>
        typeof(ThemeOptions).GetProperty(propertyName)?.GetValue(theme);

    private static string Camelize(string name) => char.ToLowerInvariant(name[0]) + name[1..];

    private static string Pascalize(string name) => char.ToUpperInvariant(name[0]) + name[1..];
}
