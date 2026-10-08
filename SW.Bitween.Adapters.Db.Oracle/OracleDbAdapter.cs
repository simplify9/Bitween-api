using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oracle.ManagedDataAccess.Client;
using SW.Serverless.Sdk;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SW.Bitween.Adapters.Db.Oracle;

/// <summary>
/// Bitween's Oracle data source provider.
///
/// Everything generic — the command surface, paging, the statement allow-list, the polling receiver
/// — lives in <see cref="DbResidentAdapterBase"/>. What is here is the part that is genuinely
/// Oracle: how a connection string is spelled, what the data dictionary is called, which privileges
/// are worth probing, and REF CURSOR.
/// </summary>
// Three roles, one package. "datasource" is what makes it configurable as a connection;
// "receiver" and "handler" are what put it in the pickers a subscription actually chooses from,
// because the same resident instance both polls a table and runs a statement on delivery.
// Declared rather than encoded in the id: reclassifying by rename would break every subscription
// that stores it.
[AdapterKind("datasource")]
[AdapterKind("receiver")]
[AdapterKind("handler")]
public class OracleDbAdapter(IOptions<OracleOptions> options, ILogger<OracleDbAdapter> logger)
    : DbResidentAdapterBase(options.Value, logger)
{
    readonly OracleOptions _options = options.Value;

    protected override DbProviderFactory Factory => OracleClientFactory.Instance;

    protected override string ParameterPrefix => ":";

    /// <summary>Oracle has no bare SELECT: everything comes from somewhere, and DUAL is that somewhere.</summary>
    protected override string PingStatement => "select 1 from dual";

    // ------------------------------------------------------------------ connection

    protected override string BuildConnectionString()
    {
        var builder = new OracleConnectionStringBuilder
        {
            UserID = _options.UserName,
            Password = _options.Password ?? "",
            DataSource = DataSourceOf(),
            ConnectionTimeout = _options.ConnectTimeoutSeconds,

            // The whole reason this adapter is resident. MinPoolSize keeps sessions alive through
            // quiet periods, which is the difference between a 3am job that runs and one that times
            // out reconnecting to a listener that has gone cold.
            Pooling = true,
            MinPoolSize = Math.Max(0, _options.MinPoolSize),
            MaxPoolSize = Math.Max(1, _options.MaxPoolSize)
        };

        if (_options.AsSysDba) builder.DBAPrivilege = "SYSDBA";

        var connectionString = builder.ToString();

        // Set on the driver rather than in the connection string: both are process-wide in ODP.NET
        // core, and an adapter process serves exactly one data source, so process-wide is per data
        // source here.
        if (!string.IsNullOrWhiteSpace(_options.WalletDirectory))
            OracleConfiguration.WalletLocation = _options.WalletDirectory;

        OracleConfiguration.FetchSize = Math.Max(1024, _options.FetchSize * 1024);

        return connectionString;
    }

    /// <summary>
    /// Oracle's DataSource is three different things depending on how the database is reached, and
    /// getting it wrong is the most common reason a connection that "should work" does not. So the
    /// ambiguous cases are refused with a message saying which field to fill in, rather than
    /// guessed at.
    /// </summary>
    string DataSourceOf()
    {
        if (!string.IsNullOrWhiteSpace(_options.ConnectDescriptor))
        {
            if (!string.IsNullOrWhiteSpace(_options.ServiceName) || !string.IsNullOrWhiteSpace(_options.Sid))
                throw new InvalidOperationException(
                    "ConnectDescriptor is set as well as ServiceName or Sid. The descriptor already "
                    + "says how to reach the database; clear the others so there is one answer.");

            return _options.ConnectDescriptor;
        }

        var hasService = !string.IsNullOrWhiteSpace(_options.ServiceName);
        var hasSid = !string.IsNullOrWhiteSpace(_options.Sid);

        if (hasService && hasSid)
            throw new InvalidOperationException(
                "Both ServiceName and Sid are set, and they are alternatives — a service name names "
                + "a database service, a SID names an instance. Set whichever the DBA gave you and "
                + "clear the other.");

        if (!hasService && !hasSid)
            throw new InvalidOperationException(
                "Neither ServiceName nor Sid is set, so there is nothing to connect to on "
                + $"{_options.Host}:{_options.Port}. A modern Oracle wants the SERVICE name — what "
                + "`lsnrctl services` lists, e.g. FREEPDB1.");

        // Easy Connect. The slash form is the service name, the colon form is the SID.
        return hasService
            ? $"{_options.Host}:{_options.Port}/{_options.ServiceName}"
            : $"{_options.Host}:{_options.Port}:{_options.Sid}";
    }

    protected override void PrepareCommand(DbCommand command)
    {
        // Without this ODP.NET binds by POSITION, in the order parameters were added — so a
        // statement whose parameters are written in a different order than the caller supplied them
        // binds the wrong values, silently, with no type error to catch it.
        if (command is OracleCommand oracle) oracle.BindByName = _options.BindByName;
    }

    // ------------------------------------------------------------------ capabilities

    // ------------------------------------------------------------------ checking

    private static readonly HashSet<string> DdlKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "CREATE", "ALTER", "DROP", "TRUNCATE", "GRANT", "REVOKE", "RENAME", "COMMENT", "AUDIT",
        "NOAUDIT", "ANALYZE", "FLASHBACK", "PURGE", "ASSOCIATE", "DISASSOCIATE",
    };

    /// <summary>The first word of the statement, past whitespace and comments.</summary>
    internal static string LeadingKeyword(string sql)
    {
        var i = 0;
        while (i < sql.Length)
        {
            if (char.IsWhiteSpace(sql[i])) { i++; continue; }
            if (sql.AsSpan(i).StartsWith("--"))
            {
                var end = sql.IndexOf('\n', i);
                i = end < 0 ? sql.Length : end + 1;
                continue;
            }
            if (sql.AsSpan(i).StartsWith("/*"))
            {
                var end = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? sql.Length : end + 2;
                continue;
            }
            break;
        }

        var start = i;
        while (i < sql.Length && char.IsLetter(sql[i])) i++;
        return i > start ? sql[start..i] : null;
    }

    /// <summary>
    /// Oracle is checked with <c>DBMS_SQL.PARSE</c> rather than by preparing.
    ///
    /// ODP.NET's <c>Prepare</c> is a client-side no-op — Oracle validates when a statement is
    /// executed, not when it is prepared — so the shared check passed everything here, including a
    /// select from a table that does not exist. A statement reported as verified without being
    /// looked at is worse than no check, because it is the one nobody goes back to.
    ///
    /// PARSE is the real thing: it compiles the statement and resolves every name in it, raising
    /// ORA-00942 for a missing table and ORA-00936 for a syntax error, and it runs nothing. Bind
    /// placeholders need no values — parsing is exactly the step before binding.
    /// </summary>
    protected override async Task CheckSyntaxAsync(DbConnection connection, string sql)
    {
        // Except for DDL: Oracle executes a DDL statement the moment it is parsed, so checking
        // "drop table orders" would drop it, from a Save or Test button, with no exchange behind it.
        if (LeadingKeyword(sql) is { } keyword && DdlKeywords.Contains(keyword))
            throw new InvalidOperationException(
                $"{keyword} statements can't be checked on Oracle: parsing one runs it. " +
                "Statements are for reading and writing data; run schema changes outside Bitween.");

        using var command = connection.CreateCommand();
        command.CommandText = @"
            declare
                c integer := dbms_sql.open_cursor;
            begin
                begin
                    dbms_sql.parse(c, :statement, dbms_sql.native);
                exception
                    when others then
                        -- Closed here as well as below: leaving a cursor open on the shared pooled
                        -- connection would leak one per bad statement somebody tried to save.
                        dbms_sql.close_cursor(c);
                        raise;
                end;
                dbms_sql.close_cursor(c);
            end;";
        command.CommandTimeout = 10;

        var parameter = command.CreateParameter();
        parameter.ParameterName = "statement";
        parameter.Value = sql;
        command.Parameters.Add(parameter);

        PrepareCommand(command);

        try
        {
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            // Rethrown without the PL/SQL stack. Running the parse inside a block means the driver
            // reports where in OUR block it failed — three ORA-06512 frames through SYS.DBMS_SQL —
            // underneath the one line that is about the operator's SQL. Keeping them would bury
            // "table does not exist" under the mechanism used to discover it.
            throw new InvalidOperationException(FirstOracleError(ex.Message), ex);
        }
    }

    /// <summary>
    /// The first ORA- line, which is the cause; the rest are the frames it was raised through.
    /// Anything that does not look like an Oracle error is returned whole rather than trimmed to
    /// nothing.
    /// </summary>
    static string FirstOracleError(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return message;

        var lines = message.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var cause = lines.FirstOrDefault(l =>
            l.TrimStart().StartsWith("ORA-", StringComparison.OrdinalIgnoreCase)
            && !l.TrimStart().StartsWith("ORA-06512", StringComparison.OrdinalIgnoreCase));

        return (cause ?? lines[0]).Trim();
    }

    protected override DbCapabilities DescribeEngine() => new()
    {
        Engine = "Oracle",
        SupportedObjects = ["table", "view", "materialized_view", "procedure", "function", "package", "sequence", "synonym"],

        StoredProcedures = true,
        ProcedureOutParameters = true,

        // True, but not the way the other engines mean it. An Oracle procedure does not return rows
        // by itself; it returns them through a REF CURSOR out parameter, which the caller has to
        // declare. See AddOutParameter.
        ProcedureResultSets = true,
        MultipleResultSets = false,

        NamedParameters = true,
        Transactions = true,
        IsolationLevels = ["ReadCommitted", "Serializable"],

        // Array binding exists in ODP.NET, but as a different call shape rather than a flag on this
        // path — so it is honestly false until BulkLoad is implemented for it.
        BulkCopy = false,
        Merge = true,
        Returning = true,
        Json = true,
        ArrayTypes = false,

        // Continuous Query Notification is real and this adapter does not do it yet. Declared false
        // rather than omitted, so the UI can say "not available" instead of leaving a gap.
        ChangeNotification = false,
        LogBasedCdc = false,

        SchemaDiscovery = true,
        RowCountEstimates = true,
        ReceiveModes = ["bulk", "incrementing", "timestamp", "timestamp+incrementing", "marker"],

        // `fetch first N rows only`, since 12c. `rownum` is the older idiom and is a trap with an
        // ORDER BY — it is applied before the sort, so it takes an arbitrary N and then sorts those.
        LimitStyle = "fetchFirst"
    };

    /// <summary>
    /// What this login can actually do. Read from the session's own privileges rather than assumed
    /// from the engine — an integration account is usually granted a narrow slice, and finding that
    /// out at configure time is worth a round trip.
    /// </summary>
    protected override async Task<List<string>> ProbePrivilegesAsync(DbConnection connection,
        CancellationToken cancellationToken)
    {
        var privileges = new List<string>();

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                "select privilege from session_privs order by privilege";
            command.CommandTimeout = 10;

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                privileges.Add(reader.GetString(0));
        }
        catch (Exception ex)
        {
            // A login without SELECT on session_privs is unusual but not broken — it just cannot
            // tell us what it can do. Saying so beats failing the connection test over it.
            Logger.LogDebug(ex, "Could not read session_privs.");
            privileges.Add($"(could not be read: {ex.Message})");
        }

        return privileges;
    }

    protected override IEnumerable<KeyValuePair<string, string>> ExtraStatusDetails()
    {
        yield return new KeyValuePair<string, string>("oracle.schema",
            _options.Schema ?? _options.UserName ?? "");
        yield return new KeyValuePair<string, string>("oracle.bindByName", _options.BindByName.ToString());
    }

    // ------------------------------------------------------------------ REF CURSOR

    /// <summary>
    /// The reason this hook exists at all. Every other engine's procedure hands back rows on its
    /// own; Oracle hands them back through a declared REF CURSOR out parameter, and a caller that
    /// adds a plain output parameter gets an ORA-06550 about the wrong argument type rather than
    /// anything that points at the real problem.
    /// </summary>
    protected override void AddOutParameter(DbCommand command, DbRoutineParameter declared)
    {
        if (!string.Equals(declared.Direction, "RefCursor", StringComparison.OrdinalIgnoreCase))
        {
            base.AddOutParameter(command, declared);
            return;
        }

        var parameter = new OracleParameter
        {
            ParameterName = declared.Name,
            OracleDbType = OracleDbType.RefCursor,
            Direction = ParameterDirection.Output
        };

        command.Parameters.Add(parameter);
    }

    // ------------------------------------------------------------------ discovery

    /// <summary>
    /// Read from the data dictionary rather than through <c>GetSchema</c>. ODP.NET's collections
    /// cover tables and columns, but not comments, not routine parameter direction, and not
    /// estimated row counts — and the ALL_* views answer all of it in one query per object type.
    ///
    /// ALL_, never DBA_: ALL_ is what this login can see, which is the honest answer and the one
    /// that does not need a privilege an integration account should not have.
    /// </summary>
    protected override async Task<List<DbObject>> DiscoverAsync(DbConnection connection,
        DiscoverRequest request, CancellationToken cancellationToken)
    {
        var schema = (request.Schema ?? _options.Schema)?.ToUpperInvariant();
        var like = request.NameLike?.ToUpperInvariant();

        var objects = request.ObjectType switch
        {
            "procedure" or "function" or "package" =>
                await RoutinesAsync(connection, request, schema, like, cancellationToken),
            "sequence" => await SequencesAsync(connection, request, schema, like, cancellationToken),
            _ => await RelationsAsync(connection, request, schema, like, cancellationToken)
        };

        return objects;
    }

    async Task<List<DbObject>> RelationsAsync(DbConnection connection, DiscoverRequest request,
        string schema, string like, CancellationToken cancellationToken)
    {
        // object_type is filtered rather than switched on, so "table" and "view" share one query
        // and the paging arithmetic is not written three times.
        var wanted = request.ObjectType switch
        {
            "view" => new[] { "VIEW" },
            "materialized_view" => new[] { "MATERIALIZED VIEW" },
            _ => new[] { "TABLE" }
        };

        var sql = new StringBuilder(@"
            select o.owner, o.object_name, o.object_type,
                   (select c.comments from all_tab_comments c
                     where c.owner = o.owner and c.table_name = o.object_name) as comments,
                   (select t.num_rows from all_tables t
                     where t.owner = o.owner and t.table_name = o.object_name) as num_rows
              from all_objects o
             where o.object_type in (" + string.Join(",", wanted.Select((_, i) => $":t{i}")) + ")");

        var parameters = new Dictionary<string, object>();
        for (var i = 0; i < wanted.Length; i++) parameters[$"t{i}"] = wanted[i];

        // Oracle's own catalogue is enormous, and an integration login can usually see all of it.
        // Excluding the system schemas is the difference between a usable menu and 30,000 rows.
        sql.Append(" and o.owner not in ('SYS','SYSTEM','XDB','MDSYS','CTXSYS','OUTLN','DBSNMP','ORDSYS','APPQOSSYS','WMSYS','LBACSYS','OLAPSYS','AUDSYS','GSMADMIN_INTERNAL','DVSYS','ORDDATA')");

        if (schema != null) { sql.Append(" and o.owner = :owner"); parameters["owner"] = schema; }
        if (like != null) { sql.Append(" and instr(o.object_name, :nameLike) > 0"); parameters["nameLike"] = like; }

        sql.Append(" order by o.owner, o.object_name offset :skip rows fetch next :take rows only");
        parameters["skip"] = request.Skip;
        parameters["take"] = request.Take;

        var objects = new List<DbObject>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = sql.ToString();
            command.CommandTimeout = Options.CommandTimeoutSeconds;
            AddParameters(command, parameters);

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                objects.Add(new DbObject
                {
                    Schema = reader.GetString(0),
                    Name = reader.GetString(1),
                    Type = reader.GetString(2).ToLowerInvariant().Replace(' ', '_'),
                    Comment = reader.IsDBNull(3) ? null : reader.GetString(3),

                    // From the optimiser's statistics, so it is as fresh as the last gather. Stated
                    // as an estimate everywhere it surfaces — the alternative is COUNT(*) on a
                    // stranger's table, which is not a thing a menu should do.
                    RowCount = !request.IncludeRowCounts || reader.IsDBNull(4)
                        ? null
                        : Convert.ToInt64(reader.GetValue(4))
                });
        }

        if (request.IncludeColumns && objects.Count > 0)
            await FillColumnsAsync(connection, objects, cancellationToken);

        return objects;
    }

    async Task FillColumnsAsync(DbConnection connection, List<DbObject> objects,
        CancellationToken cancellationToken)
    {
        // One query for the whole page, not one per object: a page of 200 tables would otherwise be
        // 200 round trips, and against a remote database that is the difference between a screen
        // that opens and one that times out.
        var owners = objects.Select(o => o.Schema).Distinct().ToList();
        var names = objects.Select(o => o.Name).ToList();

        var parameters = new Dictionary<string, object>();
        var ownerList = string.Join(",", owners.Select((o, i) => { parameters[$"o{i}"] = o; return $":o{i}"; }));
        var nameList = string.Join(",", names.Select((n, i) => { parameters[$"n{i}"] = n; return $":n{i}"; }));

        var sql = $@"
            select c.owner, c.table_name, c.column_name, c.data_type, c.nullable,
                   c.data_length, c.data_precision, c.data_scale, c.column_id,
                   case when c.identity_column = 'YES' or c.virtual_column = 'YES' then 1 else 0 end as generated,
                   case when exists (
                        select 1 from all_constraints k
                          join all_cons_columns kc
                            on kc.owner = k.owner and kc.constraint_name = k.constraint_name
                         where k.owner = c.owner and k.table_name = c.table_name
                           and k.constraint_type = 'P' and kc.column_name = c.column_name
                   ) then 1 else 0 end as is_pk
              from all_tab_cols c
             where c.owner in ({ownerList}) and c.table_name in ({nameList})
               and c.hidden_column = 'NO'
             order by c.owner, c.table_name, c.column_id";

        var byObject = objects.ToDictionary(o => $"{o.Schema}.{o.Name}");

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = Options.CommandTimeoutSeconds;
        AddParameters(command, parameters);

        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var key = $"{reader.GetString(0)}.{reader.GetString(1)}";
            if (!byObject.TryGetValue(key, out var target)) continue;

            var dataType = reader.GetString(3);
            target.Columns.Add(new DbColumn
            {
                Name = reader.GetString(2),
                DbType = dataType,
                ClrType = ClrTypeOf(dataType),
                Nullable = reader.GetString(4) == "Y",
                Length = reader.IsDBNull(5) ? null : Convert.ToInt32(reader.GetValue(5)),
                Precision = reader.IsDBNull(6) ? null : Convert.ToInt32(reader.GetValue(6)),
                Scale = reader.IsDBNull(7) ? null : Convert.ToInt32(reader.GetValue(7)),
                Ordinal = reader.IsDBNull(8) ? 0 : Convert.ToInt32(reader.GetValue(8)),
                Generated = Convert.ToInt32(reader.GetValue(9)) == 1,
                PrimaryKey = Convert.ToInt32(reader.GetValue(10)) == 1
            });
        }
    }

    async Task<List<DbObject>> RoutinesAsync(DbConnection connection, DiscoverRequest request,
        string schema, string like, CancellationToken cancellationToken)
    {
        var objectType = request.ObjectType.ToUpperInvariant();

        var parameters = new Dictionary<string, object> { ["objectType"] = objectType };
        var sql = new StringBuilder(@"
            select o.owner, o.object_name, o.object_type
              from all_objects o
             where o.object_type = :objectType
               and o.owner not in ('SYS','SYSTEM','XDB','MDSYS','CTXSYS','OUTLN','DBSNMP','ORDSYS','APPQOSSYS','WMSYS','LBACSYS','AUDSYS')");

        if (schema != null) { sql.Append(" and o.owner = :owner"); parameters["owner"] = schema; }
        if (like != null) { sql.Append(" and instr(o.object_name, :nameLike) > 0"); parameters["nameLike"] = like; }

        sql.Append(" order by o.owner, o.object_name offset :skip rows fetch next :take rows only");
        parameters["skip"] = request.Skip;
        parameters["take"] = request.Take;

        var objects = new List<DbObject>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = sql.ToString();
            command.CommandTimeout = Options.CommandTimeoutSeconds;
            AddParameters(command, parameters);

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                objects.Add(new DbObject
                {
                    Schema = reader.GetString(0),
                    Name = reader.GetString(1),
                    Type = reader.GetString(2).ToLowerInvariant()
                });
        }

        // Parameters always, not behind IncludeColumns: a procedure without its argument list is
        // just a name, and nobody can call it from that.
        foreach (var routine in objects)
            await FillParametersAsync(connection, routine, cancellationToken);

        return objects;
    }

    async Task FillParametersAsync(DbConnection connection, DbObject routine,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = @"
            select nvl(a.argument_name, 'RETURN'), a.data_type, a.in_out, a.position
              from all_arguments a
             where a.owner = :owner and a.object_name = :name
             order by a.position";
        command.CommandTimeout = Options.CommandTimeoutSeconds;
        AddParameters(command, new Dictionary<string, object>
        {
            ["owner"] = routine.Schema,
            ["name"] = routine.Name
        });

        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var dataType = reader.IsDBNull(1) ? "" : reader.GetString(1);
            var inOut = reader.IsDBNull(2) ? "IN" : reader.GetString(2);

            routine.Parameters.Add(new DbRoutineParameter
            {
                Name = reader.GetString(0),
                DbType = dataType,

                // Surfaced as RefCursor rather than Out, because a caller has to declare it
                // differently — this is the field that tells them to.
                Direction = dataType == "REF CURSOR" ? "RefCursor" : DirectionOf(inOut),
                Ordinal = reader.IsDBNull(3) ? 0 : Convert.ToInt32(reader.GetValue(3))
            });
        }
    }

    async Task<List<DbObject>> SequencesAsync(DbConnection connection, DiscoverRequest request,
        string schema, string like, CancellationToken cancellationToken)
    {
        var parameters = new Dictionary<string, object>();
        var sql = new StringBuilder(@"
            select s.sequence_owner, s.sequence_name, s.last_number
              from all_sequences s
             where s.sequence_owner not in ('SYS','SYSTEM','XDB','MDSYS','AUDSYS')");

        if (schema != null) { sql.Append(" and s.sequence_owner = :owner"); parameters["owner"] = schema; }
        if (like != null) { sql.Append(" and instr(s.sequence_name, :nameLike) > 0"); parameters["nameLike"] = like; }

        sql.Append(" order by s.sequence_owner, s.sequence_name offset :skip rows fetch next :take rows only");
        parameters["skip"] = request.Skip;
        parameters["take"] = request.Take;

        var objects = new List<DbObject>();
        using var command = connection.CreateCommand();
        command.CommandText = sql.ToString();
        command.CommandTimeout = Options.CommandTimeoutSeconds;
        AddParameters(command, parameters);

        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            objects.Add(new DbObject
            {
                Schema = reader.GetString(0),
                Name = reader.GetString(1),
                Type = "sequence",

                // The counter's current position, which is the only interesting thing about a
                // sequence and the reason "counters" are worth listing at all.
                RowCount = reader.IsDBNull(2) ? null : Convert.ToInt64(reader.GetValue(2))
            });

        return objects;
    }

    static string DirectionOf(string inOut) => inOut switch
    {
        "OUT" => "Out",
        "IN/OUT" => "InOut",
        _ => "In"
    };

    /// <summary>
    /// What a value of this column arrives as in a result row. Coarse on purpose — it is here so a
    /// mapper author knows whether to expect a string or a number, not to be a type system.
    /// </summary>
    static string ClrTypeOf(string oracleType) => oracleType switch
    {
        "NUMBER" or "FLOAT" or "BINARY_FLOAT" or "BINARY_DOUBLE" => "decimal",
        "DATE" => "DateTime",
        var t when t != null && t.StartsWith("TIMESTAMP", StringComparison.Ordinal) => "DateTime",
        var t when t != null && t.StartsWith("INTERVAL", StringComparison.Ordinal) => "TimeSpan",
        "BLOB" or "RAW" or "LONG RAW" or "BFILE" => "byte[]",
        "CLOB" or "NCLOB" or "LONG" => "string",
        _ => "string"
    };

    void AddParameters(DbCommand command, Dictionary<string, object> parameters)
    {
        foreach (var kv in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = kv.Key;
            parameter.Value = kv.Value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }

        PrepareCommand(command);
    }
}
