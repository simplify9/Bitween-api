using SW.Bitween.Adapters;

namespace SW.Bitween.Adapters.Db.MySql;

/// <summary>
/// Every setting here arrives as a DataSource property, bound by name, and the form an operator
/// fills in is generated from these attributes - so a field added here appears in Bitween with no
/// front-end change.
///
/// The hints spend their weight on the two things that are genuinely MySQL's own: a database and
/// a schema are the same thing, which surprises anyone arriving from PostgreSQL or Oracle, and
/// SSL mode, which is what people get wrong against a managed instance.
/// </summary>
[AdapterSettings(
    Kind = "Relational",
    Label = "MySQL",
    Description = "A MySQL or MariaDB database, held open with a pooled connection so statements, "
                + "procedures and polling receivers do not pay a connect on every message.")]
public class MySqlOptions : DbOptionsBase
{
    [AdapterSetting(Required = true, Hint = "Host name or IP. No protocol prefix, no mysql:// URL.")]
    public string Host { get; set; } = "localhost";

    [AdapterSetting(Default = "3306")]
    public int Port { get; set; } = 3306;

    /// <summary>
    /// MySQL has no schema separate from the database - CREATE SCHEMA is a synonym for CREATE
    /// DATABASE - so this one value is both, and it is what an unqualified table name resolves
    /// against.
    /// </summary>
    [AdapterSetting(Required = true, Hint =
        "The database to connect to. In MySQL a database IS a schema, so this is also what an "
        + "unqualified table name resolves against - there is no separate schema setting.")]
    public string Database { get; set; }

    [AdapterSetting(Required = true)]
    public string UserName { get; set; }

    [AdapterSetting(Secret = true, Required = true)]
    public string Password { get; set; }

    [AdapterSetting(Default = "Preferred",
        AllowedValues = new[] { "None", "Preferred", "Required", "VerifyCA", "VerifyFull" },
        Hint = "Required or above for anything that is not localhost. Preferred will silently fall "
             + "back to an unencrypted connection if the server does not offer TLS, which is "
             + "exactly the case you wanted to know about.")]
    public string SslMode { get; set; } = "Preferred";

    [AdapterSetting(Default = "Bitween", Hint =
        "What this connection calls itself in the performance schema. Worth keeping distinctive: "
        + "it is how a DBA works out which of the connections on their server is yours.")]
    public string ApplicationName { get; set; } = "Bitween";

    [AdapterSetting(Default = "180", Hint =
        "Seconds an idle pooled connection is kept before it is closed. Keep it below the server's "
        + "own wait_timeout - the default is 28800, but a managed instance or a proxy in front of "
        + "one is often far lower, and a connection the server closed first surfaces as a broken "
        + "pipe on the next message rather than as a timeout.")]
    public int ConnectionIdleLifetimeSeconds { get; set; } = 180;

    [AdapterSetting(Default = "true", AllowedValues = new[] { "true", "false" }, Hint =
        "Ask the server to parse and plan a statement once, then run it by handle. Worth leaving "
        + "on for a subscription, which runs the same statement over and over. Turn it off for a "
        + "connection through a proxy such as ProxySQL, where prepared statements are pinned to a "
        + "backend and can defeat the pooling the proxy is there to provide.")]
    public bool ServerPrepare { get; set; } = true;

    [AdapterSetting(Default = "false", AllowedValues = new[] { "true", "false" }, Hint =
        "Return DATE and DATETIME as strings rather than dates. MySQL permits '0000-00-00', which "
        + "is not a date any .NET type can hold - a column holding one throws on read unless this "
        + "is on. Only needed against a schema that has them, which is usually an old one.")]
    public bool AllowZeroDateTime { get; set; }
}
