using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.Cli.Configuration;

namespace SW.Bitween.UnitTests;

/// <summary>
/// Kits without a Bitween: kit.yaml read and checked, parameters against what the bundle reads, and the
/// configuration a kit installs: its connectors first, under the installation's name.
/// </summary>
[TestClass]
public class KitTests
{
    readonly string folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "bitween-kit-tests", Guid.NewGuid().ToString("N"))).FullName;

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(folder, true); } catch { }
    }

    const string KitYaml = """
        apiVersion: bitween/v1
        kind: Kit
        metadata: { id: s9.test.contacts, version: 1.2.0, displayName: Test contacts, summary: For tests. }
        connectors:
          - { id: s9.openapi, version: 1.0.0 }
        parameters:
          - { name: Label, description: Names, default: Test }
          - { name: Url, description: Where to send }
          - { name: Key, description: The key, secret: true }
          - { name: Optional, description: Maybe, required: false }
        """;

    const string Bundle = """
        apiVersion: bitween/v1
        kind: Configuration
        informationTypes:
          - name: ${param:Label} contact
            format: Json
        subscriptions:
          - name: ${param:Label} out
            type: Internal
            informationType: ${param:Label} contact
            handler:
              adapter: s9.openapi
              version: 1.0.0
              properties: { BaseUrl: "${param:Url}", Token: "${param:Key}", Note: "${param:Optional}" }
        """;

    string Write(string kit = KitYaml, string bundle = Bundle)
    {
        var path = Directory.CreateDirectory(Path.Combine(folder, Guid.NewGuid().ToString("N"))).FullName;
        File.WriteAllText(Path.Combine(path, "kit.yaml"), kit);
        Directory.CreateDirectory(Path.Combine(path, "bundle"));
        File.WriteAllText(Path.Combine(path, "bundle", "main.yaml"), bundle);
        return path;
    }

    [TestMethod]
    public void A_good_kit_passes_and_installs_its_connectors_first_under_its_own_name()
    {
        var kit = Kit.Load(Write());
        var (problems, warnings) = kit.Check();
        Assert.AreEqual(0, problems.Count, string.Join("\n", problems));
        Assert.AreEqual(0, warnings.Count, string.Join("\n", warnings));

        var config = kit.Configuration(new Dictionary<string, string> { ["Url"] = "https://x.test", ["Key"] = "k-123" }, null, null);
        Assert.AreEqual("s9.test.contacts", config.Name);
        var install = config.Sections[Kinds.Adapters].First();
        Assert.AreEqual("s9.openapi", (string)install["install"]);
        Assert.AreEqual("1.0.0", (string)install["version"]);

        var subscription = config.Sections["subscriptions"].Single();
        Assert.AreEqual("Test out", (string)subscription["name"]);
        Assert.AreEqual("https://x.test", (string)subscription["handler"]!["properties"]!["BaseUrl"]);
        // A secret parameter is kept out of what's printed; an optional one left out is empty.
        Assert.IsTrue(config.SecretValues.Contains("k-123"));
        Assert.AreEqual("", (string)subscription["handler"]!["properties"]!["Note"]);
    }

    [TestMethod]
    public void A_second_installation_has_its_own_name_and_labels()
    {
        var kit = Kit.Load(Write());
        var config = kit.Configuration(new Dictionary<string, string> { ["Url"] = "u", ["Key"] = "k", ["Label"] = "Riyadh" }, null, "contacts-riyadh");
        Assert.AreEqual("contacts-riyadh", config.Name);
        Assert.AreEqual("Riyadh out", (string)config.Sections["subscriptions"].Single()["name"]);
    }

    [TestMethod]
    public void A_required_parameter_not_given_is_named_and_an_unknown_one_is_refused()
    {
        var kit = Kit.Load(Write());
        var missing = Assert.ThrowsException<ConfigException>(() => kit.Configuration(new Dictionary<string, string> { ["Url"] = "u" }, null, null));
        StringAssert.Contains(missing.Message, "Key");
        var unknown = Assert.ThrowsException<ConfigException>(() => kit.Configuration(new Dictionary<string, string> { ["Colour"] = "red" }, null, null));
        StringAssert.Contains(unknown.Message, "no parameter Colour");
    }

    [TestMethod]
    public void A_parameter_read_but_not_declared_fails_and_one_declared_but_unread_warns()
    {
        var kit = Kit.Load(Write(bundle: Bundle.Replace("${param:Optional}", "${param:Undeclared}")));
        var (problems, warnings) = kit.Check();
        Assert.IsTrue(problems.Any(p => p.Contains("${param:Undeclared}")), string.Join("\n", problems));
        Assert.IsTrue(warnings.Any(w => w.Contains("Optional")), string.Join("\n", warnings));
    }

    [TestMethod]
    public void An_adapter_that_is_neither_built_in_nor_the_kit_s_fails_and_an_unpinned_connector_warns()
    {
        var other = Kit.Load(Write(bundle: Bundle.Replace("adapter: s9.openapi", "adapter: acme.other")));
        Assert.IsTrue(other.Check().Problems.Any(p => p.Contains("acme.other")));

        var unpinned = Kit.Load(Write(bundle: Bundle.Replace("      version: 1.0.0\n", "")));
        Assert.IsTrue(unpinned.Check().Warnings.Any(w => w.Contains("pin it to 1.0.0")));
    }

    /// <summary>
    /// A kit is installed inside Bitween's own process, so it reads nothing but its parameters and its own files:
    /// ${env:...} there would be the server's environment, and ${file:../...} any file the server can open.
    /// </summary>
    [DataTestMethod]
    [DataRow("${secret:SOME_KEY}", "reads values only from its parameters")]
    [DataRow("${env:Token__Key}", "reads values only from its parameters")]
    [DataRow("${file:../../kit.yaml}", "reads only its own files")]
    [DataRow("${file:/etc/hosts}", "reads only its own files")]
    public void A_kit_reads_nothing_but_its_parameters_and_its_own_files(string reference, string problem)
    {
        var kit = Kit.Load(Write(bundle: Bundle.Replace("${param:Key}", reference)));
        var (problems, _) = kit.Check();
        Assert.IsTrue(problems.Any(p => p.Contains(problem)), string.Join("\n", problems));
        var ex = Assert.ThrowsException<ConfigException>(() =>
            kit.Configuration(new Dictionary<string, string> { ["Url"] = "u", ["Key"] = "k" }, null, null));
        StringAssert.Contains(ex.Message, problem);
    }

    [TestMethod]
    public void A_kit_reads_its_own_files_in_subfolders_and_plain_apply_files_still_read_anything()
    {
        var path = Write(bundle: Bundle.Replace("${param:Key}", "${file:api/token.txt}"));
        Directory.CreateDirectory(Path.Combine(path, "bundle", "api"));
        File.WriteAllText(Path.Combine(path, "bundle", "api", "token.txt"), "from-the-kit");
        var config = Kit.Load(path).Configuration(new Dictionary<string, string> { ["Url"] = "u", ["Key"] = "k" }, null, null);
        Assert.AreEqual("from-the-kit", (string)config.Sections["subscriptions"].Single()["handler"]!["properties"]!["Token"]);

        // Outside a kit, files are the user's own: a path out of the folder, and the environment, still work.
        var plain = Path.Combine(folder, "plain.yaml");
        Environment.SetEnvironmentVariable("BITWEEN_KIT_TEST_VALUE", "from-env");
        File.WriteAllText(plain, "apiVersion: bitween/v1\nkind: Configuration\nmetadata: { name: plain }\ninformationTypes:\n  - name: ${env:BITWEEN_KIT_TEST_VALUE}\n");
        Assert.AreEqual("from-env", (string)ConfigFile.Load(plain).Sections["informationTypes"].Single()["name"]);
    }

    [DataTestMethod]
    [DataRow("id: s9.test.contacts", "id: Not Valid", "metadata.id")]
    [DataRow("version: 1.2.0", "version: one", "metadata.version")]
    [DataRow("{ id: s9.openapi, version: 1.0.0 }", "{ id: s9.openapi }", "connectors")]
    [DataRow("{ name: Url, description: Where to send }", "{ name: Url }", "has no description")]
    [DataRow("{ name: Label, description: Names, default: Test }", "{ name: 9bad, description: x }", "isn't a name")]
    public void What_kit_yaml_gets_wrong_is_said(string from, string to, string problem)
    {
        var ex = Assert.ThrowsException<ConfigException>(() => Kit.Load(Write(kit: KitYaml.Replace(from, to))));
        StringAssert.Contains(ex.Message, problem);
    }

    [TestMethod]
    public void Plan_and_apply_files_read_parameters_too()
    {
        var path = Path.Combine(folder, "plain.yaml");
        File.WriteAllText(path, "apiVersion: bitween/v1\nkind: Configuration\nmetadata: { name: plain }\ninformationTypes:\n  - name: ${param:Name}\n");
        var config = ConfigFile.Load(path, parameters: new Dictionary<string, string> { ["Name"] = "Orders" });
        Assert.AreEqual("Orders", (string)config.Sections["informationTypes"].Single()["name"]);
        var ex = Assert.ThrowsException<ConfigException>(() => ConfigFile.Load(path));
        StringAssert.Contains(ex.Message, "--set");
    }
}
