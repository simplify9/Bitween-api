using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.Services;

namespace SW.Bitween.UnitTests;

/// <summary>
/// The settings page renders whatever the catalog says, so what an administrator is offered — which
/// sections, which rows are edits and which are only shown — is decided here rather than in the UI.
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
                "Adapters",
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

        // It applies per request — the Login handler and the config endpoint both read it live — so
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
}
