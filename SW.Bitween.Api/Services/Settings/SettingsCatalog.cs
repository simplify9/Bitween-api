using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using SW.Scheduler;

namespace SW.Bitween.Services;

public enum SettingKind
{
    String,
    Number,
    Boolean,
    Color
}

/// <summary>What the UI is allowed to do with a setting.</summary>
public enum SettingAccess
{
    /// <summary>Stored in the <c>Settings</c> table and changeable from the UI.</summary>
    Editable,

    /// <summary>
    /// Environment-owned: shown with its current value so an administrator can see what this
    /// instance is running on, but not changeable here because it's read once at startup.
    /// </summary>
    ReadOnly,

    /// <summary>
    /// Environment-owned and private: only whether a value is set is reported, never the value —
    /// for credentials and keys that would be pointless to leak into the browser.
    /// </summary>
    Presence
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
    Action<SettingsTarget, string> Write)
{
    /// <summary>Editable unless a definition says otherwise — see <see cref="SettingAccess"/>.</summary>
    public SettingAccess Access { get; init; } = SettingAccess.Editable;

    /// <summary>
    /// Extra work needed to make a new value take effect, where assigning the property isn't
    /// enough — rescheduling a job, re-declaring queues. Runs on the instance that saved it,
    /// after the value has been applied.
    /// </summary>
    public Func<IServiceProvider, Task> OnChange { get; init; }

    /// <summary>Only editable settings get a row; the rest are read straight off the options.</summary>
    public bool Stored => Access == SettingAccess.Editable;
}

