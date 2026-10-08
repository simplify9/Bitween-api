using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.NativeAdapters;

namespace SW.Bitween.UnitTests;

[TestClass]
public class FtpProtocolTests
{
    /// <summary>OpenSSH prints "SHA256:" and drops the padding; either form must match the other.</summary>
    [DataTestMethod]
    [DataRow("SHA256:nThbg6kXUpJWGl7E1IGOCspRomTxdCARLviKw6E5SY8", "nThbg6kXUpJWGl7E1IGOCspRomTxdCARLviKw6E5SY8=")]
    [DataRow("  sha256:abc=  ", "abc")]
    public void Fingerprints_compare_across_formats(string configured, string presented) =>
        Assert.AreEqual(FtpProtocol.NormalizeFingerprint(configured), FtpProtocol.NormalizeFingerprint(presented));

    /// <summary>
    /// The upload name comes from the exchange — from an email subject, for a POP3 receiver — so
    /// it is cut to one name and can't climb out of the target directory.
    /// </summary>
    [DataTestMethod]
    [DataRow("invoice.xml", "invoice.xml")]
    [DataRow("../../etc/cron.d/x", "x")]
    [DataRow("..\\..\\windows\\x.bat", "x.bat")]
    [DataRow("/absolute/path/report.csv", "report.csv")]
    [DataRow("..", null)]
    [DataRow("dir/", null)]
    public void Upload_names_are_one_file_name(string given, string expected) =>
        Assert.AreEqual(expected, FtpProtocol.SafeFileName(given));
}
