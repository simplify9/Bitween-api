using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SW.Serverless.Sdk;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SW.Bitween.Adapters.Db.SqlServer;

/// <summary>
/// Bitween's SQL Server data source provider, which serves Azure SQL too.
///
/// Everything generic - the command surface, paging, the statement allow-list, the polling receiver
/// - lives in <see cref="DbResidentAdapterBase"/>. What is here is the part that is genuinely SQL
/// Server: the connection string, the sys catalog views, and what this principal is granted.
/// </summary>
// Three roles, one package. "datasource" is what makes it configurable as a connection;
// "receiver" and "handler" are what put it in the pickers a subscription actually chooses from,
// because the same resident instance both polls a table and runs a statement on delivery.
[AdapterKind("datasource")]
[AdapterKind("receiver")]
[AdapterKind("handler")]
public class SqlServerDbAdapter(IOptions<SqlServerOptions> options, ILogger<SqlServerDbAdapter> logger)
    : DbResidentAdapterBase(options.Value, logger)
{
    readonly SqlServerOptions _options = options.Value;

    protected override DbProviderFactory Factory => SqlClientFactory.Instance;

    protected override string ParameterPrefix => "@";

    // ------------------------------------------------------------------ connection

    protected override string BuildConnectionString()
    {
        if (string.IsNullOrWhiteSpace(_options.Database))
            throw new InvalidOperationException(
                "Database is required: this connects to one database on the server, and it is what "
                + "an unqualified name in a statement resolves against.");

        // A named instance is resolved through the SQL Server Browser, not by port, and supplying
        // both is an error the driver reports obscurely - so the port is only added when the host
        // is not already naming an instance.
        var host = (_options.Host ?? "").Trim();
        var dataSource = host.Contains('\\') || host.Contains(',')
            ? host
            : $"{host},{Math.Max(1, _options.Port)}";

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = dataSource,
            InitialCatalog = _options.Database,
            UserID = _options.UserName,
            Password = _options.Password ?? "",
            ConnectTimeout = Math.Max(1, _options.ConnectTimeoutSeconds),
            CommandTimeout = Math.Max(0, _options.CommandTimeoutSeconds),
            ApplicationName = _options.ApplicationName ?? "Bitween",

            // The whole reason this adapter is resident: the pool outlives the message, so a
            // connect, a TLS handshake and an authentication round trip are paid once rather than
            // per Xchange.
            Pooling = true,
            MinPoolSize = Math.Max(0, _options.MinPoolSize),
            MaxPoolSize = Math.Max(1, _options.MaxPoolSize),

            Encrypt = _options.Encrypt,
            TrustServerCertificate = _options.TrustServerCertificate,
            MultipleActiveResultSets = _options.MultipleActiveResultSets
        };

        return builder.ToString();
    }

    /// <summary>
    /// SQL Server has no search_path: the default schema belongs to the LOGIN, not to the
    /// connection. Where a data source names one, it is applied per connection instead.
    ///
    /// Failure is deliberately not fatal. Impersonating a schema's owner is a privilege many
    /// service accounts do not have, and a connection that works perfectly for qualified names
    /// should not be refused because it could not take a shortcut for unqualified ones.
    /// </summary>
    protected override async Task OnConnectionOpenedAsync(DbConnection connection,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.Schema) && !_options.SnapshotIsolation) return;

        try
        {
            if (_options.SnapshotIsolation)
            {
                using var isolation = connection.CreateCommand();
                isolation.CommandText = "set transaction isolation level snapshot";
                isolation.CommandTimeout = 10;
                await isolation.ExecuteNonQueryAsync(cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(_options.Schema))
            {
                using var command = connection.CreateCommand();

                // The owner of the schema, which is the principal whose default schema it is. Bound
                // as a parameter and then emitted through QUOTENAME, because EXECUTE AS takes a
                // literal and this value comes from configuration.
                command.CommandText = @"
                    declare @owner sysname =
                        (select dp.name
                           from sys.schemas s
                           join sys.database_principals dp on dp.principal_id = s.principal_id
                          where s.name = @schema);
                    if @owner is not null and @owner <> user_name()
                        exec('execute as user = ' + quotename(@owner));";
                command.CommandTimeout = 10;

                var parameter = command.CreateParameter();
                parameter.ParameterName = "schema";
                parameter.Value = _options.Schema;
                command.Parameters.Add(parameter);

                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex,
                "Could not apply the connection defaults for schema {Schema}; unqualified names "
                + "will resolve against the login's own default schema.", _options.Schema);
        }
    }

    // ------------------------------------------------------------------ checking

    /// <summary>
    /// SQL Server is checked with <c>sp_describe_undeclared_parameters</c> rather than by preparing.
    ///
    /// <c>SqlCommand.Prepare</c> refuses unless every parameter has been given an explicit type and
    /// size - which is exactly what a caller checking somebody else's SQL does not know, and
    /// guessing nvarchar(4000) for a parameter compared against an int would make the check answer
    /// a different question from the one asked.
    ///
    /// The procedure parses the batch and binds every name in it, then reports the parameters it
    /// would need - so it catches a syntax error and a missing table, which are the two things this
    /// check exists to catch, and needs nothing supplied.
    /// </summary>
    protected override async Task CheckSyntaxAsync(DbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "sys.sp_describe_undeclared_parameters";
        command.CommandType = CommandType.StoredProcedure;
        command.CommandTimeout = 10;

        var parameter = command.CreateParameter();
        parameter.ParameterName = "@tsql";
        parameter.DbType = DbType.String;
        parameter.Size = -1;
        parameter.Value = sql;
        command.Parameters.Add(parameter);

        // ExecuteReader rather than ExecuteNonQuery: the answer is a result set, and a batch that
        // will not bind throws while producing it.
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) { }
    }

    // ------------------------------------------------------------------ capabilities

    protected override DbCapabilities DescribeEngine() => new()
    {
        Engine = "SQL Server",
        SupportedObjects = ["table", "view", "procedure", "function", "sequence"],

        StoredProcedures = true,
        ProcedureOutParameters = true,

        // A procedure returns result sets by SELECTing, with nothing to declare and nothing to
        // bind - so Call returns rows directly, where PostgreSQL needs a set-returning function
        // queried with SELECT and Oracle needs an explicit REF CURSOR.
        ProcedureResultSets = true,
        MultipleResultSets = true,

        NamedParameters = true,
        Transactions = true,

        // Snapshot as well as the four standard levels, and it is the one worth having here: a
        // long-running read for a polling receiver does not block the writers it is reading from.
        IsolationLevels =
            ["ReadUncommitted", "ReadCommitted", "RepeatableRead", "Serializable", "Snapshot"],

        // SqlBulkCopy exists and is the fastest path there is, but BulkLoad is not implemented for
        // it yet - false rather than advertising a path that would quietly fall back to row-by-row.
        BulkCopy = false,

        // The real thing, not an approximation of one.
        Merge = true,

        // The OUTPUT clause, which does what RETURNING does and does it for MERGE too.
        Returning = true,

        Json = true,

        // Table-valued parameters are the nearest thing and are not an array type; a caller cannot
        // bind a list to one parameter the way PostgreSQL allows.
        ArrayTypes = false,

        // Query Notifications over Service Broker is exactly the thing a resident adapter could
        // hold for free, and it is not wired yet. Declared false so the UI says "not available"
        // rather than leaving a gap. Change Tracking and CDC are the same story.
        ChangeNotification = false,
        LogBasedCdc = false,

        SchemaDiscovery = true,
        RowCountEstimates = true,
        ReceiveModes = ["bulk", "incrementing", "timestamp", "timestamp+incrementing", "marker"],

        // TOP, which goes before the column list rather than after the query. OFFSET/FETCH exists
        // too and is what paging uses, but it requires an ORDER BY - TOP is the form that works on
        // a draft nobody has ordered yet.
        LimitStyle = "top"
    };

    /// <summary>
    /// What this principal may do IN THIS DATABASE, asked of the server rather than assumed.
    /// fn_my_permissions accounts for role membership and for permissions granted at the server
    /// level that reach down, which reading sys.database_permissions directly does not.
    /// </summary>
    protected override async Task<List<string>> ProbePrivilegesAsync(DbConnection connection,
        CancellationToken cancellationToken)
    {
        var privileges = new List<string>();

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                "select permission_name from fn_my_permissions(null, 'DATABASE') order by permission_name";
            command.CommandTimeout = 10;

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                privileges.Add(reader.GetString(0));
        }
        catch (Exception ex)
        {
            // A principal that cannot call fn_my_permissions is unusual but not broken - it just
            // cannot tell us what it can do. Saying so beats failing the connection test over it.
            Logger.LogDebug(ex, "Could not read permissions.");
            privileges.Add($"(could not be read: {ex.Message})");
        }

        return privileges;
    }

    protected override IEnumerable<KeyValuePair<string, string>> ExtraStatusDetails()
    {
        yield return new KeyValuePair<string, string>("sqlserver.database", _options.Database ?? "");
        yield return new KeyValuePair<string, string>("sqlserver.schema", _options.Schema ?? "(login default)");
        yield return new KeyValuePair<string, string>("sqlserver.encrypt", _options.Encrypt.ToString());
        yield return new KeyValuePair<string, string>(
            "sqlserver.trustServerCertificate", _options.TrustServerCertificate.ToString());
    }

    // ------------------------------------------------------------------ discovery

    /// <summary>
    /// The sys catalog views rather than INFORMATION_SCHEMA. The standard views are defined to show
    /// only what the caller has a permission on and cannot report an estimated row count at all;
    /// sys.objects joined to the partition stats answers both in one query.
    /// </summary>
    protected override async Task<List<DbObject>> DiscoverAsync(DbConnection connection,
        DiscoverRequest request, CancellationToken cancellationToken)
    {
        var schema = request.Schema;
        var like = request.NameLike?.ToLowerInvariant();

        return request.ObjectType switch
        {
            "procedure" or "function" =>
                await RoutinesAsync(connection, request, schema, like, cancellationToken),
            "sequence" => await SequencesAsync(connection, request, schema, like, cancellationToken),
            _ => await RelationsAsync(connection, request, schema, like, cancellationToken)
        };
    }

    async Task<List<DbObject>> RelationsAsync(DbConnection connection, DiscoverRequest request,
        string schema, string like, CancellationToken cancellationToken)
    {
        // U user table, V view. Nothing else in sys.objects is a thing an integration reads rows
        // from.
        var wanted = request.ObjectType == "view" ? "V" : "U";

        var parameters = new Dictionary<string, object> { ["wanted"] = wanted };
        var sql = new StringBuilder(@"
            select s.name as [schema], o.name, o.type,
                   cast(ep.value as nvarchar(max)) as comment,
                   (select sum(p.rows) from sys.partitions p
                     where p.object_id = o.object_id and p.index_id in (0, 1)) as estimate
              from sys.objects o
              join sys.schemas s on s.schema_id = o.schema_id
              left join sys.extended_properties ep
                     on ep.major_id = o.object_id and ep.minor_id = 0 and ep.name = 'MS_Description'
             where o.type = @wanted and o.is_ms_shipped = 0");

        if (schema != null) { sql.Append(" and s.name = @schema"); parameters["schema"] = schema; }
        if (like != null) { sql.Append(" and charindex(@nameLike, lower(o.name)) > 0"); parameters["nameLike"] = like; }

        // OFFSET/FETCH needs an ORDER BY, which this has anyway - the list is meant to be stable
        // between pages.
        sql.Append(" order by s.name, o.name offset @skip rows fetch next @take rows only");
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
                    Type = reader.GetString(2).Trim() == "V" ? "view" : "table",
                    Comment = reader.IsDBNull(3) ? null : reader.GetString(3),

                    // The heap or clustered index row count from the partition stats: maintained by
                    // the engine and cheap to read. Reported as an estimate everywhere it surfaces
                    // - the alternative is COUNT(*) on a stranger's table, which a menu should not
                    // do. Null for a view, which has no partitions of its own.
                    RowCount = !request.IncludeRowCounts || reader.IsDBNull(4)
                        ? null
                        : Math.Max(0, reader.GetInt64(4))
                });
        }

        if (request.IncludeColumns && objects.Count > 0)
            await FillColumnsAsync(connection, objects, cancellationToken);

        return objects;
    }

    async Task FillColumnsAsync(DbConnection connection, List<DbObject> objects,
        CancellationToken cancellationToken)
    {
        // One query for the whole page, not one per object: a page of 200 tables would otherwise
        // be 200 round trips, and against a remote database that is the difference between a
        // screen that opens and one that times out.
        var pairs = PairsOf(objects.Select(o => (o.Schema, o.Name)));

        using var command = connection.CreateCommand();
        command.CommandText = $@"
            select s.name as [schema], o.name as [object], c.name as [column],
                   t.name as type_name, c.max_length, c.precision, c.scale,
                   c.is_nullable, c.column_id,
                   c.is_identity | c.is_computed |
                     (case when c.default_object_id <> 0 then 1 else 0 end) as generated,
                   (case when exists (
                        select 1
                          from sys.index_columns ic
                          join sys.indexes i
                            on i.object_id = ic.object_id and i.index_id = ic.index_id
                         where ic.object_id = c.object_id and ic.column_id = c.column_id
                           and i.is_primary_key = 1) then 1 else 0 end) as is_pk
              from sys.columns c
              join sys.objects o on o.object_id = c.object_id
              join sys.schemas s on s.schema_id = o.schema_id
              join sys.types   t on t.user_type_id = c.user_type_id
               and exists (select 1 from {pairs} as w(s, o)
                            where w.s = s.name collate database_default
                              and w.o = o.name collate database_default)
             order by s.name, o.name, c.column_id";
        command.CommandTimeout = Options.CommandTimeoutSeconds;

        var byObject = objects.ToDictionary(o => $"{o.Schema}.{o.Name}");

        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var key = $"{reader.GetString(0)}.{reader.GetString(1)}";
            if (!byObject.TryGetValue(key, out var target)) continue;

            var typeName = reader.GetString(3);

            // max_length is in BYTES, and an nvarchar stores two per character - so the number a
            // schema was declared with is half of it. -1 is the max/blob form.
            var maxLength = reader.GetInt16(4);
            var wide = typeName.StartsWith("n", StringComparison.OrdinalIgnoreCase)
                    && typeName.IndexOf("char", StringComparison.OrdinalIgnoreCase) >= 0;

            target.Columns.Add(new DbColumn
            {
                Name = reader.GetString(2),
                DbType = DescribeType(typeName, maxLength, reader.GetByte(5), reader.GetByte(6), wide),
                ClrType = ClrTypeOf(typeName),
                Nullable = reader.GetBoolean(7),
                Ordinal = reader.GetInt32(8),
                Generated = reader.GetInt32(9) != 0,
                PrimaryKey = reader.GetInt32(10) != 0,
                Length = maxLength < 0 ? null : wide ? maxLength / 2 : maxLength,
                Precision = reader.GetByte(5),
                Scale = reader.GetByte(6)
            });
        }
    }

    /// <summary>The type as a schema would declare it, which is what an operator recognises.</summary>
    static string DescribeType(string typeName, short maxLength, byte precision, byte scale, bool wide)
    {
        var bare = typeName.ToLowerInvariant();

        if (bare is "decimal" or "numeric") return $"{bare}({precision},{scale})";
        if (bare is "datetime2" or "time" or "datetimeoffset") return $"{bare}({scale})";

        if (bare.EndsWith("char") || bare.EndsWith("binary"))
            return maxLength < 0 ? $"{bare}(max)" : $"{bare}({(wide ? maxLength / 2 : maxLength)})";

        return bare;
    }

    async Task<List<DbObject>> RoutinesAsync(DbConnection connection, DiscoverRequest request,
        string schema, string like, CancellationToken cancellationToken)
    {
        // P stored procedure; FN scalar, IF inline table-valued, TF multi-statement table-valued.
        // All three function forms are worth listing - the table-valued ones are what a receive
        // statement would select from.
        var types = request.ObjectType == "procedure"
            ? new[] { "P" }
            : new[] { "FN", "IF", "TF" };

        var parameters = new Dictionary<string, object>();
        var placeholders = new List<string>();
        for (var i = 0; i < types.Length; i++)
        {
            placeholders.Add($"@type{i}");
            parameters[$"type{i}"] = types[i];
        }

        var sql = new StringBuilder($@"
            select s.name as [schema], o.name, o.type,
                   cast(ep.value as nvarchar(max)) as comment
              from sys.objects o
              join sys.schemas s on s.schema_id = o.schema_id
              left join sys.extended_properties ep
                     on ep.major_id = o.object_id and ep.minor_id = 0 and ep.name = 'MS_Description'
             where o.type in ({string.Join(",", placeholders)}) and o.is_ms_shipped = 0");

        if (schema != null) { sql.Append(" and s.name = @schema"); parameters["schema"] = schema; }
        if (like != null) { sql.Append(" and charindex(@nameLike, lower(o.name)) > 0"); parameters["nameLike"] = like; }

        sql.Append(" order by s.name, o.name offset @skip rows fetch next @take rows only");
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
                    Type = reader.GetString(2).Trim() == "P" ? "procedure" : "function",
                    Comment = reader.IsDBNull(3) ? null : reader.GetString(3)
                });
        }

        if (objects.Count > 0) await FillParametersAsync(connection, objects, cancellationToken);
        return objects;
    }

    async Task FillParametersAsync(DbConnection connection, List<DbObject> routines,
        CancellationToken cancellationToken)
    {
        var pairs = PairsOf(routines.Select(r => (r.Schema, r.Name)));

        using var command = connection.CreateCommand();
        command.CommandText = $@"
            select s.name as [schema], o.name as [object], p.name as parameter,
                   t.name as type_name, p.is_output, p.parameter_id, p.max_length, p.precision, p.scale
              from sys.parameters p
              join sys.objects o on o.object_id = p.object_id
              join sys.schemas s on s.schema_id = o.schema_id
              join sys.types   t on t.user_type_id = p.user_type_id
             where exists (select 1 from {pairs} as w(s, o)
                            where w.s = s.name collate database_default
                              and w.o = o.name collate database_default)
             order by s.name, o.name, p.parameter_id";
        command.CommandTimeout = Options.CommandTimeoutSeconds;

        var byRoutine = routines.ToDictionary(r => $"{r.Schema}.{r.Name}");

        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var key = $"{reader.GetString(0)}.{reader.GetString(1)}";
            if (!byRoutine.TryGetValue(key, out var target)) continue;

            // parameter_id 0 is a scalar function's return value, and it has no name.
            var ordinal = reader.GetInt32(5);
            var name = reader.IsDBNull(2) || reader.GetString(2).Length == 0
                ? (ordinal == 0 ? "(returns)" : $"@p{ordinal}")
                : reader.GetString(2).TrimStart('@');

            var typeName = reader.GetString(3);
            var wide = typeName.StartsWith("n", StringComparison.OrdinalIgnoreCase)
                    && typeName.IndexOf("char", StringComparison.OrdinalIgnoreCase) >= 0;

            target.Parameters.Add(new DbRoutineParameter
            {
                Name = name,
                DbType = DescribeType(typeName, reader.GetInt16(6), reader.GetByte(7), reader.GetByte(8), wide),
                Direction = ordinal == 0 ? "ReturnValue" : reader.GetBoolean(4) ? "Out" : "In",
                Ordinal = ordinal
            });
        }
    }

    async Task<List<DbObject>> SequencesAsync(DbConnection connection, DiscoverRequest request,
        string schema, string like, CancellationToken cancellationToken)
    {
        var parameters = new Dictionary<string, object>();
        var sql = new StringBuilder(@"
            select s.name as [schema], q.name, cast(q.current_value as bigint) as current_value
              from sys.sequences q
              join sys.schemas s on s.schema_id = q.schema_id
             where q.is_ms_shipped = 0");

        if (schema != null) { sql.Append(" and s.name = @schema"); parameters["schema"] = schema; }
        if (like != null) { sql.Append(" and charindex(@nameLike, lower(q.name)) > 0"); parameters["nameLike"] = like; }

        sql.Append(" order by s.name, q.name offset @skip rows fetch next @take rows only");
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
    /// The page's (schema, object) pairs as a table to join against.
    ///
    /// SQL Server has no row constructor in IN - <c>where (a, b) in ((..),(..))</c> is PostgreSQL
    /// and MySQL syntax and is a parse error here - so the pairs become a VALUES table and the
    /// filter becomes an EXISTS against it. The collation is forced to the database's own because
    /// a VALUES literal takes the server default, and on an instance whose default differs from
    /// the database's the comparison fails outright rather than merely matching oddly.
    /// </summary>
    static string PairsOf(IEnumerable<(string Schema, string Name)> objects) =>
        "(values " + string.Join(",", objects.Select(o => $"({Quote(o.Schema)},{Quote(o.Name)})")) + ")";

    /// <summary>
    /// An N-prefixed literal for the pairs above. Doubling the quote is SQL Server's own
    /// escape, and these are identifiers this connection just read back from the catalog - quoted
    /// anyway, because "it cannot contain a quote" is the kind of assumption that survives right up
    /// until a table is named oddly.
    /// </summary>
    static string Quote(string value) => "N'" + (value ?? "").Replace("'", "''") + "'";

    /// <summary>
    /// What a value of this column arrives as in a result row. Coarse on purpose - it tells a
    /// mapper author whether to expect a string or a number, and is not trying to be a type system.
    /// </summary>
    static string ClrTypeOf(string typeName) => typeName.ToLowerInvariant() switch
    {
        "bit" => "bool",
        "tinyint" or "smallint" or "int" => "int",
        "bigint" => "long",
        "decimal" or "numeric" or "money" or "smallmoney" => "decimal",
        "float" or "real" => "double",
        "date" or "datetime" or "datetime2" or "smalldatetime" => "DateTime",
        "datetimeoffset" => "DateTimeOffset",
        "time" => "TimeSpan",
        "uniqueidentifier" => "Guid",
        "binary" or "varbinary" or "image" or "timestamp" or "rowversion" => "byte[]",
        "xml" => "string",
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
    }
}
