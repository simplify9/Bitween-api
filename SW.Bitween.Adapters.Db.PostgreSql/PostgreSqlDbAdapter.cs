using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using SW.Serverless.Sdk;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SW.Bitween.Adapters.Db.PostgreSql;

/// <summary>
/// Bitween's PostgreSQL data source provider.
///
/// Everything generic - the command surface, paging, the statement allow-list, the polling receiver
/// - lives in <see cref="DbResidentAdapterBase"/>. What is here is the part that is genuinely
/// PostgreSQL: the connection string, the system catalogs, and what a role is actually allowed to
/// do.
/// </summary>
// Three roles, one package. "datasource" is what makes it configurable as a connection;
// "receiver" and "handler" are what put it in the pickers a subscription actually chooses from,
// because the same resident instance both polls a table and runs a statement on delivery.
// Declared rather than encoded in the id: reclassifying by rename would break every subscription
// that stores it.
[AdapterKind("datasource")]
[AdapterKind("receiver")]
[AdapterKind("handler")]
public class PostgreSqlDbAdapter(IOptions<PostgreSqlOptions> options, ILogger<PostgreSqlDbAdapter> logger)
    : DbResidentAdapterBase(options.Value, logger)
{
    readonly PostgreSqlOptions _options = options.Value;

    protected override DbProviderFactory Factory => NpgsqlFactory.Instance;

    /// <summary>
    /// Npgsql accepts both <c>@name</c> and <c>:name</c>, and <c>@</c> is the one to standardise on:
    /// <c>:</c> collides with the <c>::</c> cast operator, which is written constantly in PostgreSQL
    /// and would have the placeholder scan reading `value::text` as a parameter called text.
    /// </summary>
    protected override string ParameterPrefix => "@";

    // ------------------------------------------------------------------ connection

    protected override string BuildConnectionString()
    {
        if (string.IsNullOrWhiteSpace(_options.Database))
            throw new InvalidOperationException(
                "Database is required: PostgreSQL connects to one database, not to a server. It is "
                + "the name you would pass to psql -d.");

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = _options.Host,
            Port = _options.Port,
            Database = _options.Database,
            Username = _options.UserName,
            Password = _options.Password ?? "",
            Timeout = _options.ConnectTimeoutSeconds,
            CommandTimeout = _options.CommandTimeoutSeconds,
            ApplicationName = _options.ApplicationName ?? "Bitween",

            // The whole reason this adapter is resident: the pool outlives the message, so a
            // connect, a TLS handshake and an authentication round trip are paid once rather than
            // per Xchange.
            Pooling = true,
            MinPoolSize = Math.Max(0, _options.MinPoolSize),
            MaxPoolSize = Math.Max(1, _options.MaxPoolSize),
            ConnectionIdleLifetime = Math.Max(0, _options.ConnectionIdleLifetimeSeconds),

            // Off unless asked for. Auto-preparation caches a plan per connection, and a plan built
            // against one shape of data can be markedly worse than replanning against another.
            MaxAutoPrepare = _options.AutoPrepare ? 20 : 0
        };

        if (Enum.TryParse<SslMode>(_options.SslMode, ignoreCase: true, out var sslMode))
            builder.SslMode = sslMode;
        else if (!string.IsNullOrWhiteSpace(_options.SslMode))
            throw new InvalidOperationException(
                $"'{_options.SslMode}' is not a PostgreSQL SSL mode. Use one of: "
                + string.Join(", ", Enum.GetNames(typeof(SslMode))) + ".");

        // search_path rather than a schema qualifier on every statement, so an operator can point a
        // data source at a schema without rewriting the SQL they configured.
        if (!string.IsNullOrWhiteSpace(_options.Schema))
            builder.SearchPath = _options.Schema;

        return builder.ToString();
    }

    // ------------------------------------------------------------------ capabilities

    protected override DbCapabilities DescribeEngine() => new()
    {
        Engine = "PostgreSQL",
        SupportedObjects = ["table", "view", "materialized_view", "procedure", "function", "sequence"],

        // CALL, since PostgreSQL 11. Before that everything was a function, which is why the two
        // are listed separately in discovery rather than lumped together as "routines".
        StoredProcedures = true,
        ProcedureOutParameters = true,

        // A PROCEDURE called with CALL cannot hand back a result set the way Oracle's REF CURSOR
        // does. A set-returning FUNCTION can, and it is queried with SELECT - so it goes through
        // Query, not Call. Stated false because that is the honest answer for Call.
        ProcedureResultSets = false,
        MultipleResultSets = true,

        NamedParameters = true,
        Transactions = true,
        IsolationLevels = ["ReadCommitted", "RepeatableRead", "Serializable"],

        // COPY exists and is excellent, but BulkLoad is not implemented for it yet - so this stays
        // false rather than advertising a path that would fall back to row-by-row inserts silently.
        BulkCopy = false,
        Merge = true,
        Returning = true,
        Json = true,
        ArrayTypes = true,

        // LISTEN/NOTIFY is exactly the thing a resident adapter could hold for free, and it is not
        // wired yet. Declared false so the UI says "not available" rather than leaving a gap.
        ChangeNotification = false,
        LogBasedCdc = false,

        SchemaDiscovery = true,
        RowCountEstimates = true,
        ReceiveModes = ["bulk", "incrementing", "timestamp", "timestamp+incrementing", "marker"]
    };

    /// <summary>
    /// MERGE arrived in PostgreSQL 15. Claiming it against 13 or 14 - both still widely run, and
    /// both supported until 2025 and 2026 - would have somebody write a statement the server
    /// cannot parse, and find out on the first message rather than here.
    /// </summary>
    protected override void AdjustForVersion(DbCapabilities described) => Narrow(described);

    /// <summary>
    /// The narrowing itself, reachable without an adapter instance. Proving this needs one server
    /// per version otherwise, and the rule is a comparison rather than anything the database does.
    /// </summary>
    public static void AdjustForVersionForTests(DbCapabilities described) => Narrow(described);

    static void Narrow(DbCapabilities described)
    {
        var major = MajorVersionOf(described.ServerVersion);
        if (major > 0 && major < 15) described.Merge = false;
    }

    /// <summary>
    /// What this ROLE may do, asked of the server rather than assumed. Role attributes and database
    /// privileges are separate things in PostgreSQL and both matter - REPLICATION in particular,
    /// because it is the gate on log-based CDC if that ever becomes a provider.
    /// </summary>
    protected override async Task<List<string>> ProbePrivilegesAsync(DbConnection connection,
        CancellationToken cancellationToken)
    {
        var privileges = new List<string>();

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
                select r.rolsuper, r.rolcreatedb, r.rolcreaterole, r.rolreplication, r.rolbypassrls,
                       has_database_privilege(current_database(), 'CONNECT')  as can_connect,
                       has_database_privilege(current_database(), 'CREATE')   as can_create,
                       has_database_privilege(current_database(), 'TEMPORARY') as can_temp
                  from pg_roles r
                 where r.rolname = current_user";
            command.CommandTimeout = 10;

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                if (reader.GetBoolean(0)) privileges.Add("SUPERUSER");
                if (reader.GetBoolean(1)) privileges.Add("CREATEDB");
                if (reader.GetBoolean(2)) privileges.Add("CREATEROLE");
                if (reader.GetBoolean(3)) privileges.Add("REPLICATION");
                if (reader.GetBoolean(4)) privileges.Add("BYPASSRLS");
                if (reader.GetBoolean(5)) privileges.Add("CONNECT");
                if (reader.GetBoolean(6)) privileges.Add("CREATE");
                if (reader.GetBoolean(7)) privileges.Add("TEMPORARY");
            }
        }
        catch (Exception ex)
        {
            // A role that cannot read pg_roles is unusual but not broken - it just cannot tell us
            // what it can do. Saying so beats failing the connection test over it.
            Logger.LogDebug(ex, "Could not read pg_roles.");
            privileges.Add($"(could not be read: {ex.Message})");
        }

        return privileges;
    }

    protected override IEnumerable<KeyValuePair<string, string>> ExtraStatusDetails()
    {
        yield return new KeyValuePair<string, string>("postgres.database", _options.Database ?? "");
        yield return new KeyValuePair<string, string>("postgres.searchPath", _options.Schema ?? "(server default)");
        yield return new KeyValuePair<string, string>("postgres.sslMode", _options.SslMode ?? "");
    }

    // ------------------------------------------------------------------ discovery

    /// <summary>
    /// Read from the system catalogs rather than from <c>GetSchema</c> or information_schema.
    /// information_schema is standard and slow, and it hides anything the role does not own; pg_class
    /// answers comments, estimated row counts and identity columns in one query per object type.
    /// </summary>
    protected override async Task<List<DbObject>> DiscoverAsync(DbConnection connection,
        DiscoverRequest request, CancellationToken cancellationToken)
    {
        var schema = request.Schema ?? FirstSearchPathSchema();
        var like = request.NameLike?.ToLowerInvariant();

        return request.ObjectType switch
        {
            "procedure" or "function" =>
                await RoutinesAsync(connection, request, schema, like, cancellationToken),
            "sequence" => await SequencesAsync(connection, request, schema, like, cancellationToken),
            _ => await RelationsAsync(connection, request, schema, like, cancellationToken)
        };
    }

    /// <summary>
    /// The search_path can list several schemas; the first is the one an unqualified name resolves
    /// to, so it is the one a discovery call with no schema should mean.
    /// </summary>
    string FirstSearchPathSchema() =>
        string.IsNullOrWhiteSpace(_options.Schema)
            ? null
            : _options.Schema.Split(',').First().Trim();

    async Task<List<DbObject>> RelationsAsync(DbConnection connection, DiscoverRequest request,
        string schema, string like, CancellationToken cancellationToken)
    {
        // relkind: r ordinary table, p partitioned table, v view, m materialised view.
        var kinds = request.ObjectType switch
        {
            "view" => new[] { "v" },
            "materialized_view" => new[] { "m" },
            _ => new[] { "r", "p" }
        };

        var parameters = new Dictionary<string, object> { ["kinds"] = kinds };
        var sql = new StringBuilder(@"
            select n.nspname, c.relname, c.relkind,
                   obj_description(c.oid, 'pg_class') as comment,
                   c.reltuples::bigint                as estimate
              from pg_class c
              join pg_namespace n on n.oid = c.relnamespace
             where c.relkind = any(@kinds)
               and n.nspname not in ('pg_catalog', 'information_schema', 'pg_toast')
               and n.nspname not like 'pg_temp%'");

        if (schema != null) { sql.Append(" and n.nspname = @schema"); parameters["schema"] = schema; }
        if (like != null) { sql.Append(" and position(@nameLike in lower(c.relname)) > 0"); parameters["nameLike"] = like; }

        sql.Append(" order by n.nspname, c.relname offset @skip limit @take");
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
                    Type = TypeOf(reader.GetChar(2)),
                    Comment = reader.IsDBNull(3) ? null : reader.GetString(3),

                    // reltuples, so it is as fresh as the last ANALYZE. It is -1 on a table that
                    // has never been analysed, and that is UNKNOWN rather than empty - clamping it
                    // to zero said "no rows" about a table that may hold millions. Reported as an
                    // estimate everywhere it surfaces; the alternative is COUNT(*) on a stranger's
                    // table, which a menu should not do.
                    RowCount = !request.IncludeRowCounts || reader.IsDBNull(4) || reader.GetInt64(4) < 0
                        ? null
                        : reader.GetInt64(4)
                });
        }

        if (request.IncludeColumns && objects.Count > 0)
            await FillColumnsAsync(connection, objects, cancellationToken);

        return objects;
    }

    static string TypeOf(char relkind) => relkind switch
    {
        'v' => "view",
        'm' => "materialized_view",
        'p' => "table",
        _ => "table"
    };

    async Task FillColumnsAsync(DbConnection connection, List<DbObject> objects,
        CancellationToken cancellationToken)
    {
        // One query for the whole page, not one per object: a page of 200 tables would otherwise be
        // 200 round trips, and against a remote database that is the difference between a screen
        // that opens and one that times out.
        var schemas = objects.Select(o => o.Schema).Distinct().ToArray();
        var names = objects.Select(o => o.Name).Distinct().ToArray();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            select n.nspname, c.relname, a.attname,
                   format_type(a.atttypid, a.atttypmod)          as db_type,
                   not a.attnotnull                              as is_nullable,
                   a.attnum                                      as ordinal,
                   a.attidentity <> ''
                     or a.attgenerated <> ''
                     or d.adbin is not null                      as generated,
                   coalesce(pk.is_pk, false)                     as is_pk,
                   information_schema._pg_char_max_length(a.atttypid, a.atttypmod) as max_length,
                   information_schema._pg_numeric_precision(a.atttypid, a.atttypmod) as numeric_precision,
                   information_schema._pg_numeric_scale(a.atttypid, a.atttypmod)     as numeric_scale
              from pg_attribute a
              join pg_class c     on c.oid = a.attrelid
              join pg_namespace n on n.oid = c.relnamespace
              left join pg_attrdef d on d.adrelid = c.oid and d.adnum = a.attnum
              left join lateral (
                    select true as is_pk
                      from pg_index i
                     where i.indrelid = c.oid and i.indisprimary and a.attnum = any(i.indkey)
              ) pk on true
             where n.nspname = any(@schemas) and c.relname = any(@names)
               and a.attnum > 0 and not a.attisdropped
             order by n.nspname, c.relname, a.attnum";
        command.CommandTimeout = Options.CommandTimeoutSeconds;
        AddParameters(command, new Dictionary<string, object>
        {
            ["schemas"] = schemas,
            ["names"] = names
        });

        var byObject = objects.ToDictionary(o => $"{o.Schema}.{o.Name}");

        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var key = $"{reader.GetString(0)}.{reader.GetString(1)}";
            if (!byObject.TryGetValue(key, out var target)) continue;

            var dbType = reader.GetString(3);
            target.Columns.Add(new DbColumn
            {
                Name = reader.GetString(2),
                DbType = dbType,
                ClrType = ClrTypeOf(dbType),
                Nullable = reader.GetBoolean(4),
                Ordinal = reader.GetInt16(5),
                Generated = reader.GetBoolean(6),
                PrimaryKey = reader.GetBoolean(7),
                Length = reader.IsDBNull(8) ? null : reader.GetInt32(8),
                Precision = reader.IsDBNull(9) ? null : reader.GetInt32(9),
                Scale = reader.IsDBNull(10) ? null : reader.GetInt32(10)
            });
        }
    }

    async Task<List<DbObject>> RoutinesAsync(DbConnection connection, DiscoverRequest request,
        string schema, string like, CancellationToken cancellationToken)
    {
        // prokind: p procedure, f ordinary function, a aggregate, w window. Only the first two are
        // things an integration would call.
        var kind = request.ObjectType == "procedure" ? 'p' : 'f';

        var parameters = new Dictionary<string, object> { ["kind"] = kind };
        var sql = new StringBuilder(@"
            select n.nspname, p.proname, p.prokind,
                   pg_get_function_arguments(p.oid) as arguments,
                   pg_get_function_result(p.oid)    as result,
                   obj_description(p.oid, 'pg_proc') as comment
              from pg_proc p
              join pg_namespace n on n.oid = p.pronamespace
             where p.prokind = @kind
               and n.nspname not in ('pg_catalog', 'information_schema')");

        if (schema != null) { sql.Append(" and n.nspname = @schema"); parameters["schema"] = schema; }
        if (like != null) { sql.Append(" and position(@nameLike in lower(p.proname)) > 0"); parameters["nameLike"] = like; }

        sql.Append(" order by n.nspname, p.proname offset @skip limit @take");
        parameters["skip"] = request.Skip;
        parameters["take"] = request.Take;

        var objects = new List<DbObject>();
        using var command = connection.CreateCommand();
        command.CommandText = sql.ToString();
        command.CommandTimeout = Options.CommandTimeoutSeconds;
        AddParameters(command, parameters);

        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var routine = new DbObject
            {
                Schema = reader.GetString(0),
                Name = reader.GetString(1),
                Type = reader.GetChar(2) == 'p' ? "procedure" : "function",
                Comment = reader.IsDBNull(5) ? null : reader.GetString(5)
            };

            // pg_get_function_arguments returns the signature as PostgreSQL would write it -
            // "p_customer text, OUT total numeric" - which is both the authoritative answer and
            // already in the form somebody would type. Parsed rather than reassembled from
            // pg_proc's parallel arrays, which is where this goes wrong for defaults and variadics.
            foreach (var parsed in ParseArguments(reader.IsDBNull(3) ? "" : reader.GetString(3)))
                routine.Parameters.Add(parsed);

            // A set-returning function is the PostgreSQL answer to Oracle's REF CURSOR, and it is
            // queried with SELECT rather than CALL - worth saying so where a caller will see it.
            var result = reader.IsDBNull(4) ? "" : reader.GetString(4);
            if (result.StartsWith("SETOF ", StringComparison.OrdinalIgnoreCase) ||
                result.StartsWith("TABLE(", StringComparison.OrdinalIgnoreCase))
                routine.Parameters.Add(new DbRoutineParameter
                {
                    Name = "(returns)",
                    DbType = result,
                    Direction = "ReturnValue",
                    Ordinal = routine.Parameters.Count + 1
                });

            objects.Add(routine);
        }

        return objects;
    }

    /// <summary>
    /// Splits the signature PostgreSQL prints, at top-level commas only - a type like
    /// <c>numeric(10,2)</c> carries a comma of its own, and splitting on every one of them produces
    /// two nonsense arguments.
    /// </summary>
    static IEnumerable<DbRoutineParameter> ParseArguments(string arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments)) yield break;

        var depth = 0;
        var start = 0;
        var pieces = new List<string>();

        for (var i = 0; i < arguments.Length; i++)
        {
            if (arguments[i] == '(') depth++;
            else if (arguments[i] == ')') depth--;
            else if (arguments[i] == ',' && depth == 0)
            {
                pieces.Add(arguments.Substring(start, i - start));
                start = i + 1;
            }
        }
        pieces.Add(arguments.Substring(start));

        var ordinal = 0;
        foreach (var piece in pieces)
        {
            var text = piece.Trim();
            if (text.Length == 0) continue;

            var direction = "In";
            foreach (var mode in new[] { "INOUT ", "OUT ", "IN ", "VARIADIC " })
                if (text.StartsWith(mode, StringComparison.OrdinalIgnoreCase))
                {
                    direction = mode.Trim() switch
                    {
                        "INOUT" => "InOut",
                        "OUT" => "Out",
                        "VARIADIC" => "In",
                        _ => "In"
                    };
                    text = text.Substring(mode.Length).Trim();
                    break;
                }

            // A default is documentation here, not something a caller binds.
            var defaultAt = text.IndexOf(" DEFAULT ", StringComparison.OrdinalIgnoreCase);
            if (defaultAt < 0) defaultAt = text.IndexOf(" = ", StringComparison.Ordinal);
            if (defaultAt > 0) text = text.Substring(0, defaultAt).Trim();

            // "name type", or just "type" for an argument declared without a name.
            var space = text.IndexOf(' ');
            yield return new DbRoutineParameter
            {
                Name = space > 0 ? text.Substring(0, space) : $"${ordinal + 1}",
                DbType = space > 0 ? text.Substring(space + 1).Trim() : text,
                Direction = direction,
                Ordinal = ++ordinal
            };
        }
    }

    async Task<List<DbObject>> SequencesAsync(DbConnection connection, DiscoverRequest request,
        string schema, string like, CancellationToken cancellationToken)
    {
        var parameters = new Dictionary<string, object>();
        var sql = new StringBuilder(@"
            select s.schemaname, s.sequencename, s.last_value
              from pg_sequences s
             where s.schemaname not in ('pg_catalog', 'information_schema')");

        if (schema != null) { sql.Append(" and s.schemaname = @schema"); parameters["schema"] = schema; }
        if (like != null) { sql.Append(" and position(@nameLike in lower(s.sequencename)) > 0"); parameters["nameLike"] = like; }

        sql.Append(" order by s.schemaname, s.sequencename offset @skip limit @take");
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

                // Null until the sequence has been used at all, which is a meaningful answer of its
                // own: nothing has drawn from this counter yet.
                RowCount = reader.IsDBNull(2) ? null : reader.GetInt64(2)
            });

        return objects;
    }

    /// <summary>
    /// What a value of this column arrives as in a result row. Coarse on purpose - it tells a mapper
    /// author whether to expect a string or a number, and is not trying to be a type system.
    /// </summary>
    static string ClrTypeOf(string dbType)
    {
        var bare = dbType.Split('(')[0].Trim().ToLowerInvariant();
        if (bare.EndsWith("[]")) return "array";

        return bare switch
        {
            "smallint" or "integer" or "int" or "int2" or "int4" => "int",
            "bigint" or "int8" => "long",
            "numeric" or "decimal" or "money" => "decimal",
            "real" or "double precision" or "float4" or "float8" => "double",
            "boolean" or "bool" => "bool",
            "date" => "DateTime",
            "uuid" => "Guid",
            "bytea" => "byte[]",
            "json" or "jsonb" => "string",
            var t when t.StartsWith("timestamp") => "DateTime",
            var t when t.StartsWith("time") => "TimeSpan",
            var t when t.StartsWith("interval") => "TimeSpan",
            _ => "string"
        };
    }

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
