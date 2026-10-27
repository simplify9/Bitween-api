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
        foreach (var value in new[] { "SO-1001-X", "ACME Ltd.", "0045 2231 8890", "acme_prod" })
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
    public void Addresses_are_left_for_stagings_own()
    {
        Assert.AreEqual("address", MirrorFaker.BlankKind("BaseUrl", "https://ws05.acmeparcel.net:8443/api/v2/orders?apikey=XK99&mode=live"));
        Assert.AreEqual("address", MirrorFaker.BlankKind("SftpHost", "ws05.acmeparcel.net"));
        Assert.AreEqual("address", MirrorFaker.BlankKind("Recipients", "ops@acmecargo.net, Lina <lina@acmecargo.net>"));
        Assert.AreEqual("address", MirrorFaker.BlankKind("Server", "10.20.30.40"));
        Assert.AreEqual("address", MirrorFaker.BlankKind("ConnectionString", "Host=db.acmecargo.net;Port=5432;Database=ship;Username=app"));
        Assert.AreEqual("address", MirrorFaker.BlankKind("Broker", "amqp://mq.acmecargo.net:5672/prod"));
        Assert.AreEqual("address", MirrorFaker.BlankKind("note", "mail ops@acmecargo.net"));
    }

    [TestMethod]
    public void Logins_are_left_for_the_teams_test_login()
    {
        Assert.AreEqual("login", MirrorFaker.BlankKind("Username", "acme"));
        Assert.AreEqual("login", MirrorFaker.BlankKind("SftpUser", "ops"));
        Assert.AreEqual("login", MirrorFaker.BlankKind("client_id", "acme-prod"));
        // An access key's id goes by a secret's name, so it stays a secret.
        Assert.AreEqual("secret", MirrorFaker.BlankKind("AccessKeyId", "AKIAEXAMPLE7Q2"));
        // An address named like a login is still a login: the team's own goes there.
        Assert.AreEqual("login", MirrorFaker.BlankKind("LoginUser", "ops@acmecargo.net"));
        Assert.IsNull(MirrorFaker.BlankKind("UserAgent", "Bitween/10"));
        Assert.IsNull(MirrorFaker.BlankKind("Username", "{{partner.ftpUser}}"));
    }

    [TestMethod]
    public void A_placeholder_stands_in_and_reads_as_still_to_fill_in()
    {
        Assert.AreEqual("sftp://set-me.invalid", MirrorFaker.Placeholder("address", "Url", "sftp://ws05.acmeparcel.net:22/in"));
        Assert.AreEqual("set-me@set-me.invalid", MirrorFaker.Placeholder("address", "To", "ops@acmecargo.net, lina@acmecargo.net"));
        Assert.AreEqual("set-me.invalid", MirrorFaker.Placeholder("address", "Host", "10.20.30.40"));
        Assert.AreEqual("", MirrorFaker.Placeholder("secret", "Password", MirrorFaker.Masked));
        Assert.AreEqual("set-me", MirrorFaker.Placeholder("login", "clientId", "acme-prod"));

        Assert.IsTrue(MirrorFaker.IsPlaceholder("https://set-me.invalid"));
        Assert.IsTrue(MirrorFaker.IsPlaceholder(" Set-Me "));
        Assert.IsTrue(MirrorFaker.IsPlaceholder(""));
        Assert.IsFalse(MirrorFaker.IsPlaceholder("https://sandbox.carrier.com/v2"));
    }

    [TestMethod]
    public void Text_that_only_looks_like_an_address_is_copied_as_it_is()
    {
        // Named like an address, holding something else.
        Assert.IsNull(MirrorFaker.BlankKind("SftpRemoteFolder", "/inbound/orders"));
        Assert.IsNull(MirrorFaker.BlankKind("ServerTimeoutSeconds", "30"));
        Assert.IsNull(MirrorFaker.BlankKind("FileName", "orders.json"));
        Assert.IsNull(MirrorFaker.BlankKind("Host", "localhost:5000"));
        // XML names its namespaces after hosts every system names alike.
        Assert.IsNull(MirrorFaker.BlankKind("Namespace", "http://www.w3.org/2001/XMLSchema"));
        // A token is copied: what it names is staging's own.
        Assert.IsNull(MirrorFaker.BlankKind("Url", "https://{{globals.api.host}}/orders"));
        Assert.IsNull(MirrorFaker.BlankKind("Token", "{{globals.carrier.token}}"));
    }

    [TestMethod]
    public void A_secret_is_left_for_stagings_own_whether_prod_masks_it_or_not()
    {
        Assert.AreEqual("secret", MirrorFaker.BlankKind("Password", MirrorFaker.Masked));
        Assert.AreEqual("secret", MirrorFaker.BlankKind("apiKey", "live_9f8e7d"));
        Assert.IsNull(MirrorFaker.BlankKind("Method", "POST"));
    }

    [TestMethod]
    public void Words_the_setup_names_are_kept_wherever_they_turn_up()
    {
        var faker = new MirrorFaker(Encoding.UTF8.GetBytes("a key only these tests use"), ["SHIPPED", "ORD"]);

        Assert.AreEqual("SHIPPED", faker.Fake("SHIPPED"));
        StringAssert.Contains(faker.Fake("Order SO-1001 SHIPPED today"), " SHIPPED ");
        Assert.IsFalse(faker.Fake("Order SO-1001 SHIPPED today").Contains("SO-1001"));

        // A file pattern's word and extension stay, so ORD_*.csv still matches; the date stays a date.
        var name = faker.FileName("ORD_2026-10-20_0042.csv");
        StringAssert.StartsWith(name, "ORD_");
        StringAssert.EndsWith(name, ".csv");
        Assert.IsTrue(DateTime.TryParse(name[4..14], out _), name);
        Assert.AreNotEqual("ORD_2026-10-20_0042.csv", name);
    }

    [TestMethod]
    public void A_word_is_the_same_stand_in_alone_and_inside_a_text()
    {
        var alone = Faker.Fake("C123");
        Assert.AreEqual(alone, Faker.Fake("Ref C123").Split(' ')[1]);
        Assert.AreEqual(alone + ".xml", Faker.FileName("C123.xml"));
    }

    [TestMethod]
    public void A_day_written_without_dashes_moves_with_the_same_day_written_with_them()
    {
        var dashed = Faker.Fake("2026-10-20");
        Assert.AreEqual(dashed.Replace("-", ""), Faker.Fake("20261020"));
        Assert.AreEqual(dashed.Replace("-", "") + "101500", Faker.Fake("20261020101500"));
    }

    [TestMethod]
    public void Digits_of_any_script_are_faked_and_stay_that_script()
    {
        const string phone = "٠٧٩١٢٣٤٥٦٧";
        var fake = Faker.Fake(phone);
        Assert.AreNotEqual(phone, fake);
        Assert.AreEqual(phone.Length, fake.Length);
        Assert.IsTrue(fake.All(c => c is >= '٠' and <= '٩'), fake);
        Assert.AreEqual('٠', fake[0]);
    }

    [TestMethod]
    public void Data_is_faked_unless_kept()
    {
        Assert.AreNotEqual("Europe/Paris", Faker.Datum("timeZone", "Europe/Paris"));
        Assert.AreEqual("Europe/Paris", Faker.Datum("timeZone", "Europe/Paris", keepReal: true));
        Assert.AreEqual("2222", Faker.Datum("SftpPort", "2222"));
    }

    [TestMethod]
    [Timeout(5000)]
    public void A_long_value_with_no_address_in_it_is_faked_in_time()
    {
        // Like an attachment encoded in a file: a megabyte with neither :// nor @ once took minutes.
        var faker = new MirrorFaker(System.Text.Encoding.UTF8.GetBytes("a key only these tests use"), []);
        var value = new string('a', 1_000_000);

        Assert.AreEqual(value.Length, faker.Fake(value).Length);
        Assert.IsNull(MirrorFaker.BlankKind("Payload", value));
    }
}
