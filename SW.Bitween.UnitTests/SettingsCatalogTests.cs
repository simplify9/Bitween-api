using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.Services;

namespace SW.Bitween.UnitTests;

/// <summary>
/// The settings page renders whatever the catalog says, so what an administrator is offered - which
/// sections, which rows are edits and which are only shown - is decided here rather than in the UI.
/// </summary>
[TestClass]
public class SettingsCatalogTests
{
    [TestMethod]
    public void Sections_are_the_ones_the_page_lists_in_the_order_it_lists_them()
    {
        // The page takes its section links from the rows, first appearance first.
        CollectionAssert.AreEqual(
            new[]
            {
                "Documents & storage",
                "API behavior",
                "Single sign-on (Microsoft)",
                "Single sign-on (OpenID Connect)",
                "Adapters",
                "Marketplace",
                "AI",
                "Mail server",
                "Reliability & jobs",
                "Messaging",
                "Database",
                "Security",
                "Brand & theme",
            },
            SettingsCatalog.All.Select(d => d.Section).Distinct().ToArray());
    }

    /// <summary>
    /// There is no restart-required row: a setting that couldn't take effect immediately is shown as
    /// an environment value instead of being offered as an edit that needs a restart to land.
    /// </summary>
    [TestMethod]
    public void Every_setting_is_either_applied_live_or_environment_owned()
    {
        foreach (var definition in SettingsCatalog.All)
        {
            // An editable setting takes effect by being written to the live options; an environment
            // one has nowhere to be written that would matter, since whoever reads it did so at boot.
            Assert.AreEqual(definition.Stored, definition.Write is not null,
                $"{definition.Key} is {definition.Access} but {(definition.Write is null ? "can't" : "can")} be written.");
            Assert.AreEqual(definition.Access == SettingAccess.Editable, definition.Stored, definition.Key);
        }
    }

    [TestMethod]
    public void Database_settings_are_shown_but_never_stored()
    {
        var database = SettingsCatalog.All.Where(d => d.Section == "Database").ToDictionary(d => d.Key);

        // The provider and its credentials are fixed when the connection is built at startup.
        Assert.AreEqual(SettingAccess.ReadOnly, database["Bitween.UseAzureManagedIdentity"].Access);
        Assert.AreEqual(SettingKind.Boolean, database["Bitween.UseAzureManagedIdentity"].Kind);
        // …and a client ID is reported only as set or not set, never by its content.
        Assert.AreEqual(SettingAccess.Presence, database["Bitween.AzureManagedIdentityClientId"].Access);
        Assert.IsFalse(database.Values.Any(d => d.Stored), "A database setting is offered as an edit.");
    }

    [TestMethod]
    public void Microsoft_only_sign_in_is_an_editable_setting_not_an_environment_value()
    {
        var definition = SettingsCatalog.Find("Bitween.DisableEmailPasswordLogin");

        // It applies per request - the Login handler and the config endpoint both read it live - so
        // it belongs in the catalog as an edit rather than a read-only environment row.
        Assert.AreEqual("Single sign-on (Microsoft)", definition.Section);
        Assert.AreEqual("Microsoft sign-in only", definition.Label);
        Assert.AreEqual(SettingKind.Boolean, definition.Kind);
        Assert.AreEqual(SettingAccess.Editable, definition.Access);
        Assert.IsFalse(definition.Secret);
        Assert.AreEqual("false", SettingsService.DefaultOf(definition));
    }

    /// <summary>
    /// Whether an invalid expression is refused is SettingsTests'
    /// <c>A_schedule_that_is_not_a_cron_expression_is_refused</c>; this is that it can be edited at all.
    /// </summary>
    [TestMethod]
    public void The_retry_schedule_is_editable_and_polls_every_minute_by_default()
    {
        var definition = SettingsCatalog.Find("Bitween.RetryJobCron");

        Assert.AreEqual("Reliability & jobs", definition.Section);
        Assert.AreEqual("Retry poll schedule", definition.Label);
        Assert.AreEqual(SettingKind.String, definition.Kind);
        Assert.AreEqual(SettingAccess.Editable, definition.Access);
        Assert.AreEqual("0 * * * * ?", SettingsService.DefaultOf(definition));
    }

    [TestMethod]
    public void The_partner_key_header_is_editable_and_stays_partnerkey_by_default()
    {
        var definition = SettingsCatalog.Find("Bitween.PartnerKeyHeader");

        // Read per request by the gateway and the legacy exchange endpoints, so it applies live.
        Assert.AreEqual("API behavior", definition.Section);
        Assert.AreEqual(SettingAccess.Editable, definition.Access);
        Assert.AreEqual("partnerkey", SettingsService.DefaultOf(definition));

        var target = new SettingsTarget(new BitweenOptions(), new ThemeOptions());
        definition.Write(target, " X-Api-Key ");
        Assert.AreEqual("X-Api-Key", target.Bitween.PartnerKeyHeader);
        // Already carries Bearer and Basic; a key sent in it would be read as one of those.
        Assert.ThrowsException<System.FormatException>(() => definition.Write(target, "Authorization"));
    }

    /// <summary>
    /// The mail server is set once for every Email channel. It is read on each send, so it is an
    /// edit like any other, and its password is stored the way the Rebex key is: encrypted.
    /// </summary>
    [TestMethod]
    public void The_mail_server_is_editable_and_checks_what_it_is_given()
    {
        var mail = SettingsCatalog.All.Where(d => d.Section == "Mail server").ToDictionary(d => d.Key);

        Assert.IsTrue(mail.Values.All(d => d.Access == SettingAccess.Editable));
        CollectionAssert.AreEqual(new[] { "Bitween.MailServer.Password" },
            mail.Values.Where(d => d.Secret).Select(d => d.Key).ToArray());
        Assert.AreEqual("587", SettingsService.DefaultOf(mail["Bitween.MailServer.Port"]));
        Assert.AreEqual("true", SettingsService.DefaultOf(mail["Bitween.MailServer.UseTls"]));

        var target = new SettingsTarget(new BitweenOptions(), new ThemeOptions());
        Assert.ThrowsException<System.FormatException>(() => mail["Bitween.MailServer.Port"].Write(target, "70000"));
        Assert.ThrowsException<System.FormatException>(() =>
            mail["Bitween.MailServer.From"].Write(target, "Bitween <bitween@example.com>"));

        mail["Bitween.MailServer.From"].Write(target, " bitween@example.com ");
        Assert.AreEqual("bitween@example.com", target.Bitween.MailServer.From);
        // Empty clears it, which is how an administrator stops Email channels sending.
        mail["Bitween.MailServer.Host"].Write(target, "");
        Assert.IsNull(target.Bitween.MailServer.Host);
    }

    /// <summary>
    /// Every AI feature reads the provider per call, so all of it is an edit; only the key is secret,
    /// and the provider is a choice rather than free text.
    /// </summary>
    [TestMethod]
    public void The_ai_provider_is_editable_and_checks_what_it_is_given()
    {
        var ai = SettingsCatalog.All.Where(d => d.Section == "AI").ToDictionary(d => d.Key);

        Assert.IsTrue(ai.Values.All(d => d.Access == SettingAccess.Editable));
        CollectionAssert.AreEqual(new[] { "Bitween.Ai.ApiKey" }, ai.Values.Where(d => d.Secret).Select(d => d.Key).ToArray());
        CollectionAssert.AreEqual(new[] { "Anthropic", "OpenAI" }, ai["Bitween.Ai.Provider"].Choices.ToArray());
        Assert.AreEqual("Anthropic", SettingsService.DefaultOf(ai["Bitween.Ai.Provider"]));

        var target = new SettingsTarget(new BitweenOptions(), new ThemeOptions());
        Assert.IsFalse(target.Bitween.Ai.IsSet, "no key, no AI");
        ai["Bitween.Ai.ApiKey"].Write(target, " sk-test ");
        Assert.IsTrue(target.Bitween.Ai.IsSet);
        Assert.AreEqual("claude-opus-5-5", target.Bitween.Ai.ResolvedModel);

        // Spelled the way the list spells it, whatever case it arrived in.
        ai["Bitween.Ai.Provider"].Write(target, "openai");
        Assert.AreEqual("OpenAI", target.Bitween.Ai.Provider);
        Assert.IsFalse(target.Bitween.Ai.IsSet, "OpenAI has no default model");
        Assert.ThrowsException<System.FormatException>(() => ai["Bitween.Ai.Provider"].Write(target, "Gemini"));

        Assert.ThrowsException<System.FormatException>(() => ai["Bitween.Ai.BaseUrl"].Write(target, "localhost:11434"));
        // Plain http would send the key in the clear anywhere but this machine.
        Assert.ThrowsException<System.FormatException>(() => ai["Bitween.Ai.BaseUrl"].Write(target, "http://ollama.lan:11434/v1/"));
        ai["Bitween.Ai.BaseUrl"].Write(target, "http://localhost:11434/v1/");
        Assert.AreEqual("http://localhost:11434/v1/", target.Bitween.Ai.BaseUrl);

        // A misspelt provider from configuration is off, not sent down the OpenAI path.
        target.Bitween.Ai.Model = "some-model";
        target.Bitween.Ai.Provider = "Anthropc";
        Assert.IsFalse(target.Bitween.Ai.IsSet);
    }
}
