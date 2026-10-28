using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.Cli.Configuration;
using SW.Bitween.Services.Mirror;
using SW.PrimitiveTypes;

namespace SW.Bitween.UnitTests;

/// <summary>
/// What bringing prod's picked items onto staging takes with them: what each names, and how prod's
/// traffic reaches a subscription, so it runs on staging as it does on prod.
/// </summary>
[TestClass]
public class MirrorBringTests
{
    static Dictionary<string, List<JsonObject>> Setup(params (string Section, string Json)[] items) =>
        items.GroupBy(i => i.Section).ToDictionary(g => g.Key, g => g.Select(i => (JsonObject)JsonNode.Parse(i.Json)!).ToList());

    static readonly Dictionary<string, List<JsonObject>> Prod = Setup(
        ("informationTypes", """{ "name": "Orders" }"""),
        ("partners", """{ "name": "Acme" }"""),
        ("partners", """{ "name": "Globex" }"""),
        ("globalValues", """{ "id": "carrier", "values": { "token": "x" } }"""),
        ("connections", """{ "name": "dhl" }"""),
        ("notificationChannels", """{ "name": "Ops mail" }"""),
        ("notificationChannels", """{ "name": "Retry alerts" }"""),
        ("retryPolicies", """{ "name": "Carrier", "alertChannel": "Retry alerts" }"""),
        ("dataSources", """{ "name": "Warehouse" }"""),
        ("dataSources", """{ "name": "Unused" }"""),
        ("dataSourceStatements", """{ "dataSource": "Warehouse", "name": "outbox" }"""),
        ("subscriptions", """
            {
              "name": "Order intake", "type": "GatewayApiCall", "informationType": "Orders", "retryPolicy": "Carrier",
              "responseSubscription": "Archive", "notifications": [{ "channel": "Ops mail", "onFailure": true }],
              "handler": { "adapter": "NativeHttpHandler", "properties": {
                "Token": "{{globals.carrier.token}}", "Auth": "Bearer {{connections.dhl.accessToken}}" } }
            }
            """),
        ("subscriptions", """{ "name": "Archive", "type": "Response" }"""),
        ("subscriptions", """{ "name": "Returns", "type": "GatewayApiCall" }"""),
        ("subscriptions", """{ "name": "Drain", "type": "Receiving", "dataSource": "Warehouse" }"""),
        ("apiGateways", """
            {
              "name": "Intake", "urlName": "intake",
              "partners": [{ "partner": "Acme", "subscription": "Order intake" }, { "partner": "Globex", "subscription": "Returns" }]
            }
            """),
        ("busGateways", """{ "name": "Orders out", "informationType": "Orders", "routes": [{ "subscription": "Archive" }] }"""));

    static List<string> Keys(Dictionary<(string Section, string Key), string> needs) =>
        needs.Keys.Select(n => $"{n.Section}/{n.Key}").Order().ToList();

    [TestMethod]
    public void A_subscription_brings_what_it_names_and_how_prods_traffic_reaches_it()
    {
        var needs = MirrorService.Needs(Prod, [("subscriptions", "Order intake")]);

        CollectionAssert.AreEquivalent(new[]
        {
            "subscriptions/Order intake", "informationTypes/Orders", "retryPolicies/Carrier", "notificationChannels/Retry alerts",
            "notificationChannels/Ops mail", "globalValues/carrier", "connections/dhl",
            // It hands its responses to Archive, which a bus gateway also routes to.
            "subscriptions/Archive", "busGateways/Orders out",
            // Acme calls it through the gateway; Globex calls another subscription there, which stays behind.
            "apiGateways/intake", "partners/Acme",
        }, Keys(needs));
        Assert.AreEqual("Picked.", needs[("subscriptions", "Order intake")]);
        Assert.AreEqual("It calls “Order intake” through the API gateway “intake”.", needs[("partners", "Acme")]);
        Assert.AreEqual("The subscription “Order intake” hands its responses to it.", needs[("subscriptions", "Archive")]);
        Assert.AreEqual("The subscription “Order intake” reads its values.", needs[("globalValues", "carrier")]);
    }

    [TestMethod]
    public void A_picked_gateway_brings_everything_it_leads_to()
    {
        var needs = MirrorService.Needs(Prod, [("apiGateways", "intake")]);

        CollectionAssert.IsSubsetOf(new[] { "subscriptions/Order intake", "subscriptions/Returns", "partners/Acme", "partners/Globex" }, Keys(needs));
        Assert.AreEqual("The API gateway “intake” leads to it.", needs[("subscriptions", "Returns")]);
    }

    [TestMethod]
    public void A_subscription_on_a_data_source_brings_its_sql_statements_and_nothing_else_of_prods()
    {
        var needs = MirrorService.Needs(Prod, [("subscriptions", "Drain")]);

        CollectionAssert.AreEquivalent(new[] { "subscriptions/Drain", "dataSources/Warehouse", "dataSourceStatements/Warehouse/outbox" }, Keys(needs));
    }