/// <summary>
/// Every setting the settings page shows, and — for the editable ones — the only keys the
/// <c>Settings</c> table ever holds.
/// <para>
/// The membership rule is about <see cref="SettingAccess"/>, not about being listed here. A
/// setting is <b>editable</b> only if every consumer reads it from the options singleton per call,
/// so a change takes effect immediately. Anything captured once during
/// <c>Startup.ConfigureServices</c> — the bus, CORS, storage, JWT signing, the DB provider — is
/// environment-owned and appears as <see cref="SettingAccess.ReadOnly"/> (its value shown) or
/// <see cref="SettingAccess.Presence"/> (only whether it's set), so an administrator can see what
/// the instance is running on without being offered an edit that couldn't work.
/// </para>
/// <para>
/// Read-only and presence settings never get a row: they're read straight off the options object,
/// which configuration bound at startup.
/// </para>
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

        View("Bitween.DocumentPrefix", "Documents & storage", "Document prefix",
            "The cloud-storage key prefix every exchange document is written under. Fixed per environment — changing it would leave everything already stored unreachable.",
            SettingKind.String, t => t.Bitween.DocumentPrefix),

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

        View("Bitween.CorsOrigins", "API behavior", "Allowed browser origins",
            "The origins allowed to call this API with cookies attached. Read once when the CORS policy is built at startup.",
            SettingKind.String, t => Join(t.Bitween.CorsOrigins)),

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

        View("Bitween.AdapterPath", "Adapters", "Custom adapter path",
            "The cloud-storage key prefix custom adapter packages are downloaded from. Handed to the serverless runner when it's configured at startup.",
            SettingKind.String, t => t.Bitween.AdapterPath),

        View("Bitween.ServerlessCommandTimeout", "Adapters", "Custom adapter timeout (seconds)",
            "How long a custom adapter may run before the serverless runner gives up on it.",
            SettingKind.Number, t => t.Bitween.ServerlessCommandTimeout.ToString(CultureInfo.InvariantCulture)),

        // ——— Reliability & jobs ———
        new("Bitween.RetryJobCron", "Reliability & jobs",
            "Retry poll schedule",
            "How often Bitween looks for exchanges whose scheduled retry has come due, as a cron expression: second minute hour day-of-month month day-of-week. Saving re-schedules the job straight away.",
            SettingKind.String, false,
            t => t.Bitween.RetryJobCron,
            (t, v) => t.Bitween.RetryJobCron = Cron(v))
        {
            // Assigning the property isn't enough here: the trigger already lives in the Quartz
            // store, so it has to be replaced. Schedule() reads the value we just applied.
            OnChange = sp => sp.GetRequiredService<IScheduleRepository>()
                .Schedule<RetryJob>(sp.GetRequiredService<BitweenOptions>().RetryJobCron)
        },

        // ——— Messaging ———
        // All environment-owned: the bus connection and its queue topology are built once, during
        // startup, so nothing here could take effect on a running instance.
        View("Bitween.QueuePrefix", "Messaging", "Queue name prefix",
            "Prefixed to every queue this instance declares, which is what keeps two Bitween deployments on one RabbitMQ from consuming each other's messages.",
            SettingKind.String, t => t.Bitween.QueuePrefix),

        View("Bitween.BusDefaultQueuePrefetch", "Messaging", "Default queue prefetch",
            "How many messages a consumer may hold unacknowledged by default. A work group can override it for its own queue.",
            SettingKind.Number, t => t.Bitween.BusDefaultQueuePrefetch?.ToString(CultureInfo.InvariantCulture)),

        View("Bitween.ConsumeLegacyEventMessages", "Messaging", "Consume legacy event messages",
            "Whether this instance also drains the five queues named after exchange events, which an older Bitween published to before work groups existed. Nothing publishes to them today, so this is only for finishing off messages left behind by an upgrade.",
            SettingKind.Boolean, t => Str(t.Bitween.ConsumeLegacyEventMessages)),

        Presence("Bitween.RabbitMqManagementUrl", "Messaging", "RabbitMQ management URL",
            "The management API queue health is read from. All three management values are needed before queue depths can be shown.",
            t => t.Bitween.RabbitMqManagementUrl),

        Presence("Bitween.RabbitMqManagementUsername", "Messaging", "RabbitMQ management username",
            "The account queue health reads with. Required alongside the URL and password.",
            t => t.Bitween.RabbitMqManagementUsername),

        Presence("Bitween.RabbitMqManagementPassword", "Messaging", "RabbitMQ management password",
            "The password for the management account. Required alongside the URL and username.",
            t => t.Bitween.RabbitMqManagementPassword),

        // ——— Database ———
        View("Bitween.UseAzureManagedIdentity", "Database", "Use Azure managed identity",
            "Whether database connections authenticate with an Azure managed identity instead of a password in the connection string.",
            SettingKind.Boolean, t => Str(t.Bitween.UseAzureManagedIdentity)),

        Presence("Bitween.AzureManagedIdentityClientId", "Database", "Managed identity client ID",
            "Set only when a user-assigned identity is used; left unset, the system-assigned identity is.",
            t => t.Bitween.AzureManagedIdentityClientId),

        // ——— Security ———
        Presence("Bitween.SettingsEncryptionKey", "Security", "Settings encryption key",
            "Encrypts secret settings before they're stored. Without it, a secret can't be saved here at all and stays environment-only.",
            t => t.Bitween.SettingsEncryptionKey),

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

    /// <summary>
    /// An environment value the UI shows but can't change. No <c>Write</c>: there's nowhere to
    /// write it to that would matter, since whoever reads it did so at startup.
    /// </summary>
    private static SettingDefinition View(string key, string section, string label, string description,
        SettingKind kind, Func<SettingsTarget, string> read) =>
        new(key, section, label, description, kind, false, read, null) { Access = SettingAccess.ReadOnly };

    /// <summary>An environment value reported only as set or not set, never by its content.</summary>
    private static SettingDefinition Presence(string key, string section, string label, string description,
        Func<SettingsTarget, string> read) =>
        new(key, section, label, description, SettingKind.String, false, read, null)
            { Access = SettingAccess.Presence };

    private static string Str(bool value) => value ? "true" : "false";

    private static string Join(string[] values) =>
        values is null ? string.Empty : string.Join(", ", values);

    /// <summary>
    /// Rejected here rather than by the scheduler, because a bad expression that reached the table
    /// would throw on the next boot — inside the background service that seeds every subscription's
    /// schedule, taking all of them down with it.
    /// </summary>
    private static string Cron(string value) =>
        CronExpression.IsValidExpression(value)
            ? value
            : throw new FormatException($"'{value}' is not a valid cron expression.");

    /// <summary>Anything but an explicit "true" is off — matches how the UI posts checkboxes.</summary>
    private static bool Bool(string value) => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    private static int Int(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new FormatException($"'{value}' is not a whole number.");

    private static int? NullableInt(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : Int(value);
}
