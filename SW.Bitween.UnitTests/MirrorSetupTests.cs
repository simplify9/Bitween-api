using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.Cli.Configuration;
using SW.Bitween.Services.Mirror;

namespace SW.Bitween.UnitTests;

/// <summary>
/// What each kind of prod's setup becomes on a staging that mirrors it: how flows behave kept,
/// addresses and secrets replaced, partners' and global data faked, and nothing left to run on its own.
/// </summary>
[TestClass]
public class MirrorSetupTests
{
    static readonly MirrorFaker Faker = new(Encoding.UTF8.GetBytes("a key only these tests use"));

    static Dictionary<string, List<Existing>> Prod(params (string Section, string Key, string File)[] items) =>
        items.GroupBy(i => i.Section).ToDictionary(g => g.Key,
            g => g.Select((i, n) => new Existing((n + 1).ToString(), i.Key, (JsonObject)JsonNode.Parse(i.File)!, new JsonObject())).ToList());

    static JsonObject Only(MirrorSetupCopy copy, string section) => copy.Sections[section].Single();

    [TestMethod]
    public void Data_sources_and_bus_gateways_arrive_switched_off_and_say_which_prod_has_on()
    {
        var copy = MirrorSetup.Copy(Prod(
            ("busGateways", "Carrier events", """{ "name": "Carrier events", "inactive": false, "endpointProperties": { "Url": "amqp://events.carrier.com" } }"""),
            ("busGateways", "Old feed", """{ "name": "Old feed", "inactive": true }"""),
            ("dataSources", "ERP", """{ "name": "ERP", "properties": { "Host": "erp.acme.com" } }""")), Faker, new HashSet<string>());

        Assert.IsTrue(copy.Sections["busGateways"].All(g => (bool)g["inactive"]!));
        Assert.IsTrue((bool)Only(copy, "dataSources")["inactive"]!);
        // A rehearsal's traffic goes through the ones prod has on, as prod's does; never through one prod has off.
        CollectionAssert.AreEquivalent(new[] { "busGateways/Carrier events", "dataSources/ERP" }, copy.SwitchedOff.ToList());
    }

    [TestMethod]
    public void A_subscription_keeps_how_it_behaves_and_arrives_paused()
    {
        var copy = MirrorSetup.Copy(Prod(("subscriptions", "Push labels", """
            {
              "name": "Push labels", "type": "Internal", "paused": false,
              "matchExpression": { "type": "one_of", "path": "$.country", "values": ["FR"] },
              "handler": { "adapter": "NativeHttpHandler", "properties": {
                "Url": "https://api.carrier.com/v2/labels", "Method": "POST", "Password": "__private__", "Token": "{{globals.carrier.token}}" } }
            }
            """)), Faker, new HashSet<string>());

        var s = Only(copy, "subscriptions");
        Assert.AreEqual(true, (bool)s["paused"]!);
        Assert.AreEqual("FR", (string)s["matchExpression"]!["values"]![0]!);
        var p = s["handler"]!["properties"]!;
        Assert.AreEqual("POST", (string)p["Method"]!);
        Assert.AreEqual("{{globals.carrier.token}}", (string)p["Token"]!);
        StringAssert.EndsWith((string)p["Url"]!, ".mirror.invalid/v2/labels");
        StringAssert.StartsWith((string)p["Password"]!, "mirror-");

        var address = copy.Addresses.Single();
        Assert.AreEqual("https", address.Kind);
        var use = address.UsedBy.Single();
        Assert.AreEqual(("subscriptions", "Push labels"), (use.Section, use.Key));
        StringAssert.Contains(use.Where, "Url");
    }

    [TestMethod]
    public void A_partner_gets_fakes_of_its_data_and_keys_of_its_own()
    {
        var copy = MirrorSetup.Copy(Prod(("partners", "Acme", """
            {
              "name": "Acme", "secretProperties": ["sftpPassword"], "loginIdentity": "acme-client-778",
              "properties": { "accountNumber": "AC-1001", "timeZone": "Europe/Paris", "sftpPassword": "__private__",
                              "apiKey": "live_9f8e7d", "webhookUrl": "https://hooks.acme.com/in" },
              "keys": { "main": "abcde...(hidden)" }
            }
            """)), Faker, new HashSet<string> { "partner:timeZone", "partner:apiKey", "partner:webhookUrl" });

        var a = Only(copy, "partners");
        var p = a["properties"]!;
        Assert.AreNotEqual("AC-1001", (string)p["accountNumber"]!);
        Assert.AreEqual("AC-1001".Length, ((string)p["accountNumber"]!).Length);
        // Kept, because it was asked for and it is a setting.
        Assert.AreEqual("Europe/Paris", (string)p["timeZone"]!);
        // Asked for, and never kept: a name a secret goes by, and an address.
        Assert.AreNotEqual("live_9f8e7d", (string)p["apiKey"]!);
        Assert.IsFalse(((string)p["webhookUrl"]!).Contains("acme.com"));
        StringAssert.StartsWith((string)p["sftpPassword"]!, "mirror-");
        Assert.AreNotEqual("acme-client-778", (string)a["loginIdentity"]!);
        StringAssert.StartsWith((string)a["keys"]!["main"]!, "mirror-");

        // Offered for keeping: data that could be a setting, never a secret, an address or a secret's name.
        CollectionAssert.AreEquivalent(new[] { "partner:accountNumber", "partner:timeZone" }, copy.FakedKeys.Select(k => k.Id).ToList());
        Assert.IsTrue(copy.FakedKeys.Single(k => k.Id == "partner:timeZone").Kept);
    }