    [TestMethod]
    public void Something_prod_no_longer_has_is_left_out()
    {
        var needs = MirrorService.Needs(Prod, [("subscriptions", "Gone")]);

        Assert.AreEqual(0, needs.Count);
    }

    [TestMethod]
    public void Says_in_one_line_whose_data_comes_faked_however_many()
    {
        var index = new Dictionary<(string, string), JsonObject>();
        foreach (var name in new[] { "P6", "P2", "P5", "P1", "P4", "P3" }) index[("partners", name)] = new JsonObject { ["properties"] = new JsonObject { ["a"] = "1" } };
        index[("partners", "Bare")] = new JsonObject();
        index[("globalValues", "carrier")] = new JsonObject { ["values"] = new JsonObject { ["token"] = "x" } };

        Assert.AreEqual("Faked in the same shape as prod's: the properties of “P1”, “P2”, “P3” and 3 more; the global values “carrier”.",
            MirrorService.FakedNote(index, index.Keys).Text);
        Assert.AreEqual("Faked in the same shape as prod's: the properties of “P1” and “P2”.",
            MirrorService.FakedNote(index, [("partners", "P2"), ("partners", "P1"), ("partners", "Bare")]).Text);
        Assert.IsNull(MirrorService.FakedNote(index, [("partners", "Bare")]));
    }

    // ------------------------------------------------------------------ secrets saved as placeholders

    static readonly MirrorFaker Faker = new(Encoding.UTF8.GetBytes("a key only these tests use"));

    /// <summary>Prod's "Agent tracing", whose adapter can't be saved without its Password; its ApiKey it can.</summary>
    static MirrorSetupCopy Tracing() => MirrorSetup.Copy(new Dictionary<string, List<Existing>>
    {
        ["subscriptions"] = [new Existing("1", "Agent tracing", (JsonObject)JsonNode.Parse("""
            {
              "name": "Agent tracing", "type": "Receiving",
              "receiver": { "adapter": "infolink5.receivers.cassini", "properties": {
                "Url": "https://cassini.example.com", "Password": "__private__", "ApiKey": "__private__" } }
            }
            """)!, new JsonObject())],
    }, Faker, new HashSet<string>());

    const string Password = """["subscriptions","Agent tracing","receiver","properties","Password"]""";

    /// <summary>Bitween's check: what saving would refuse for, of Url and Password.</summary>
    static Task<IReadOnlyCollection<string>> Requires(string adapter, ICollection<KeyAndValue> provided) =>
        Task.FromResult<IReadOnlyCollection<string>>(new[] { "Url", "Password" }
            .Where(n => string.IsNullOrEmpty(provided.FirstOrDefault(p => p.Key == n)?.Value)).ToList());

    [TestMethod]
    public async Task A_secret_its_adapter_cannot_be_saved_without_is_saved_as_a_placeholder()
    {
        var copy = Tracing();
        var item = copy.Sections["subscriptions"].Single();
        var creating = new Change(Op.Create, "subscriptions", "Agent tracing", item);

        var stoodIn = await MirrorService.StandInAsync([creating], copy.Blanks, Requires);

        var properties = item["receiver"]!["properties"]!;
        Assert.AreEqual("set-me", (string)properties["Password"]!);
        Assert.AreEqual("", (string)properties["ApiKey"]!); // it can do without: left empty, as before
        Assert.AreEqual("https://set-me.invalid", (string)properties["Url"]!);
        CollectionAssert.AreEqual(new[] { Password }, stoodIn.Select(s => s.Id).ToList());
        Assert.AreSame(creating, stoodIn.Single().Change);
    }

    [TestMethod]
    public async Task Only_a_subscription_being_made_whose_adapter_can_be_described_gets_one()
    {
        var copy = Tracing();
        var item = copy.Sections["subscriptions"].Single();

        // Staging's own is never touched; one whose adapter this bring installs is left for saving to refuse.
        Assert.AreEqual(0, (await MirrorService.StandInAsync([new Change(Op.Update, "subscriptions", "Agent tracing", item)], copy.Blanks, Requires)).Count);
        Assert.AreEqual(0, (await MirrorService.StandInAsync([new Change(Op.Create, "subscriptions", "Agent tracing", item)], copy.Blanks,
            (_, _) => throw new InvalidOperationException("not installed"))).Count);
        Assert.AreEqual("", (string)item["receiver"]!["properties"]!["Password"]!);
    }

