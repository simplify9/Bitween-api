using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SW.Bitween.Services;

public enum SettingKind
{
    String,
    Number,
    Boolean,
    Color
}

/// <summary>The two option singletons a setting can read from and write to.</summary>
public sealed record SettingsTarget(BitweenOptions Bitween, ThemeOptions Theme);

/// <summary>
/// Everything known about one setting except its value: how to show it, and how to read and
/// write it on the live options singletons.
/// </summary>
public sealed record SettingDefinition(
    string Key,
    string Section,
    string Label,
    string Description,
    SettingKind Kind,
    bool Secret,
    Func<SettingsTarget, string> Read,
    Action<SettingsTarget, string> Write);

/// <summary>
/// The settings a user may change from the UI, and the only ones the <c>Settings</c> table ever
/// holds. Membership rule: a setting belongs here <b>only</b> if every consumer reads it from
/// the options singleton per call, so changing it takes effect immediately. Anything captured
/// once during <c>Startup.ConfigureServices</c> — the bus, CORS, storage, JWT signing, Quartz,
/// the DB provider — stays environment-only, because a stored value could never apply without
/// a restart.
/// </summary>
public static class SettingsCatalog
{
    public static readonly IReadOnlyList<SettingDefinition> All =
    [
        // ——— Documents & storage ———
        new("Bitween.AreXChangeFilesPrivate", "Documents & storage",
            "Keep exchange files private",
            "Turn this on if you want generated exchange files kept private and served through short-lived signed links instead of public URLs.",
            SettingKind.Boolean, false,
            t => Str(t.Bitween.AreXChangeFilesPrivate),
            (t, v) => t.Bitween.AreXChangeFilesPrivate = Bool(v)),

        // ——— API behavior ———
        new("Bitween.ApiCallSubscriptionResponseAcceptedStatusCode", "API behavior",
            "Accepted response status code",
            "Change this if a partner's API expects a different HTTP status code (instead of 202 Accepted) when their request has been queued for async processing.",
            SettingKind.Number, false,
            t => t.Bitween.ApiCallSubscriptionResponseAcceptedStatusCode?.ToString(CultureInfo.InvariantCulture),
            (t, v) => t.Bitween.ApiCallSubscriptionResponseAcceptedStatusCode = NullableInt(v)),

        new("Bitween.JwtExpiryMinutes", "API behavior",
            "Sign-in session length (minutes)",
            "Shorten this for tighter session security, or lengthen it if teammates are being signed out more often than you'd like. Applies to sessions started after the change.",
            SettingKind.Number, false,
            t => t.Bitween.JwtExpiryMinutes.ToString(CultureInfo.InvariantCulture),
            (t, v) => t.Bitween.JwtExpiryMinutes = Int(v)),

        // ——— Single sign-on (Microsoft) ———
        // Not secrets: these are public client identifiers, and the [Unprotect] Config endpoint
        // already serves them to anonymous visitors so the login page can offer the MS button.
        new("Bitween.MsalClientId", "Single sign-on (Microsoft)",
            "Azure AD client ID",
            "Add this — together with the tenant ID and redirect URI below — if you want to let teammates sign in with a Microsoft account. All three are required for Microsoft sign-in to turn on.",
            SettingKind.String, false,
            t => t.Bitween.MsalClientId,
            (t, v) => t.Bitween.MsalClientId = v),

        new("Bitween.MsalTenantId", "Single sign-on (Microsoft)",
            "Azure AD tenant ID",
            "The Azure AD tenant Microsoft sign-in is restricted to. Required alongside the client ID and redirect URI.",
            SettingKind.String, false,
            t => t.Bitween.MsalTenantId,
            (t, v) => t.Bitween.MsalTenantId = v),

        new("Bitween.MsalRedirectUri", "Single sign-on (Microsoft)",
            "Azure AD redirect URI",
            "The URL Azure AD sends users back to after signing in — must match the redirect URI registered on the Azure AD app. Required alongside the client ID and tenant ID.",
            SettingKind.String, false,
            t => t.Bitween.MsalRedirectUri,
            (t, v) => t.Bitween.MsalRedirectUri = v),

        // ——— Adapters ———
        new("Bitween.RebexLicenseKey", "Adapters",
            "Rebex license key",
            "Add this if you want the native POP3 and FTP adapters, which are built on the Rebex library — without a key they aren't offered when picking a receiver or handler.",
            SettingKind.String, true,
            t => t.Bitween.RebexLicenseKey,
            (t, v) => t.Bitween.RebexLicenseKey = v),

        // ——— Brand & theme ———
        new("Theme.PrimaryColor", "Brand & theme",
            "Primary color",
            "Re-brands the whole app's accent color — buttons, links, active nav, focus rings — instantly, without waiting on a deploy.",
            SettingKind.Color, false,
            t => t.Theme.PrimaryColor,
            (t, v) => t.Theme.PrimaryColor = v),

        new("Theme.CompanyName", "Brand & theme",
            "Company name",
            "Shown in the footer and used in a few page titles.",
            SettingKind.String, false,
            t => t.Theme.CompanyName,
            (t, v) => t.Theme.CompanyName = v),

        new("Theme.TabTitle", "Brand & theme",
            "Browser tab title",
            "What shows in the browser tab.",
            SettingKind.String, false,
            t => t.Theme.TabTitle,
            (t, v) => t.Theme.TabTitle = v),

        new("Theme.TabIcon", "Brand & theme",
            "Favicon URL",
            "The icon shown in the browser tab. Paste a URL to an .ico, .svg or .png.",
            SettingKind.String, false,
            t => t.Theme.TabIcon,
            (t, v) => t.Theme.TabIcon = v),

        new("Theme.LoginLogo", "Brand & theme",
            "Sign-in page logo",
            "The logo shown above the sign-in form.",
            SettingKind.String, false,
            t => t.Theme.LoginLogo,
            (t, v) => t.Theme.LoginLogo = v),

        new("Theme.BitweenLogo", "Brand & theme",
            "Sidebar logo",
            "The full logo shown at the top of the sidebar.",
            SettingKind.String, false,
            t => t.Theme.BitweenLogo,
            (t, v) => t.Theme.BitweenLogo = v),

        new("Theme.BitweenIcon", "Brand & theme",
            "Collapsed sidebar icon",
            "The compact icon shown when the sidebar is collapsed to icons only.",
            SettingKind.String, false,
            t => t.Theme.BitweenIcon,
            (t, v) => t.Theme.BitweenIcon = v),

        new("Theme.BitweenHeaderIcon", "Brand & theme",
            "Mobile header icon",
            "Icon variant used in the mobile top bar.",
            SettingKind.String, false,
            t => t.Theme.BitweenHeaderIcon,
            (t, v) => t.Theme.BitweenHeaderIcon = v),

        new("Theme.BitweenText", "Brand & theme",
            "Sign-in page blurb",
            "The marketing description shown beside the sign-in form.",
            SettingKind.String, false,
            t => t.Theme.BitweenText,
            (t, v) => t.Theme.BitweenText = v),

        new("Theme.ShowFooter", "Brand & theme",
            "Show the footer",
            "Turn this off to hide the footer everywhere — the copyright line and the website / LinkedIn / GitHub links below every page.",
            SettingKind.Boolean, false,
            t => Str(t.Theme.ShowFooter),
            (t, v) => t.Theme.ShowFooter = Bool(v)),

        new("Theme.LinkedinLink", "Brand & theme",
            "LinkedIn link",
            "Add this if you want a LinkedIn link in the footer — leave blank to hide it.",
            SettingKind.String, false,
            t => t.Theme.LinkedinLink,
            (t, v) => t.Theme.LinkedinLink = v),

        new("Theme.GithubLink", "Brand & theme",
            "GitHub link",
            "Add this if you want a GitHub link in the footer — leave blank to hide it.",
            SettingKind.String, false,
            t => t.Theme.GithubLink,
            (t, v) => t.Theme.GithubLink = v),

        new("Theme.WebsiteLink", "Brand & theme",
            "Website link",
            "Add this if you want a company website link in the footer — leave blank to hide it.",
            SettingKind.String, false,
            t => t.Theme.WebsiteLink,
            (t, v) => t.Theme.WebsiteLink = v),

        new("Theme.AllRightsReserved", "Brand & theme",
            "Copyright notice",
            "The copyright notice text shown in the footer.",
            SettingKind.String, false,
            t => t.Theme.AllRightsReserved,
            (t, v) => t.Theme.AllRightsReserved = v),

        new("Theme.CopyRightsIcon", "Brand & theme",
            "Copyright symbol",
            "The symbol shown before the copyright notice.",
            SettingKind.String, false,
            t => t.Theme.CopyRightsIcon,
            (t, v) => t.Theme.CopyRightsIcon = v)
    ];

    private static readonly Dictionary<string, SettingDefinition> ByKey =
        All.ToDictionary(d => d.Key, StringComparer.OrdinalIgnoreCase);

    public static SettingDefinition Find(string key) =>
        key is not null && ByKey.TryGetValue(key, out var definition) ? definition : null;

    private static string Str(bool value) => value ? "true" : "false";

    /// <summary>Anything but an explicit "true" is off — matches how the UI posts checkboxes.</summary>
    private static bool Bool(string value) => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    private static int Int(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new FormatException($"'{value}' is not a whole number.");

    private static int? NullableInt(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : Int(value);
}
