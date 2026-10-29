using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using SW.Bitween.Cli.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SW.Bitween.UnitTests;

/// <summary>
/// The pieces of bitween apply that don't need a Bitween: reading and writing YAML so a written file
/// reads back the same, substituting secrets and files, and the rules for when two values are the same.
/// </summary>
[TestClass]
public class ConfigurationFileTests
{
    readonly string folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "bitween-config-tests", Guid.NewGuid().ToString("N"))).FullName;

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(folder, true); } catch { }
    }

    [TestMethod]
    public void Yaml_written_reads_back_as_the_same_values()
    {
        var original = new JsonObject
        {
            ["name"] = "Orders: inbound # one",
            ["count"] = 3,
            ["ratio"] = 0.5,
            ["on"] = true,
            ["code"] = "007",
            ["looksTrue"] = "true",
            ["empty"] = "",
            ["nothing"] = null,
            ["sql"] = "select *\nfrom orders\nwhere id > :cursor\n",
            ["dash"] = "- not a list",
            ["nested"] = new JsonObject { ["list"] = new JsonArray(1, "two", new JsonObject { ["a"] = "b", ["c"] = new JsonArray("d") }) },
            ["lists"] = new JsonArray(new JsonArray("x", "y"), new JsonArray()),
            ["needs quotes"] = "${secret:X}",
        };

        var back = Yaml.Read(Yaml.Write(original)).Single();

        Assert.AreEqual(original.ToJsonString(), back!.ToJsonString());
    }

    [TestMethod]
    public void Secrets_variables_and_files_are_substituted_and_secrets_remembered()
    {
        File.WriteAllText(Path.Combine(folder, "schema.json"), "{ \"type\": \"object\" }");
        var secrets = Directory.CreateDirectory(Path.Combine(folder, "secrets")).FullName;
        File.WriteAllText(Path.Combine(secrets, "FROM_FOLDER"), "folder-secret\n");
        Environment.SetEnvironmentVariable("CONFIG_TEST_SECRET", "env-secret");
        Environment.SetEnvironmentVariable("CONFIG_TEST_REGION", "eu");
        try
        {
            File.WriteAllText(Path.Combine(folder, "a.yaml"), """
                apiVersion: bitween/v1
                kind: Configuration
                metadata:
                  name: test
                informationTypes:
                  - name: Order
                    validationSchema: ${file:schema.json}
                """);
            File.WriteAllText(Path.Combine(folder, "b.yml"), """
                apiVersion: bitween/v1
                kind: Configuration
                partners:
                  - name: Acme ${env:CONFIG_TEST_REGION}
                    keys:
                      main: ${secret:CONFIG_TEST_SECRET}
                      other: ${secret:FROM_FOLDER}
                """);

            var config = ConfigFile.Load(folder, secrets);

            Assert.AreEqual("test", config.Name);
            Assert.AreEqual("{ \"type\": \"object\" }", (string)config.Sections["informationTypes"][0]["validationSchema"]!);
            var partner = config.Sections["partners"][0];
            Assert.AreEqual("Acme eu", (string)partner["name"]!);
            Assert.AreEqual("env-secret", (string)partner["keys"]!["main"]!);
            Assert.AreEqual("folder-secret", (string)partner["keys"]!["other"]!);
            CollectionAssert.AreEqual(new[] { "env-secret", "folder-secret" }, config.SecretValues.Order().ToArray());
        }
        finally
        {
            Environment.SetEnvironmentVariable("CONFIG_TEST_SECRET", null);
            Environment.SetEnvironmentVariable("CONFIG_TEST_REGION", null);
        }
    }

    [TestMethod]
    public void A_file_with_missing_secrets_or_unknown_sections_is_refused_naming_each()
    {
        var file = Path.Combine(folder, "c.yaml");
        File.WriteAllText(file, """
            apiVersion: bitween/v1
            kind: Configuration
            metadata: { name: x }
            partners:
              - name: A
                keys: { main: "${secret:NOT_SET_ONE}", other: "${secret:NOT_SET_TWO}" }
            """);
        var missing = Assert.ThrowsException<ConfigException>(() => ConfigFile.Load(file));
        StringAssert.Contains(missing.Message, "NOT_SET_ONE, NOT_SET_TWO");

        File.WriteAllText(file, "apiVersion: bitween/v1\nkind: Configuration\nmetadata: { name: x }\nwidgets: []\n");
        StringAssert.Contains(Assert.ThrowsException<ConfigException>(() => ConfigFile.Load(file)).Message, "no section widgets");

        File.WriteAllText(file, "apiVersion: bitween/v2\nkind: Configuration\n");
        StringAssert.Contains(Assert.ThrowsException<ConfigException>(() => ConfigFile.Load(file)).Message, "apiVersion must be bitween/v1");
    }

    [TestMethod]
    public void Values_compare_by_what_the_file_says()
    {
        // Only what the file sets is compared; Bitween's defaults for the rest don't count.
        Assert.IsTrue(Values.Same(JsonNode.Parse("""{ "a": 1 }"""), JsonNode.Parse("""{ "a": 1, "b": 2 }""")));
        // ...except a mapping that replaces every key, which has to name them all.
        Assert.IsFalse(Values.Same(JsonNode.Parse("""{ "a": "1" }"""), JsonNode.Parse("""{ "a": "1", "b": "2" }"""), exactKeys: true));
        // A secret Bitween won't show matches anything.
        Assert.IsTrue(Values.Same(JsonValue.Create("s3cret"), JsonValue.Create(Values.Masked)));
        // Empty is empty, however it's said.
        Assert.IsTrue(Values.Same(null, new JsonArray()));
        Assert.IsTrue(Values.Same(JsonValue.Create(""), null));
        // A number is a number, however it's written.
        Assert.IsTrue(Values.Same(JsonValue.Create(3), JsonValue.Create("3")));
        Assert.IsTrue(Values.Same(JsonValue.Create(1.0), JsonValue.Create(1)));
        Assert.IsFalse(Values.Same(JsonValue.Create("a"), JsonValue.Create("A")));
        Assert.IsFalse(Values.Same(JsonNode.Parse("[1, 2]"), JsonNode.Parse("[2, 1]")));
    }

    [TestMethod]
    public void Secret_names_are_made_from_what_they_belong_to()
    {
        Assert.AreEqual("PARTNER_ACME_LTD_KEY_MAIN", Exporter.Name(["partner", "Acme Ltd.", "key", "main"]));
        Assert.AreEqual("SUBSCRIPTION_ORDERS_TO_WAREHOUSE_HANDLER_APIKEY", Exporter.Name(["subscription", "Orders to warehouse!", "handler", "ApiKey"]));
    }

    [TestMethod]
    public void An_aggregation_with_an_information_type_is_turned_away_before_anything_is_made()
    {
        var aggregation = new JsonObject { ["name"] = "Hourly manifest", ["type"] = "Aggregation", ["aggregationFor"] = "Returns", ["informationType"] = "Customer return" };
        var problems = new Subscriptions().Unresolved(aggregation, (_, _) => true).ToList();
        Assert.IsTrue(problems.Any(p => p.Contains("an aggregation has none of its own")), string.Join("; ", problems));

        aggregation.Remove("informationType");
        Assert.AreEqual(0, new Subscriptions().Unresolved(aggregation, (_, _) => true).Count());
        // Any other kind keeps its information type.
        var receiving = new JsonObject { ["name"] = "Pickup", ["type"] = "Receiving", ["informationType"] = "Customer return" };
        Assert.AreEqual(0, new Subscriptions().Unresolved(receiving, (_, _) => true).Count());
    }

    [TestMethod]
    public void A_retry_budget_without_a_shared_total_is_turned_away_since_it_would_never_retry()
    {
        JsonObject Policy(JsonObject budget) => new()
        {
            ["name"] = "Partner outage",
            ["groups"] = new JsonArray(new JsonObject { ["name"] = "down or slow", ["appliesTo"] = new JsonArray("Error"), ["budget"] = budget }),
        };
        var missing = new RetryPolicies().Unresolved(Policy(new JsonObject { ["maxAttemptsPerError"] = 6 }), (_, _) => true).ToList();
        Assert.IsTrue(missing.Any(p => p.Contains("no retries at all")), string.Join("; ", missing));

        Assert.AreEqual(0, new RetryPolicies().Unresolved(Policy(new JsonObject { ["maxAttemptsPerError"] = 6, ["maxAttemptsTotal"] = 500 }), (_, _) => true).Count());
        // Null is no ceiling, and 0 written out is taken as meant.
        Assert.AreEqual(0, new RetryPolicies().Unresolved(Policy(new JsonObject { ["maxAttemptsPerError"] = 6, ["maxAttemptsTotal"] = null }), (_, _) => true).Count());
        Assert.AreEqual(0, new RetryPolicies().Unresolved(Policy(new JsonObject { ["maxAttemptsPerError"] = 6, ["maxAttemptsTotal"] = 0 }), (_, _) => true).Count());
    }

    /// <summary>
    /// Bitween answers expectTraffic with every member, nulls included. Exported, it names only what
    /// is set; compared, a member either side leaves unset is unset on both, and one set on only one
    /// side is a change, so a plan neither repeats itself nor misses a removed time zone.
    /// </summary>
    [TestMethod]
    public void Expected_traffic_compares_by_what_is_set()
    {
        static JsonObject Api(string zone) => new()
        {
            ["name"] = "Orders", ["type"] = "GatewayApiCall", ["pausedOn"] = null,
            ["expectTraffic"] = new JsonObject
            {
                ["everyMinutes"] = 15, ["days"] = null, ["from"] = null, ["to"] = null, ["timeZone"] = zone,
            },
        };
        var ctx = new Context(null);
        var kind = new Subscriptions();
        var have = kind.ToExisting(ctx, "1", Api(null));

        Assert.AreEqual("""{"everyMinutes":15}""", have.File["expectTraffic"]!.ToJsonString());

        JsonObject Want(JsonNode expect) => new() { ["name"] = "Orders", ["expectTraffic"] = expect };
        Assert.IsFalse(kind.Differences(ctx, Want(new JsonObject { ["everyMinutes"] = 15 }), have).Contains("expectTraffic"));
        Assert.IsFalse(kind.Differences(ctx, Want(new JsonObject { ["everyMinutes"] = "15", ["days"] = new JsonArray() }), have)
            .Contains("expectTraffic"));
        Assert.IsTrue(kind.Differences(ctx, Want(new JsonObject { ["everyMinutes"] = 30 }), have).Contains("expectTraffic"));
        Assert.IsTrue(kind.Differences(ctx, Want(null), have).Contains("expectTraffic"));

        var zoned = kind.ToExisting(ctx, "1", Api("Asia/Riyadh"));
        Assert.IsTrue(kind.Differences(ctx, Want(new JsonObject { ["everyMinutes"] = 15 }), zoned).Contains("expectTraffic"));

        var none = kind.ToExisting(ctx, "1", new JsonObject { ["name"] = "Orders", ["expectTraffic"] = null });
        Assert.IsFalse(kind.Differences(ctx, Want(null), none).Contains("expectTraffic"));
    }

    /// <summary>
    /// Channels go by name in the file and by id in Bitween: a policy's alert, a group's, an inline
    /// group's, and a subscription's notification rows, which compare as a set with a tick left out off.
    /// </summary>
    [TestMethod]
    public void Channels_go_by_name_in_the_file_and_by_id_in_Bitween()
    {
        var ctx = new Context(null);
        ctx.Know("notificationChannels", "Ops email", "5");
        ctx.Know("notificationChannels", "On-call Teams", "6");

        var policies = new RetryPolicies();
        var policy = policies.ToExisting(ctx, "1", new JsonObject
        {
            ["name"] = "Partner outage", ["alertChannelId"] = 5,
            ["groups"] = new JsonArray(new JsonObject
            {
                ["id"] = "6f1d2c1e-0000-0000-0000-000000000001", ["name"] = "down", ["alertMode"] = "Send", ["alertChannelId"] = 6,
                ["alertHandlerId"] = null, ["alertHandlerProperties"] = null,
            }),
        });
        Assert.AreEqual("Ops email", (string)policy.File["alertChannel"]);
        Assert.AreEqual("""{"name":"down","alertMode":"Send","alertChannel":"On-call Teams"}""", policy.File["groups"]![0]!.ToJsonString());

        var sent = policies.ToApi(ctx, policy.File);
        Assert.AreEqual(5, (int)sent["alertChannelId"]!);
        Assert.AreEqual("""{"name":"down","alertMode":"Send","alertChannelId":6}""", sent["groups"]![0]!.ToJsonString());

        var subscriptions = new Subscriptions();
        var subscription = subscriptions.ToExisting(ctx, "3", new JsonObject
        {
            ["name"] = "Orders", ["type"] = "GatewayApiCall", ["pausedOn"] = null,
            ["notifications"] = new JsonArray(new JsonObject { ["channelId"] = 5, ["onFailure"] = true, ["onBadResult"] = false, ["onSuccess"] = false, ["onQuiet"] = true }),
            ["customRetryPolicy"] = new JsonObject { ["groups"] = new JsonArray(new JsonObject { ["name"] = "down", ["alertChannelId"] = 6 }) },
        });
        Assert.AreEqual("""[{"channel":"Ops email","onFailure":true,"onQuiet":true}]""", subscription.File["notifications"]!.ToJsonString());
        Assert.AreEqual("On-call Teams", (string)subscription.File["customRetryPolicy"]!["groups"]![0]!["alertChannel"]);

        JsonObject Want(params JsonObject[] rows) => new() { ["name"] = "Orders", ["notifications"] = new JsonArray(rows) };
        // The same row with a tick written out as off is the same; a tick fewer, or no rows, is not.
        Assert.IsFalse(subscriptions.Differences(ctx, Want(new JsonObject { ["channel"] = "Ops email", ["onQuiet"] = true, ["onFailure"] = true, ["onSuccess"] = false }), subscription)
            .Contains("notifications"));
        Assert.IsTrue(subscriptions.Differences(ctx, Want(new JsonObject { ["channel"] = "Ops email", ["onFailure"] = true }), subscription).Contains("notifications"));
        Assert.IsTrue(subscriptions.Differences(ctx, Want(), subscription).Contains("notifications"));

        var api = subscriptions.ToApi(ctx, Want(new JsonObject { ["channel"] = "On-call Teams", ["onQuiet"] = true }));
        Assert.AreEqual("""[{"channelId":6,"onFailure":false,"onBadResult":false,"onSuccess":false,"onQuiet":true}]""", api["notifications"]!.ToJsonString());
        // Left out, Bitween keeps the rows it has.
        Assert.IsFalse(subscriptions.ToApi(ctx, new JsonObject { ["name"] = "Orders" }).ContainsKey("notifications"));
    }

    [TestMethod]
    public void An_alert_sent_the_retired_way_or_to_no_such_channel_is_turned_away()
    {
        static bool Known(string section, string key) => section == "notificationChannels" && key == "Ops email";
        static JsonObject Group(string channel = null, string handler = null) =>
            new() { ["name"] = "down", ["alertChannel"] = channel, ["alertHandlerId"] = handler, ["alertHandlerProperties"] = null };
        static JsonObject Policy(string channel, JsonObject group) => new() { ["name"] = "Partner outage", ["alertChannel"] = channel, ["groups"] = new JsonArray(group) };
        var kind = new RetryPolicies();

        var old = new JsonObject { ["name"] = "Partner outage", ["alertHandler"] = "NativeSmtpHandler" };
        Assert.IsTrue(kind.Unresolved(old, Known).Any(p => p.Contains("alertHandler is retired")));
        Assert.IsTrue(kind.Unresolved(Policy(null, Group(handler: "NativeSmtpHandler")), Known).Any(p => p.Contains("alertHandlerId, which is retired")));

        // An older export wrote the retired fields as null, which says nothing.
        Assert.AreEqual(0, kind.Unresolved(Policy("Ops email", Group("Ops email")), Known).Count());
        Assert.AreEqual(2, kind.Unresolved(Policy("Pager", Group("Pager")), Known).Count(p => p.Contains("\"Pager\"")));

        var subscription = new JsonObject
        {
            ["name"] = "Orders",
            ["notifications"] = new JsonArray(new JsonObject { ["channel"] = "Pager", ["onFailure"] = true }, new JsonObject { ["onFailure"] = true }),
            ["customRetryPolicy"] = new JsonObject { ["groups"] = new JsonArray(Group("Pager")) },
        };
        var problems = new Subscriptions().Unresolved(subscription, Known).ToList();
        CollectionAssert.AreEquivalent(new[]
        {
            "notifications: there's no notification channel \"Pager\"",
            "notifications: a row names no channel",
            "customRetryPolicy: \"down\" names alertChannel \"Pager\", and there's no notification channel by that name",
        }, problems);
    }
}
