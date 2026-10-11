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
}