    [TestMethod]
    public void Global_values_are_faked_and_data_sources_and_bus_gateways_arrive_switched_off()
    {
        var copy = MirrorSetup.Copy(Prod(
            ("globalValues", "spaces", """{ "id": "spaces", "name": "Spaces", "values": { "bucket": "traxis-prod", "endpoint": "https://fra1.digitaloceanspaces.com" }, "secretProperties": [] }"""),
            ("dataSources", "Warehouse", """{ "name": "Warehouse", "inactive": false, "properties": { "ConnectionString": "Host=db.traxis.app;Password=pw;Database=wh" } }"""),
            ("busGateways", "Orders in", """{ "name": "Orders in", "inactive": false, "endpointProperties": { "Host": "mq.traxis.app" } }""")),
            Faker, new HashSet<string>());

        var values = Only(copy, "globalValues")["values"]!;
        Assert.AreNotEqual("traxis-prod", (string)values["bucket"]!);
        Assert.IsFalse(((string)values["endpoint"]!).Contains("digitaloceanspaces"));

        var ds = Only(copy, "dataSources");
        Assert.AreEqual(true, (bool)ds["inactive"]!);
        Assert.IsFalse(((string)ds["properties"]!["ConnectionString"]!).Contains("traxis"));
        StringAssert.Contains((string)ds["properties"]!["ConnectionString"]!, "Database=wh");

        var bus = Only(copy, "busGateways");
        Assert.AreEqual(true, (bool)bus["inactive"]!);
        StringAssert.EndsWith((string)bus["endpointProperties"]!["Host"]!, ".mirror.invalid");
    }

    [TestMethod]
    public void Several_of_one_name_are_told_apart_oldest_first()
    {
        var ctx = new Context(null);
        var kind = Kinds.Of("subscriptions");
        var items = new List<(string Id, JsonObject Api)>
        {
            ("12", new JsonObject { ["name"] = "test" }),
            ("3", new JsonObject { ["name"] = "test" }),
            ("7", new JsonObject { ["name"] = "test (2)" }),
            ("9", new JsonObject { ["name"] = "Orders" }),
        };

        var notes = MirrorService.Distinguish(ctx, kind, items).ToList();

        // The oldest keeps the name; "test (2)" is taken already, so the next is "test (3)".
        Assert.AreEqual("test", (string)items[1].Api["name"]!);
        Assert.AreEqual("test (3)", (string)items[0].Api["name"]!);
        Assert.AreEqual("test (2)", (string)items[2].Api["name"]!);
        StringAssert.Contains(notes.Single(), "2 subscriptions named");
    }

    /// <summary>
    /// A changed or newer build sends fields this release doesn't know. The copy can't carry them, so
    /// they are named - only when set on some item, since a field always empty changes nothing.
    /// </summary>
    [TestMethod]
    public void Fields_prod_sends_that_this_release_does_not_know_are_named_not_dropped()
    {
        static Existing Item(string key, string api) => new("1", key, new JsonObject(), (JsonObject)JsonNode.Parse(api)!);
        var prod = new Context(null);
        prod.Current["partners"] =
        [
            Item("Acme", """{ "id": 1, "name": "Acme", "customerTier": "gold", "legacyCode": null, "archived": false }"""),
            Item("Zed", """{ "id": 2, "name": "Zed", "customerTier": "", "legacyCode": null, "archived": false }"""),
        ];
        var staging = new Context(null);
        staging.Current["partners"] = [Item("Acme", """{ "id": 9, "name": "Acme" }""")];

        var unknown = MirrorService.Unknown(prod, staging, new Dictionary<string, string>()).Single();

        Assert.AreEqual(("partners", "customerTier", 1), (unknown.Section, unknown.Field, unknown.Items));
        CollectionAssert.AreEqual(new[] { "Acme" }, unknown.Examples.ToList());
        // Nothing to compare with until staging has partners of its own.
        Assert.AreEqual(0, MirrorService.Unknown(prod, new Context(null), new Dictionary<string, string>()).Count);
    }

    [TestMethod]
    public void Nouns_read_as_plurals()
    {
        Assert.AreEqual("retry policies", MirrorService.Plural("retry policy"));
        Assert.AreEqual("subscription categories", MirrorService.Plural("subscription category"));
        Assert.AreEqual("bus gateways", MirrorService.Plural("bus gateway"));
        Assert.AreEqual("addresses", MirrorService.Plural("address"));
    }
}
