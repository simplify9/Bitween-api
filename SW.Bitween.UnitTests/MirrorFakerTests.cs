using System;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.Services.Mirror;

namespace SW.Bitween.UnitTests;

/// <summary>
/// What staging keeps of a prod value: something of the same shape, never the value itself, and
/// the same stand-in every time, so a copy can run twice and a code named in two places still matches.
/// </summary>
[TestClass]
public class MirrorFakerTests
{
    static readonly MirrorFaker Faker = new(Encoding.UTF8.GetBytes("a key only these tests use"));

    static bool SameShape(string a, string b) =>
        a.Length == b.Length && a.Zip(b).All(p =>
            char.IsAsciiDigit(p.First) ? char.IsAsciiDigit(p.Second)
            : char.IsAsciiLetterUpper(p.First) ? char.IsAsciiLetterUpper(p.Second)
            : char.IsAsciiLetterLower(p.First) ? char.IsAsciiLetterLower(p.Second)
            : p.First == p.Second);

    [TestMethod]
    public void A_value_keeps_its_shape_and_loses_its_content()
    {
        foreach (var value in new[] { "SO-1001-X", "ACME Ltd.", "0045 2231 8890", "traxis_prod" })
        {
            var fake = Faker.Fake(value);
            Assert.AreNotEqual(value, fake);
            Assert.IsTrue(SameShape(value, fake), $"{value} became {fake}");
        }
        // A number keeps its leading zeros and doesn't gain one.
        Assert.IsTrue(Faker.Fake("00123").StartsWith("00"));
        Assert.AreNotEqual('0', Faker.Fake("123456")[0]);
    }

    [TestMethod]
    public void The_same_value_always_gets_the_same_stand_in_and_another_key_another()
    {
        Assert.AreEqual(Faker.Fake("SO-1001"), Faker.Fake("SO-1001"));
        Assert.AreNotEqual(Faker.Fake("SO-1001"), Faker.Fake("SO-1002"));
        Assert.AreNotEqual(Faker.Fake("SO-1001"), new MirrorFaker(Encoding.UTF8.GetBytes("another")).Fake("SO-1001"));
        Assert.AreEqual(Faker.Secret("partner/Acme/apiKey"), Faker.Secret("partner/Acme/apiKey"));
        Assert.AreNotEqual(Faker.Secret("partner/Acme/apiKey"), Faker.Secret("partner/Zed/apiKey"));
    }

    [TestMethod]
    public void Tokens_words_dates_guids_and_json_stay_what_they_are()
    {
        Assert.AreEqual("Bearer {{globals.api.token}}".Length, Faker.Fake("Bearer {{globals.api.token}}").Length);
        StringAssert.EndsWith(Faker.Fake("Bearer {{globals.api.token}}"), "{{globals.api.token}}");
        Assert.AreEqual("true", Faker.Fake("true"));

        var date = Faker.Fake("2026-02-28T10:15:00Z");
        Assert.IsTrue(DateTime.TryParse(date[..10], out _), date);
        StringAssert.EndsWith(date, "T10:15:00Z");

        var guid = Faker.Fake("3f2a9c1e-77b0-4c11-8a2e-0d9b5e6f7a10");
        Assert.IsTrue(Guid.TryParse(guid, out _), guid);
        Assert.AreNotEqual("3f2a9c1e-77b0-4c11-8a2e-0d9b5e6f7a10", guid);

        var json = JsonNode.Parse(Faker.Fake("""{"account":"AC-778","limit":250,"live":true,"tags":["blue"]}"""))!;
        Assert.AreEqual(250.ToString().Length, json["limit"]!.ToJsonString().Length);
        Assert.AreEqual(true, (bool)json["live"]!);
        Assert.AreNotEqual("AC-778", (string)json["account"]!);
        Assert.AreEqual(1, json["tags"]!.AsArray().Count);
    }

