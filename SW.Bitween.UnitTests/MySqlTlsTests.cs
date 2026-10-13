using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SW.Bitween.UnitTests;

/// <summary>
/// Bitween's own MySQL connection requires TLS unless the connection string picks a mode itself, which
/// is how a server without TLS keeps connecting.
/// </summary>
[TestClass]
public class MySqlTlsTests
{
    [DataTestMethod]
    [DataRow("Server=db;Database=bitween;Uid=bitween;Pwd=x", "Server=db;Database=bitween;Uid=bitween;Pwd=x;SslMode=Required")]
    [DataRow("Server=db;Database=bitween; ", "Server=db;Database=bitween;SslMode=Required")]
    [DataRow("Server=db;SslMode=Preferred", "Server=db;SslMode=Preferred")]
    [DataRow("Server=db;ssl mode=None", "Server=db;ssl mode=None")]
    [DataRow("Server=db; Ssl-Mode = VerifyFull", "Server=db; Ssl-Mode = VerifyFull")]
    [DataRow("SslMode=None;Server=db", "SslMode=None;Server=db")]
    public void Tls_is_required_unless_the_connection_string_chooses(string given, string used) =>
        Assert.AreEqual(used, MySqlTls.RequiredUnlessChosen(given));
}
