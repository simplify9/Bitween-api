using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SW.Bitween.UnitTests;

/// <summary>
/// Where a JWT gateway gets its login server's keys. The token checks themselves are
/// ApiGatewayTests; these are the rules about which login servers can be used, and how long a
/// key is trusted.
/// </summary>
[TestClass]
public class GatewayIssuersTests
{
    [TestMethod]
    public void A_key_the_login_server_stops_publishing_stops_being_accepted()
    {
        // Left to the library's default, it would be accepted for another hour — and a key
        // dropped that suddenly is usually one that leaked.
        Assert.IsFalse(new OpenIdGatewayIssuers().For("https://login.example.com").UseLastKnownGoodConfiguration);
    }

    [DataTestMethod]
    [DataRow("https://login.example.com", true)]
    [DataRow("https://login.microsoftonline.com/tenant-id/v2.0", true)]
    [DataRow("http://localhost:9555", true)]
    [DataRow("http://127.0.0.1:9555", true)]
    [DataRow("http://login.example.com", false)]   // its keys could be swapped on the way
    [DataRow("login.example.com", false)]
    [DataRow("", false)]
    [DataRow(null, false)]
    public void Only_https_login_servers_can_be_used_except_on_this_machine(string issuer, bool usable) =>
        Assert.AreEqual(usable, OpenIdGatewayIssuers.IsUsable(issuer));
}
