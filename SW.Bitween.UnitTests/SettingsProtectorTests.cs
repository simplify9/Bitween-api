using System;
using System.Security.Cryptography;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.Services;

namespace SW.Bitween.UnitTests;

[TestClass]
public class SettingsProtectorTests
{
    /// <summary>
    /// Shaped like a Rebex license key — the longest secret the catalog holds — but deliberately
    /// not one. Nothing here depends on the contents, only on the round trip.
    /// </summary>
    private const string Secret = "==NOT-A-REAL-KEY-0123456789abcdefghijklmnop==";

    private static SettingsProtector With(string passphrase) =>
        new(new BitweenOptions { SettingsEncryptionKey = passphrase });

    [TestMethod]
    public void Round_trips_a_secret()
    {
        var protector = With("a-passphrase");

        Assert.AreEqual(Secret, protector.Unprotect(protector.Protect(Secret)));
    }

    [TestMethod]
    public void Stored_form_never_contains_the_plaintext()
    {
        var stored = With("a-passphrase").Protect(Secret);

        Assert.IsFalse(stored.Contains(Secret, StringComparison.Ordinal));
        // The version marker is what tells a stored value apart from a hand-written plain one.
        Assert.IsTrue(stored.StartsWith("enc.v1:", StringComparison.Ordinal));
    }

    /// <summary>
    /// Fresh salt and nonce per value, so two instances holding the same license key don't reveal
    /// that by having identical rows.
    /// </summary>
    [TestMethod]
    public void Encrypting_the_same_value_twice_gives_different_ciphertext()
    {
        var protector = With("a-passphrase");

        Assert.AreNotEqual(protector.Protect(Secret), protector.Protect(Secret));
    }

    // Both of these surface as AuthenticationTagMismatchException (a CryptographicException):
    // GCM can't distinguish "wrong key" from "altered bytes", and either way it refuses to
    // hand back a plaintext rather than returning garbage.
    [TestMethod]
    public void A_different_passphrase_cannot_read_it()
    {
        var stored = With("a-passphrase").Protect(Secret);

        Assert.ThrowsException<AuthenticationTagMismatchException>(
            () => With("another-passphrase").Unprotect(stored));
    }

    [TestMethod]
    public void Tampering_is_detected_rather_than_decrypted()
    {
        var protector = With("a-passphrase");
        var stored = protector.Protect(Secret);
        // Flip the last ciphertext character — the authentication tag must reject it.
        var tampered = stored[..^2] + (stored[^2] == 'A' ? 'B' : 'A') + stored[^1];

        Assert.ThrowsException<AuthenticationTagMismatchException>(() => protector.Unprotect(tampered));
    }

    /// <summary>A value written before a passphrase existed, or edited by hand, still reads back.</summary>
    [TestMethod]
    public void Unmarked_values_pass_through_untouched()
    {
        Assert.AreEqual("plain-text-value", With("a-passphrase").Unprotect("plain-text-value"));
        Assert.AreEqual("", With("a-passphrase").Unprotect(""));
        Assert.IsNull(With("a-passphrase").Unprotect(null));
    }

    [TestMethod]
    public void Empty_secrets_are_stored_as_empty_not_as_ciphertext()
    {
        Assert.AreEqual("", With("a-passphrase").Protect(""));
    }

    [TestMethod]
    public void Without_a_passphrase_nothing_can_be_protected()
    {
        Assert.IsFalse(With(null).IsConfigured);
        Assert.IsFalse(With("   ").IsConfigured);
        Assert.ThrowsException<InvalidOperationException>(() => With(null).Protect(Secret));
    }
}
