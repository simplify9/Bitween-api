using System.Text.RegularExpressions;

namespace SW.Bitween;

/// <summary>
/// TLS for Bitween's own MySQL database. The driver's default, Preferred, encrypts only when the server
/// offers it and falls back to plain text otherwise, so a server without TLS, or someone in between who
/// strips it, gets credentials and payloads in the clear. Required is used instead, unless the
/// connection string chooses a mode itself, which is how a deployment whose server has no TLS keeps
/// connecting (SslMode=Preferred, or None).
/// </summary>
public static class MySqlTls
{
    // SslMode, Ssl Mode and Ssl-Mode are all one key to the drivers.
    static readonly Regex SslModeKey = new(@"(^|;)\s*ssl[\s-]*mode\s*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string RequiredUnlessChosen(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString) || SslModeKey.IsMatch(connectionString)) return connectionString;
        return connectionString.TrimEnd().TrimEnd(';') + ";SslMode=Required";
    }
}