    [TestMethod]
    public void A_secret_saved_as_a_placeholder_stays_to_do_until_a_person_says_it_is_filled_in()
    {
        var copy = Tracing();
        var staging = new Context(null);
        staging.Current["subscriptions"] = [new Existing("7", "Agent tracing", (JsonObject)JsonNode.Parse("""
            { "name": "Agent tracing", "receiver": { "adapter": "infolink5.receivers.cassini", "properties": {
                "Url": "https://test.cassini.example.com", "Password": "__private__", "ApiKey": "" } } }
            """)!, new JsonObject())];
        var open = MirrorService.TakeStagingsOwn(copy, staging, new HashSet<string> { Password });

        // Hidden like a filled one, so it reads as no difference; but it still stops tests until ticked.
        Assert.AreEqual("__private__", (string)copy.Sections["subscriptions"].Single()["receiver"]!["properties"]!["Password"]!);
        CollectionAssert.AreEquivalent(new[] { "receiver · Password True", "receiver · ApiKey False" },
            open.Select(b => $"{b.Field} {b.StoodIn}").ToList());

        // Ticked: only the empty ApiKey is left.
        CollectionAssert.AreEqual(new[] { "receiver · ApiKey" },
            MirrorService.TakeStagingsOwn(Tracing(), staging, new HashSet<string>()).Select(b => b.Field).ToList());
    }

    [TestMethod]
    public async Task Adapters_go_in_before_the_placeholders_are_worked_out_and_only_whats_made_keeps_one()
    {
        var install = new Change(Op.Install, Kinds.Adapters, "infolink5.receivers.cassini");
        var made = new Change(Op.Create, "subscriptions", "Agent tracing");
        var refused = new Change(Op.Create, "subscriptions", "Agent export");
        var steps = new List<string>();

        var (failed, saved) = await MirrorService.ApplyBringingAsync([install, made, refused],
            part =>
            {
                steps.Add("apply " + string.Join(", ", part.Select(c => c.Key)));
                return Task.FromResult(part.Contains(refused) ? new List<(Change Change, string Error)> { (refused, "Missing: Token") } : []);
            },
            all =>
            {
                steps.Add("placeholders");
                return Task.FromResult(new List<(Change Change, string Id)> { (made, "made"), (refused, "refused") });
            });

        CollectionAssert.AreEqual(new[] { "apply infolink5.receivers.cassini", "placeholders", "apply Agent tracing, Agent export" }, steps);
        Assert.AreSame(refused, failed.Single().Change);
        CollectionAssert.AreEqual(new[] { "made" }, saved);
    }

    [TestMethod]
    public void Filled_in_closes_that_items_placeholders_and_no_other_whatever_its_name()
    {
        static string Id(string key, string field) => MirrorService.StandInId(new MirrorBlank("subscriptions", key, ["handler", "properties", field], field, "secret"));
        var standIns = new HashSet<string> { Id("A", "Password"), Id("A", "Token"), Id("A/B", "Password"), Id("A/handler", "Password") };

        Assert.AreEqual(2, MirrorService.CloseStandIns(standIns, "subscriptions", "A"));

        CollectionAssert.AreEquivalent(new[] { Id("A/B", "Password"), Id("A/handler", "Password") }, standIns.ToList());
    }

    [TestMethod]
    public void The_preview_shows_each_setting_that_comes_as_it_is_with_its_value()
    {
        var copy = MirrorSetup.Copy(new Dictionary<string, List<Existing>>
        {
            ["subscriptions"] =
            [
                new Existing("1", "Agent tracing", (JsonObject)JsonNode.Parse($$$"""
                    {
                      "name": "Agent tracing", "type": "Receiving",
                      "receiver": { "adapter": "infolink5.receivers.ftp", "properties": {
                        "Host": "sftp.acmecargo.net", "Protocol": "sftp", "Port": "22", "CheckFileExistence": "false",
                        "Token": "{{globals.carrier.token}}", "TargetPath": "/acme/prod/out/event", "Rules": "{{{new string('x', 60)}}}" } }
                    }
                    """)!, new JsonObject()),
                new Existing("2", "Already there", (JsonObject)JsonNode.Parse("""
                    { "name": "Already there", "handler": { "adapter": "x", "properties": { "Agent": "SODEXI" } } }
                    """)!, new JsonObject()),
            ],
        }, Faker, new HashSet<string>());

        var notes = MirrorService.AsIsNotes(copy, [("subscriptions", "Agent tracing")]).ToList();

        // Yes or no, a short number, a token alone and what is left for staging's own aren't listed; nor is what isn't made.
        CollectionAssert.AreEqual(new[] { "receiver · Protocol", "receiver · TargetPath", "receiver · Rules" }, notes.Select(n => n.Field).ToList());
        Assert.AreEqual("sftp", notes[0].Text);
        Assert.AreEqual(new string('x', 32) + "… (60 characters)", notes[2].Text);
        Assert.IsTrue(notes.All(n => n.Group == "asIs" && n.Noun == "subscription"));
    }
}
