using SW.Bitween.Adapters;

namespace SW.Bitween.Adapters.Db.Oracle;

/// <summary>
/// Every setting here arrives as a DataSource property, bound by name, and the form an operator
/// fills in is generated from these attributes - so a field added here appears in Bitween with no
/// front-end change.
///
/// Oracle is the awkward one to configure, and the hints carry that weight deliberately: service
/// name versus SID is the single most common reason a connection that "should work" does not.
/// </summary>
[AdapterSettings(
    Kind = "Relational",
    Label = "Oracle Database",
    Description = "An Oracle database, held open with a pooled connection so statements, procedures "
                + "and polling receivers do not pay a connect on every message.")]
public class OracleOptions : DbOptionsBase
{
    [AdapterSetting(Required = true, Hint = "Host name or IP of the database listener. No protocol prefix.")]
    public string Host { get; set; } = "localhost";

    [AdapterSetting(Default = "1521", Hint = "The listener port. 1521 unless someone changed it.")]
    public int Port { get; set; } = 1521;

    [AdapterSetting(Hint =
        "The SERVICE name - what `lsnrctl services` lists, e.g. FREEPDB1 or ORCLPDB1. This is what a "
        + "modern Oracle wants. Leave empty only if you are connecting by SID instead.")]
    public string ServiceName { get; set; }

    [AdapterSetting(Hint =
        "The SID, for an older instance that has no service name. Set exactly one of ServiceName "
        + "and Sid - a connection naming both is rejected rather than silently preferring one.")]
    public string Sid { get; set; }

    [AdapterSetting(Hint =
        "A full TNS descriptor or an Easy Connect string, used INSTEAD of host/port/service. This is "
        + "the escape hatch for RAC, Data Guard and wallet-based cloud connections, where no set of "
        + "separate fields is ever going to be enough.")]
    public string ConnectDescriptor { get; set; }

    [AdapterSetting(Required = true, Hint = "The schema owner, or a user granted access to it.")]
    public string UserName { get; set; }

    [AdapterSetting(Secret = true, Required = true)]
    public string Password { get; set; }

    [AdapterSetting(Hint =
        "The schema unqualified object names resolve against. Defaults to the login's own. Set it "
        + "when the login is a service account reading someone else's schema.")]
    public string Schema { get; set; }

    [AdapterSetting(Default = "false", AllowedValues = new[] { "true", "false" }, Hint =
        "Connect as SYSDBA. Almost never right for an integration login, and a reason to ask why "
        + "the account needs it.")]
    public bool AsSysDba { get; set; }

    [AdapterSetting(Hint =
        "Directory holding an Oracle wallet (cwallet.sso), for mTLS or an Autonomous Database. "
        + "Pair it with a ConnectDescriptor naming the alias from tnsnames.ora.")]
    public string WalletDirectory { get; set; }

    [AdapterSetting(Default = "100", Hint =
        "Rows the driver fetches per round trip. The driver's own default is 100; raising it helps a "
        + "large read over a slow link and costs client memory.")]
    public int FetchSize { get; set; } = 100;

    [AdapterSetting(Default = "true", AllowedValues = new[] { "true", "false" }, Hint =
        "Bind parameters by NAME rather than by position. On, and it should stay on: off, ODP.NET "
        + "binds in the order parameters were added, so a statement using :id twice, or listing them "
        + "in a different order than the code adds them, silently binds the wrong values.")]
    public bool BindByName { get; set; } = true;
}
