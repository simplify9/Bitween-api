using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.Services.Mirror;

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
}
