using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SW.Bitween.UnitTests;

/// <summary>
/// A masked secret sent back means "keep what is stored" — but not once the adapter, or where it
/// connects, has changed, or an editor who never saw the password could send it to their own server.
/// </summary>
[TestClass]
public class SecretKeepingTests
{
    private static readonly Dictionary<string, string> Stored = new()
    {
        ["Url"] = "https://partner.example/api", ["Password"] = "secret", ["Subject"] = "Orders",
    };

    private static Dictionary<string, string> Incoming(string url = "https://partner.example/api",
        string subject = "Orders") => new()
    {
        ["Url"] = url, ["Password"] = AdapterSecretProperties.Sentinel, ["Subject"] = subject,
    };

    [TestMethod]
    public void An_unrelated_edit_keeps_the_secret() =>
        Assert.IsTrue(AdapterSecretProperties.MayKeepStoredSecrets("http", "http", Stored, Incoming(subject: "Invoices")));

    [TestMethod]
    public void Pointing_the_adapter_elsewhere_drops_it() =>
        Assert.IsFalse(AdapterSecretProperties.MayKeepStoredSecrets("http", "http", Stored, Incoming(url: "https://attacker.example")));

    [TestMethod]
    public void Switching_adapters_drops_it() =>
        Assert.IsFalse(AdapterSecretProperties.MayKeepStoredSecrets("http", "smtp", Stored, Incoming()));

    [TestMethod]
    public void Removing_the_destination_drops_it()
    {
        var incoming = Incoming();
        incoming.Remove("Url");
        Assert.IsFalse(AdapterSecretProperties.MayKeepStoredSecrets("http", "http", Stored, incoming));
    }

    [DataTestMethod]
    [DataRow("Url", true)] [DataRow("LoginUrl", true)] [DataRow("ServiceUrl", true)] [DataRow("Host", true)]
    [DataRow("SmtpServer", true)] [DataRow("Port", true)] [DataRow("ConnectionString", true)]
    [DataRow("Subject", false)] [DataRow("Password", false)] [DataRow("BatchSize", false)]
    public void Destinations_are_recognised_by_name(string key, bool destination) =>
        Assert.AreEqual(destination, AdapterSecretProperties.IsDestination(key));

    [TestMethod]
    public void When_it_may_not_be_kept_the_sentinel_restores_nothing()
    {
        var merged = AdapterSecretProperties.Merge(Stored, Incoming(url: "https://attacker.example"), keepStored: false);
        Assert.IsFalse(merged.ContainsKey("Password"));
    }
}
