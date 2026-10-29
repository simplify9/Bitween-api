using SW.Bitween.Adapters;

namespace SW.Bitween.Adapters.Db.PostgreSql;

/// <summary>
/// Every setting here arrives as a DataSource property, bound by name, and the form an operator
/// fills in is generated from these attributes - so a field added here appears in Bitween with no
/// front-end change.
///
/// PostgreSQL is the easy one to configure, and the hints spend their weight where it is actually
/// needed instead: SSL mode, which is the setting people get wrong against a managed instance, and
/// search_path, which decides what an unqualified table name even means.
/// </summary>
[AdapterSettings(
    Kind = "Relational",
    Label = "PostgreSQL",
    Description = "A PostgreSQL database, held open with a pooled connection so statements, "
                + "functions and polling receivers do not pay a connect on every message.")]
public class PostgreSqlOptions : DbOptionsBase
{
    [AdapterSetting(Required = true, Hint = "Host name or IP. No protocol prefix, no postgres:// URL.")]
    public string Host { get; set; } = "localhost";

    [AdapterSetting(Default = "5432")]
    public int Port { get; set; } = 5432;

    [AdapterSetting(Required = true, Hint = "The database to connect to, not the server.")]
    public string Database { get; set; }

    [AdapterSetting(Required = true)]
    public string UserName { get; set; }

    [AdapterSetting(Secret = true, Required = true)]
    public string Password { get; set; }

    [AdapterSetting(Default = "Prefer",
        AllowedValues = new[] { "Disable", "Allow", "Prefer", "Require", "VerifyCA", "VerifyFull" },
        Hint = "Require or above for anything that is not localhost. Prefer will silently fall back "
             + "to an unencrypted connection if the server does not offer TLS, which is exactly the "
             + "case you wanted to know about.")]
    public string SslMode { get; set; } = "Prefer";

    [AdapterSetting(Hint =
        "The search_path unqualified names resolve against, e.g. `sales` or `sales, public`. Leave "
        + "empty for the server default. Set it when the login is a service account reading someone "
        + "else's schema, rather than qualifying every statement by hand.")]
    public string Schema { get; set; }

    [AdapterSetting(Default = "Bitween", Hint =
        "What this connection calls itself in pg_stat_activity. Worth keeping distinctive: it is "
        + "how a DBA works out which of the connections on their server is yours.")]
    public string ApplicationName { get; set; } = "Bitween";

    [AdapterSetting(Default = "false", AllowedValues = new[] { "true", "false" }, Hint =
        "Use the extended protocol's automatic statement preparation. Faster for statements run "
        + "over and over, which is what a subscription does - but it holds a prepared plan per "
        + "connection, and a plan cached against skewed data can be worse than replanning.")]
    public bool AutoPrepare { get; set; }

    [AdapterSetting(Default = "300", Hint =
        "Seconds an idle pooled connection is kept before it is closed. Below MinPoolSize the pool "
        + "keeps them anyway; this only prunes the ones above it.")]
    public int ConnectionIdleLifetimeSeconds { get; set; } = 300;
}
