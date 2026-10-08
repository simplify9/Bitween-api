using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SW.Bitween.UnitTests;

[TestClass]
public class SecretColumnCipherTests
{
    [TestCleanup]
    public void Reset() => SecretColumnCipher.Configure(null);

    [TestMethod]
    public void Without_a_key_values_are_stored_as_they_are()
    {
        SecretColumnCipher.Configure(null);
        Assert.AreEqual("{\"Password\":\"p\"}", SecretColumnCipher.Protect("{\"Password\":\"p\"}"));
    }

    [TestMethod]
    public void A_value_round_trips_and_is_not_stored_in_clear()
    {
        SecretColumnCipher.Configure("a passphrase for the tests");
        var stored = SecretColumnCipher.Protect("{\"Password\":\"hunter2\"}");

        StringAssert.StartsWith(stored, SecretColumnCipher.Prefix);
        Assert.IsFalse(stored.Contains("hunter2"));
        Assert.AreEqual("{\"Password\":\"hunter2\"}", SecretColumnCipher.Unprotect(stored));
    }

    [TestMethod]
    public void Plaintext_written_before_encryption_still_reads() 
    {
        SecretColumnCipher.Configure("a passphrase for the tests");
        Assert.AreEqual("{\"a\":\"b\"}", SecretColumnCipher.Unprotect("{\"a\":\"b\"}"));
    }

    [TestMethod]
    public void A_value_written_with_the_previous_key_reads_after_the_key_changes()
    {
        SecretColumnCipher.Configure("the old key");
        var stored = SecretColumnCipher.Protect("secret");

        SecretColumnCipher.Configure("the new key", previousPassphrase: "the old key");
        Assert.AreEqual("secret", SecretColumnCipher.Unprotect(stored));
    }

    [TestMethod]
    public void A_value_written_with_an_unknown_key_says_so()
    {
        SecretColumnCipher.Configure("the old key");
        var stored = SecretColumnCipher.Protect("secret");

        SecretColumnCipher.Configure("a different key");
        var ex = Assert.ThrowsException<InvalidOperationException>(() => SecretColumnCipher.Unprotect(stored));
        StringAssert.Contains(ex.Message, "PreviousSettingsEncryptionKey");
    }
}
