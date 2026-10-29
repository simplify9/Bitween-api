using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.Services.Marketplace;

namespace SW.Bitween.UnitTests;

/// <summary>
/// The registry's signatures, checked the way the installer checks them. The key and signature
/// below were made with openssl over the exact text the registry signs, so a drift between the
/// registry's message format and Bitween's fails here.
/// </summary>
[TestClass]
public class RegistrySignaturesTests
{
    const string GoldenKey = "MCowBQYDK2VwAyEAw0J4PEsvuWAceFgQI0tNAy9MMjNCU7wzE6H1fhG9wI8=";
    const string GoldenSignature = "TqosPpdGqRDVpmBJbcGpjPeleRz/AfgbA7tPWgloCz02rmuSypDx2MajCf5hrjM7ereE9K2ClmI1DY0cxTPNBg==";
    static readonly string Sha = new('a', 64);
    static readonly DateTime SignedOn = new(2026, 10, 12, 12, 33, 27, DateTimeKind.Utc);

    [TestMethod]
    public void A_countersignature_made_by_openssl_verifies()
    {
        var message = RegistrySignatures.CountersignMessage("s9.orders", "1.0.0", Sha, "s9", "6aff15b30ab4dfea", "Official", SignedOn);
        Assert.IsTrue(RegistrySignatures.Verify(RegistrySignatures.ReadPublic(GoldenKey), message, GoldenSignature));
    }

    [TestMethod]
    public void Changing_anything_signed_fails_the_check()
    {
        var key = RegistrySignatures.ReadPublic(GoldenKey);
        Assert.IsFalse(RegistrySignatures.Verify(key,
            RegistrySignatures.CountersignMessage("s9.orders", "1.0.1", Sha, "s9", "6aff15b30ab4dfea", "Official", SignedOn), GoldenSignature));
        Assert.IsFalse(RegistrySignatures.Verify(key,
            RegistrySignatures.CountersignMessage("s9.orders", "1.0.0", Sha, "s9", "6aff15b30ab4dfea", "Community", SignedOn), GoldenSignature));
        Assert.IsFalse(RegistrySignatures.Verify(key,
            RegistrySignatures.CountersignMessage("s9.orders", "1.0.0", Sha, "s9", "6aff15b30ab4dfea", "Official", SignedOn.AddSeconds(1)), GoldenSignature));
        Assert.IsFalse(RegistrySignatures.Verify(key, "anything", "not base64"));
    }

    [TestMethod]
    public void A_time_without_a_zone_is_read_as_utc()
    {
        // Wherever this server's clock is set, a time the registry sent without a zone means UTC.
        var unspecified = DateTime.SpecifyKind(SignedOn, DateTimeKind.Unspecified);
        Assert.AreEqual("2026-10-12T12:33:27Z", RegistrySignatures.Timestamp(unspecified));
        Assert.AreEqual("2026-10-12T12:33:27Z", RegistrySignatures.Timestamp(SignedOn.ToLocalTime()));
    }

    [TestMethod]
    public void Messages_are_the_registrys_text_exactly()
    {
        Assert.AreEqual($"bitween-registry/v1/publish\ns9.orders\n1.0.0\n{Sha}",
            RegistrySignatures.PublishMessage("s9.orders", "1.0.0", Sha.ToUpperInvariant()));
        Assert.AreEqual("bitween-registry/v1/revocations\n{}", RegistrySignatures.RevocationsMessage("{}"));
    }

    [TestMethod]
    public void Trusted_keys_are_read_from_base64_or_pem_separated_any_way()
    {
        const string pem = "-----BEGIN PUBLIC KEY-----\nMCowBQYDK2VwAyEAw0J4PEsvuWAceFgQI0tNAy9MMjNCU7wzE6H1fhG9wI8=\n-----END PUBLIC KEY-----";
        var (keys, unreadable) = RegistrySignatures.TrustedKeys($"{GoldenKey}, {GoldenKey}\n{pem}\nnot-a-key");
        Assert.AreEqual(3, keys.Count);
        Assert.AreEqual(1, unreadable.Count);
        Assert.AreEqual(0, RegistrySignatures.TrustedKeys("").Keys.Count);
    }

    [TestMethod]
    public void Bitween_ships_trusting_simplify9s_registry()
    {
        var options = new BitweenOptions();
        var (keys, unreadable) = RegistrySignatures.TrustedKeys(options.RegistryTrustedKeys);
        Assert.AreEqual(0, unreadable.Count);
        Assert.AreEqual(1, keys.Count);
        Assert.IsTrue(options.RegistryEnabled);
        Assert.AreEqual(BitweenOptions.OfficialRegistryUrl, options.RegistryUrl);
    }

    [TestMethod]
    public void A_key_id_is_the_registrys()
    {
        // The registry's key ids: the first 16 hex characters of the SHA-256 of the key's DER.
        var id = RegistrySignatures.KeyIdOf(RegistrySignatures.ReadPublic(GoldenKey));
        Assert.AreEqual(16, id.Length);
        StringAssert.Matches(id, new System.Text.RegularExpressions.Regex("^[0-9a-f]{16}$"));
    }
}
