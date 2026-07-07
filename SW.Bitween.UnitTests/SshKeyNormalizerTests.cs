using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.NativeAdapters;

namespace SW.Bitween.UnitTests;

[TestClass]
public class SshKeyNormalizerTests
{
    private const string Header = "-----BEGIN OPENSSH PRIVATE KEY-----";
    private const string Footer = "-----END OPENSSH PRIVATE KEY-----";
    private const string Body = "AAAAB3NzaC1yc2EAAAADAQABAAABAQCabc123def456ghi789jkl012mno34";

    [TestMethod]
    public void WellFormedMultiLineKey_IsReturnedUnchanged()
    {
        var key = $"{Header}\n{Body}\n{Footer}";

        var result = SshKeyNormalizer.Normalize(key);

        Assert.AreEqual(key, result);
        // Regression: the header's internal spaces must survive (the old regex destroyed them).
        StringAssert.Contains(result, Header);
    }

    [TestMethod]
    public void FlattenedPemKey_IsRebuiltWithHeaderFooterAndBody()
    {
        var flattened = $"{Header} {Body} {Footer}";

        var result = SshKeyNormalizer.Normalize(flattened);
        var lines = result.Split('\n').Where(l => l.Length > 0).ToList();

        Assert.AreEqual(Header, lines.First());          // header intact, spaces preserved
        Assert.AreEqual(Footer, lines.Last());           // footer intact
        // Body reassembled with no whitespace, wrapped across the middle lines.
        var rebuiltBody = string.Concat(lines.Skip(1).Take(lines.Count - 2));
        Assert.AreEqual(Body, rebuiltBody);
    }

    [TestMethod]
    public void FlattenedPemKey_WrapsBodyAt64Chars()
    {
        var longBody = new string('A', 200);
        var flattened = $"{Header} {longBody} {Footer}";

        var result = SshKeyNormalizer.Normalize(flattened);
        var bodyLines = result.Split('\n').Where(l => l.Length > 0).Skip(1).SkipLast(1).ToList();

        Assert.IsTrue(bodyLines.All(l => l.Length <= 64));
        Assert.AreEqual(longBody, string.Concat(bodyLines));
    }

    [TestMethod]
    public void NullOrEmpty_ReturnsEmptyString()
    {
        Assert.AreEqual(string.Empty, SshKeyNormalizer.Normalize(null));
        Assert.AreEqual(string.Empty, SshKeyNormalizer.Normalize(""));
        Assert.AreEqual(string.Empty, SshKeyNormalizer.Normalize("   "));
    }

    [TestMethod]
    public void UnrecognizedSingleLine_IsReturnedTrimmedButUnmangled()
    {
        var result = SshKeyNormalizer.Normalize("  not-a-pem-key-just-text  ");

        Assert.AreEqual("not-a-pem-key-just-text", result);
    }
}
