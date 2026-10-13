using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SW.Bitween.UnitTests;

/// <summary>
/// Password hashing has the worst failure mode in the codebase: change the algorithm and nothing
/// fails to compile, nothing throws, and every account silently stops being able to sign in. These
/// tests are the thing that notices.
/// </summary>
[TestClass]
public class SecurePasswordHasherTests
{
    /// <summary>The seeded administrator, hashed under V1 (PBKDF2-HMAC-SHA1, 10,000 iterations).</summary>
    private const string SeededV1Hash =
        "$SWHASH$V1$10000$VQCi48eitH4Ml5juvBMOFZrMdQwBbhuIQVXe6RR7qJdDF2bJ";

    private const string SeededPassword = "Mtm@dmin!2";

    /// <summary>
    /// The reason V1 verification still exists. Every account created before the move to SHA256
    /// carries a V1 hash, including the one the database seeds and every developer signs in with.
    /// </summary>
    [TestMethod]
    public void An_account_hashed_before_the_upgrade_can_still_sign_in()
    {
        Assert.IsTrue(SecurePasswordHasher.Verify(SeededPassword, SeededV1Hash));
        Assert.IsFalse(SecurePasswordHasher.Verify("not the password", SeededV1Hash));
    }

    [TestMethod]
    public void A_new_password_is_written_as_V2()
    {
        StringAssert.StartsWith(SecurePasswordHasher.Hash(SeededPassword), "$SWHASH$V2$");
    }

    [TestMethod]
    public void A_hash_verifies_the_password_it_was_made_from()
    {
        var hash = SecurePasswordHasher.Hash("correct horse battery staple");

        Assert.IsTrue(SecurePasswordHasher.Verify("correct horse battery staple", hash));
        Assert.IsFalse(SecurePasswordHasher.Verify("Correct horse battery staple", hash));
    }

    /// <summary>
    /// Same password, different hash. Without a per-password salt, one precomputed table breaks
    /// every account that chose the same password.
    /// </summary>
    [TestMethod]
    public void The_same_password_hashes_differently_every_time()
    {
        Assert.AreNotEqual(SecurePasswordHasher.Hash("shared"), SecurePasswordHasher.Hash("shared"));
    }

    /// <summary>
    /// A corrupt row is a wrong answer, not a server error: one bad record must not take the
    /// sign-in endpoint down for everyone else.
    /// </summary>
    [DataTestMethod]
    [DataRow("$SWHASH$V2$210000$AAAA")]
    [DataRow("$SWHASH$V2$210000$not base64 at all")]
    [DataRow("$SWHASH$V2$0$AAAA")]
    [DataRow("$SWHASH$V2$notanumber$AAAA")]
    public void A_malformed_stored_hash_fails_to_verify_rather_than_throwing(string stored)
    {
        Assert.IsFalse(SecurePasswordHasher.Verify("anything", stored));
    }

    [TestMethod]
    public void Something_that_is_not_a_hash_at_all_is_not_supported()
    {
        Assert.IsFalse(SecurePasswordHasher.IsHashSupported("plaintext"));
        Assert.IsFalse(SecurePasswordHasher.IsHashSupported(null));
        Assert.IsTrue(SecurePasswordHasher.IsHashSupported(SeededV1Hash));
        Assert.IsTrue(SecurePasswordHasher.IsHashSupported(SecurePasswordHasher.Hash("x")));
    }

    /// <summary>
    /// What sign-in replaces once it has the password: a V1 hash, and a V2 hash made with fewer
    /// iterations than today's. A current hash is left alone.
    /// </summary>
    [TestMethod]
    public void Only_an_old_hash_needs_rehashing()
    {
        Assert.IsTrue(SecurePasswordHasher.NeedsRehash(SeededV1Hash));
        var current = SecurePasswordHasher.Hash("x");
        Assert.IsTrue(SecurePasswordHasher.NeedsRehash(current.Replace("$210000$", "$10000$")));
        Assert.IsFalse(SecurePasswordHasher.NeedsRehash(SecurePasswordHasher.Hash("x")));
    }
}
