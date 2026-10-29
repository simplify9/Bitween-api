using SW.Bitween.Adapters;

namespace SW.Bitween.Adapters.Db.SqlServer;

/// <summary>
/// Every setting here arrives as a DataSource property, bound by name, and the form an operator
/// fills in is generated from these attributes - so a field added here appears in Bitween with no
/// front-end change.
///
/// The hints spend their weight on encryption, because SQL Server is the engine where the default
/// changed underneath everybody: the modern driver encrypts by default and then refuses a
/// self-signed certificate, which is what most on-premises instances present.
/// </summary>
[AdapterSettings(
    Kind = "Relational",
    Label = "SQL Server",
    Description = "A Microsoft SQL Server or Azure SQL database, held open with a pooled "
                + "connection so statements, procedures and polling receivers do not pay a "
                + "connect on every message.")]
public class SqlServerOptions : DbOptionsBase
{
    [AdapterSetting(Required = true, Hint =
        "Host name, IP, or host\\instance for a named instance. No tcp: prefix - set Port instead.")]
    public string Host { get; set; } = "localhost";

    [AdapterSetting(Default = "1433", Hint =
        "Ignored when Host names an instance: a named instance is resolved through the SQL Server "
        + "Browser rather than by port.")]
    public int Port { get; set; } = 1433;

    [AdapterSetting(Required = true, Hint = "The database to connect to, not the server.")]
    public string Database { get; set; }

    [AdapterSetting(Required = true, Hint =
        "A SQL login, or DOMAIN\\user for Windows authentication where the host supports it.")]
    public string UserName { get; set; }

    [AdapterSetting(Secret = true, Required = true)]
    public string Password { get; set; }

    /// <summary>
    /// The default schema for unqualified names is a property of the LOGIN, not of the connection
    /// string - there is no equivalent of PostgreSQL's search_path to set here. This runs a
    /// per-connection default instead, which is why it is worded as what it does rather than as a
    /// setting name.
    /// </summary>
    [AdapterSetting(Hint =
        "Run as this schema, so an unqualified table name in a statement resolves against it. "
        + "Leave empty to use the login's own default, which is usually dbo. Only takes effect if "
        + "the login can impersonate the schema's owner; when it cannot, the connection still "
        + "works and unqualified names keep resolving as before.")]
    public string Schema { get; set; }

    [AdapterSetting(Default = "true", AllowedValues = new[] { "true", "false" }, Hint =
        "Encrypt the connection. On by default in the modern driver, which is a change from the "
        + "old one - and the reason an instance that worked for years starts failing on upgrade.")]
    public bool Encrypt { get; set; } = true;

    [AdapterSetting(Default = "false", AllowedValues = new[] { "true", "false" }, Hint =
        "Accept the server's certificate without validating it. Needed for the usual on-premises "
        + "instance with a self-signed certificate - and it means an attacker between here and the "
        + "server could present their own. Prefer installing the certificate; use this knowingly.")]
    public bool TrustServerCertificate { get; set; }

    [AdapterSetting(Default = "Bitween", Hint =
        "What this connection calls itself in sys.dm_exec_sessions. Worth keeping distinctive: it "
        + "is how a DBA works out which of the connections on their server is yours.")]
    public string ApplicationName { get; set; } = "Bitween";

    [AdapterSetting(Default = "false", AllowedValues = new[] { "true", "false" }, Hint =
        "Use MultipleActiveResultSets. Off unless something needs it: it lets one connection hold "
        + "several open readers, and the cost is that the connection cannot be reset cleanly "
        + "between uses, which is the opposite of what a shared pool wants.")]
    public bool MultipleActiveResultSets { get; set; }

    [AdapterSetting(Default = "false", AllowedValues = new[] { "true", "false" }, Hint =
        "Read committed snapshot for this connection's transactions, so readers do not block "
        + "writers. Only has an effect where the database has snapshot isolation enabled.")]
    public bool SnapshotIsolation { get; set; }
}