    [TestMethod]
    public void Addresses_become_mock_ones_that_resolve_nowhere()
    {
        var url = Faker.Setting("BaseUrl", "https://ws05.ffdx.net:8443/api/v2/orders?apikey=XK99&mode=live", "x");
        StringAssert.StartsWith(url, "https://m");
        StringAssert.Contains(url, ".mirror.invalid:8443/api/v2/orders?apikey=");
        Assert.IsFalse(url.Contains("ffdx"), url);
        // A setting's query is how the flow behaves; only a datum's is faked.
        StringAssert.EndsWith(url, "?apikey=XK99&mode=live");
        Assert.IsFalse(Faker.Datum("callback", "https://ws05.ffdx.net/cb?apikey=XK99", "x").Contains("XK99"));

        // The same host is the same mock wherever it appears.
        var host = new Regex(@"m[0-9a-f]{8}\.mirror\.invalid").Match(url).Value;
        Assert.AreEqual(host, Faker.Setting("SftpHost", "ws05.ffdx.net", "y"));

        var email = Faker.Setting("Recipients", "ops@traxis.app, Lina <lina@traxis.app>", "z");
        Assert.IsFalse(email.Contains("traxis"), email);
        StringAssert.Contains(email, ", Lina <");

        StringAssert.StartsWith(Faker.Setting("Server", "10.20.30.40", "w"), "192.0.2.");
        Assert.AreEqual("localhost:5000", Faker.Setting("Host", "localhost:5000", "w"));
    }

    [TestMethod]
    public void Text_that_only_looks_like_an_address_is_left_alone_in_a_setting()
    {
        // Named like an address, holding something else.
        Assert.AreEqual("/inbound/orders", Faker.Setting("SftpRemoteFolder", "/inbound/orders", "x"));
        Assert.AreEqual("30", Faker.Setting("ServerTimeoutSeconds", "30", "x"));
        Assert.AreEqual("orders.json", Faker.Setting("FileName", "orders.json", "x"));
        // XML names its namespaces after hosts; a namespace is matched as text.
        Assert.AreEqual("http://www.w3.org/2001/XMLSchema", Faker.Setting("Namespace", "http://www.w3.org/2001/XMLSchema", "x"));
        Assert.AreEqual("https://{{globals.api.host}}/orders", Faker.Setting("Url", "https://{{globals.api.host}}/orders", "x"));
    }

    [TestMethod]
    public void A_connection_string_keeps_its_settings_and_loses_its_server_and_password()
    {
        var cs = Faker.Setting("ConnectionString", "Host=db.traxis.app;Port=5432;Database=ship;Username=traxis;Password=S3cret!x", "x");
        StringAssert.Contains(cs, "Port=5432;Database=ship;");
        Assert.IsFalse(cs.Contains("traxis"), cs);
        Assert.IsFalse(cs.Contains("S3cret"), cs);

        var uri = Faker.Setting("Broker", "amqp://bitween:hunter2@mq.traxis.app:5672/prod", "x");
        Assert.IsFalse(uri.Contains("hunter2") || uri.Contains("traxis"), uri);
        StringAssert.EndsWith(uri, ":5672/prod");
    }

    [TestMethod]
    public void A_secret_prod_would_not_show_becomes_a_stand_in()
    {
        StringAssert.StartsWith(Faker.Setting("Password", MirrorFaker.Masked, "subscription/1/handler/Password"), "mirror-");
        StringAssert.StartsWith(Faker.Datum("apiKey", MirrorFaker.Masked, "partner/Acme/apiKey"), "mirror-");
    }

    [TestMethod]
    public void Data_is_faked_unless_kept_and_addresses_never_are()
    {
        Assert.AreNotEqual("Europe/Paris", Faker.Datum("timeZone", "Europe/Paris", "x"));
        Assert.AreEqual("Europe/Paris", Faker.Datum("timeZone", "Europe/Paris", "x", keepReal: true));
        Assert.IsFalse(Faker.Datum("note", "mail ops@traxis.app", "x", keepReal: true).Contains("traxis"));
        Assert.AreEqual("2222", Faker.Datum("SftpPort", "2222", "x"));
    }
}
