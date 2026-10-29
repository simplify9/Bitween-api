using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MySqlConnector;
using SW.Serverless.Sdk;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SW.Bitween.Adapters.Db.MySql;

/// <summary>
/// Bitween's MySQL data source provider, which serves MariaDB too.
///
/// Everything generic - the command surface, paging, the statement allow-list, the polling receiver
/// - lives in <see cref="DbResidentAdapterBase"/>. What is here is the part that is genuinely
/// MySQL: the connection string, information_schema, and what this user is actually granted.
///
/// The one structural difference from the other providers is that MySQL has no schema separate
/// from the database. A "schema" filter in discovery therefore means a database, and the default
/// is the one this connection opened rather than every database on the server - a service account
/// on a shared instance can usually see the names of databases it has no business reading.
/// </summary>
// Three roles, one package. "datasource" is what makes it configurable as a connection;
// "receiver" and "handler" are what put it in the pickers a subscription actually chooses from,
// because the same resident instance both polls a table and runs a statement on delivery.
[AdapterKind("datasource")]
[AdapterKind("receiver")]
[AdapterKind("handler")]
public class MySqlDbAdapter(IOptions<MySqlOptions> options, ILogger<MySqlDbAdapter> logger)
    : DbResidentAdapterBase(options.Value, logger)
{
    readonly MySqlOptions _options = options.Value;

    protected override DbProviderFactory Factory => MySqlConnectorFactory.Instance;

    /// <summary>
    /// MySQL binds parameters as <c>@name</c>. <c>?</c> is the wire protocol's own form and is
    /// positional; the driver accepts named placeholders and matches them by name, which is what
    /// every statement in Bitween is written against.
    /// </summary>
    protected override string ParameterPrefix => "@";

    // ------------------------------------------------------------------ connection

    protected override string BuildConnectionString()
    {
        if (string.IsNullOrWhiteSpace(_options.Database))
            throw new InvalidOperationException(
                "Database is required. In MySQL a database is also the schema, so this is what an "
                + "unqualified table name in a statement will resolve against.");

        var builder = new MySqlConnectionStringBuilder
        {
            Server = _options.Host,
            Port = (uint)Math.Max(1, _options.Port),
            Database = _options.Database,
            UserID = _options.UserName,
            Password = _options.Password ?? "",
            ConnectionTimeout = (uint)Math.Max(1, _options.ConnectTimeoutSeconds),
            DefaultCommandTimeout = (uint)Math.Max(0, _options.CommandTimeoutSeconds),
            ApplicationName = _options.ApplicationName ?? "Bitween",

            // The whole reason this adapter is resident: the pool outlives the message, so a
            // connect, a TLS handshake and an authentication round trip are paid once rather than
            // per Xchange.
            Pooling = true,
            MinimumPoolSize = (uint)Math.Max(0, _options.MinPoolSize),
            MaximumPoolSize = (uint)Math.Max(1, _options.MaxPoolSize),
            ConnectionIdleTimeout = (uint)Math.Max(0, _options.ConnectionIdleLifetimeSeconds),

            // A pooled connection MySQL closed at its end is indistinguishable from a healthy one
            // until it is used. This is what turns that into a fresh connection rather than into a
            // failed message - and it matters more here than anywhere, because wait_timeout on a
            // managed instance is routinely minutes.
            ConnectionReset = true,

            AllowZeroDateTime = _options.AllowZeroDateTime,

            // Server-side prepare, unless it is turned off. Also what makes the statement check on
            // save a real check: IgnorePrepare true would have Prepare() do nothing locally and
            // report every statement as valid.
            IgnorePrepare = !_options.ServerPrepare
        };

        if (Enum.TryParse<MySqlSslMode>(_options.SslMode, ignoreCase: true, out var sslMode))
            builder.SslMode = sslMode;
        else if (!string.IsNullOrWhiteSpace(_options.SslMode))
            throw new InvalidOperationException(
                $"'{_options.SslMode}' is not a MySQL SSL mode. Use one of: "
                + string.Join(", ", Enum.GetNames(typeof(MySqlSslMode))) + ".");

        return builder.ToString();
    }

    // ------------------------------------------------------------------ capabilities

    protected override DbCapabilities DescribeEngine() => new()
    {
        Engine = "MySQL",

        // No sequences: MySQL has AUTO_INCREMENT, which belongs to a column rather than being an
        // object of its own. MariaDB does have them, and listing the type here for a server that
        // is not MariaDB would offer a picker that always comes back empty.
        SupportedObjects = ["table", "view", "procedure", "function"],

        StoredProcedures = true,
        ProcedureOutParameters = true,

        // The one place MySQL is more capable than PostgreSQL here: a procedure returns result sets
        // simply by SELECTing, with no cursor to declare and nothing to bind - so Call returns rows
        // directly, where PostgreSQL needs a set-returning function queried with SELECT and Oracle
        // needs an explicit REF CURSOR.
        ProcedureResultSets = true,
        MultipleResultSets = true,

        NamedParameters = true,
        Transactions = true,

        // All four, and READ UNCOMMITTED genuinely does something here - unlike PostgreSQL, where
        // it is accepted and silently treated as READ COMMITTED.
        IsolationLevels = ["ReadUncommitted", "ReadCommitted", "RepeatableRead", "Serializable"],

        // MySqlBulkCopy exists and is fast, but BulkLoad is not implemented for it yet - false
        // rather than advertising a path that would quietly fall back to row-by-row inserts.
        BulkCopy = false,

        // No MERGE statement. The upsert is INSERT ... ON DUPLICATE KEY UPDATE, which is a
        // different thing with different semantics, so this is false rather than approximately true.
        Merge = false,

        // No RETURNING on MySQL. MariaDB has it for INSERT and DELETE; declaring it here would
        // have a statement written against that fail on the majority of servers.
        Returning = false,

        Json = true,

        // No array type. JSON arrays are the usual stand-in, and they arrive as a string.
        ArrayTypes = false,

        ChangeNotification = false,
        LogBasedCdc = false,

        SchemaDiscovery = true,
        RowCountEstimates = true,
        ReceiveModes = ["bulk", "incrementing", "timestamp", "timestamp+incrementing", "marker"]
    };

    /// <summary>
    /// What this user may do, asked of the server rather than assumed. SHOW GRANTS is the only
    /// answer that accounts for roles, wildcards and grants made at every level at once -
    /// information_schema.user_privileges reports the global ones only.
    /// </summary>
    protected override async Task<List<string>> ProbePrivilegesAsync(DbConnection connection,
        CancellationToken cancellationToken)
    {
        var privileges = new List<string>();

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "show grants for current_user()";
            command.CommandTimeout = 10;

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var grant = reader.GetString(0);

                // "GRANT SELECT, INSERT ON `sales`.* TO ..." - the useful half is between GRANT
                // and ON, and the rest is the grantee, which is who we already asked about.
                var on = grant.IndexOf(" ON ", StringComparison.OrdinalIgnoreCase);
                if (!grant.StartsWith("GRANT ", StringComparison.OrdinalIgnoreCase) || on < 0)
                {
                    privileges.Add(grant);
                    continue;
                }

                var what = grant.Substring(6, on - 6).Trim();
                var where = grant.Substring(on + 4).Trim();
                var to = where.IndexOf(" TO ", StringComparison.OrdinalIgnoreCase);
                if (to > 0) where = where.Substring(0, to).Trim();

                privileges.Add($"{what} on {where}");
            }
        }
        catch (Exception ex)
        {
            // A user that cannot run SHOW GRANTS is unusual but not broken - it just cannot tell
            // us what it can do. Saying so beats failing the connection test over it.
            Logger.LogDebug(ex, "Could not read grants.");
            privileges.Add($"(could not be read: {ex.Message})");
        }

        return privileges;
    }

    protected override IEnumerable<KeyValuePair<string, string>> ExtraStatusDetails()
    {
        yield return new KeyValuePair<string, string>("mysql.database", _options.Database ?? "");
        yield return new KeyValuePair<string, string>("mysql.sslMode", _options.SslMode ?? "");
        yield return new KeyValuePair<string, string>("mysql.serverPrepare", _options.ServerPrepare.ToString());
    }

    // ------------------------------------------------------------------ discovery

    /// <summary>
    /// information_schema, which on MySQL is the catalog rather than a slow standard view over one
    /// - there is no lower layer to reach for the way pg_class is under PostgreSQL's.
    /// </summary>
    protected override async Task<List<DbObject>> DiscoverAsync(DbConnection connection,
        DiscoverRequest request, CancellationToken cancellationToken)
    {
        // A schema IS a database here, and the default is the one this connection opened. Listing
        // every database on the server instead would be a longer answer and a worse one: a service
        // account on a shared instance can often see names it has no business reading.
        var schema = request.Schema ?? _options.Database;
        var like = request.NameLike?.ToLowerInvariant();

        return request.ObjectType switch
        {
            "procedure" or "function" =>
                await RoutinesAsync(connection, request, schema, like, cancellationToken),
            _ => await RelationsAsync(connection, request, schema, like, cancellationToken)
        };
    }

    async Task<List<DbObject>> RelationsAsync(DbConnection connection, DiscoverRequest request,
        string schema, string like, CancellationToken cancellationToken)
    {
        var wanted = request.ObjectType == "view" ? "VIEW" : "BASE TABLE";

        var parameters = new Dictionary<string, object> { ["wanted"] = wanted };
        var sql = new StringBuilder(@"
            select t.table_schema, t.table_name, t.table_type, t.table_comment, t.table_rows
              from information_schema.tables t
             where t.table_type = @wanted
               and t.table_schema not in ('information_schema', 'mysql', 'performance_schema', 'sys')");

        if (schema != null) { sql.Append(" and t.table_schema = @schema"); parameters["schema"] = schema; }
        if (like != null) { sql.Append(" and locate(@nameLike, lower(t.table_name)) > 0"); parameters["nameLike"] = like; }

        sql.Append(" order by t.table_schema, t.table_name limit @take offset @skip");
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
                    Type = string.Equals(reader.GetString(2), "VIEW", StringComparison.OrdinalIgnoreCase)
                        ? "view"
                        : "table",
                    Comment = reader.IsDBNull(3) || reader.GetString(3).Length == 0
                        ? null
                        : reader.GetString(3),

                    // table_rows is InnoDB's estimate from the index statistics and can be out by
                    // a wide margin on a table that has not been analysed. Reported as an estimate
                    // everywhere it surfaces - the alternative is COUNT(*) on a stranger's table,
                    // which a menu should not do. Always null for a view, which has no rows of its
                    // own to estimate.
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
        //
        // MySqlConnector has no array parameter, so the pair of IN lists is built from the page's
        // own values. They are identifiers this connection just read back from the catalog, not
        // anything a caller supplied - but they are quoted anyway, because "it cannot contain a
        // quote" is the kind of assumption that survives right up until a table is named oddly.
        var schemas = string.Join(",", objects.Select(o => Quote(o.Schema)).Distinct());
        var names = string.Join(",", objects.Select(o => Quote(o.Name)).Distinct());

        using var command = connection.CreateCommand();
        command.CommandText = $@"
            select c.table_schema, c.table_name, c.column_name, c.column_type,
                   c.is_nullable, c.ordinal_position, c.extra, c.column_key,
                   c.character_maximum_length, c.numeric_precision, c.numeric_scale
              from information_schema.columns c
             where c.table_schema in ({schemas}) and c.table_name in ({names})
             order by c.table_schema, c.table_name, c.ordinal_position";
        command.CommandTimeout = Options.CommandTimeoutSeconds;

        var byObject = objects.ToDictionary(o => $"{o.Schema}.{o.Name}");

        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var key = $"{reader.GetString(0)}.{reader.GetString(1)}";
            if (!byObject.TryGetValue(key, out var target)) continue;

            var dbType = reader.GetString(3);
            var extra = reader.IsDBNull(6) ? "" : reader.GetString(6);

            target.Columns.Add(new DbColumn
            {
                Name = reader.GetString(2),
                DbType = dbType,
                ClrType = ClrTypeOf(dbType),
                Nullable = string.Equals(reader.GetString(4), "YES", StringComparison.OrdinalIgnoreCase),
                Ordinal = (int)reader.GetInt64(5),

                // auto_increment, a generated column, and DEFAULT_GENERATED - which is what a
                // CURRENT_TIMESTAMP default reports as. All three are values the database fills in,
                // which is the only distinction an insert cares about.
                Generated = extra.IndexOf("auto_increment", StringComparison.OrdinalIgnoreCase) >= 0
                         || extra.IndexOf("GENERATED", StringComparison.OrdinalIgnoreCase) >= 0,

                // PRI on every column of the key, including each column of a composite one.
                PrimaryKey = !reader.IsDBNull(7)
                          && string.Equals(reader.GetString(7), "PRI", StringComparison.OrdinalIgnoreCase),

                Length = reader.IsDBNull(8) ? null : (int?)reader.GetInt64(8),
                Precision = reader.IsDBNull(9) ? null : (int?)reader.GetInt64(9),
                Scale = reader.IsDBNull(10) ? null : (int?)reader.GetInt64(10)
            });
        }
    }

    async Task<List<DbObject>> RoutinesAsync(DbConnection connection, DiscoverRequest request,
        string schema, string like, CancellationToken cancellationToken)
    {
        var wanted = request.ObjectType == "procedure" ? "PROCEDURE" : "FUNCTION";

        var parameters = new Dictionary<string, object> { ["wanted"] = wanted };
        var sql = new StringBuilder(@"
            select r.routine_schema, r.routine_name, r.routine_type, r.routine_comment, r.dtd_identifier
              from information_schema.routines r
             where r.routine_type = @wanted
               and r.routine_schema not in ('information_schema', 'mysql', 'performance_schema', 'sys')");

        if (schema != null) { sql.Append(" and r.routine_schema = @schema"); parameters["schema"] = schema; }
        if (like != null) { sql.Append(" and locate(@nameLike, lower(r.routine_name)) > 0"); parameters["nameLike"] = like; }

        sql.Append(" order by r.routine_schema, r.routine_name limit @take offset @skip");
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
            {
                var routine = new DbObject
                {
                    Schema = reader.GetString(0),
                    Name = reader.GetString(1),
                    Type = string.Equals(reader.GetString(2), "PROCEDURE", StringComparison.OrdinalIgnoreCase)
                        ? "procedure"
                        : "function",
                    Comment = reader.IsDBNull(3) || reader.GetString(3).Length == 0
                        ? null
                        : reader.GetString(3)
                };

                // dtd_identifier is the return type, and only a function has one.
                if (routine.Type == "function" && !reader.IsDBNull(4))
                    routine.Parameters.Add(new DbRoutineParameter
                    {
                        Name = "(returns)",
                        DbType = reader.GetString(4),
                        Direction = "ReturnValue",
                        Ordinal = 0
                    });

                objects.Add(routine);
            }
        }

        if (objects.Count > 0) await FillParametersAsync(connection, objects, cancellationToken);
        return objects;
    }

    async Task FillParametersAsync(DbConnection connection, List<DbObject> routines,
        CancellationToken cancellationToken)
    {
        // information_schema.parameters, one query for the page. The row with ordinal_position 0
        // is a function's return value, which RoutinesAsync has already recorded from
        // dtd_identifier - so it is skipped here rather than listed twice.
        var schemas = string.Join(",", routines.Select(r => Quote(r.Schema)).Distinct());
        var names = string.Join(",", routines.Select(r => Quote(r.Name)).Distinct());

        using var command = connection.CreateCommand();
        command.CommandText = $@"
            select p.specific_schema, p.specific_name, p.parameter_name,
                   p.dtd_identifier, p.parameter_mode, p.ordinal_position
              from information_schema.parameters p
             where p.specific_schema in ({schemas}) and p.specific_name in ({names})
               and p.ordinal_position > 0
             order by p.specific_schema, p.specific_name, p.ordinal_position";
        command.CommandTimeout = Options.CommandTimeoutSeconds;

        var byRoutine = routines.ToDictionary(r => $"{r.Schema}.{r.Name}");

        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var key = $"{reader.GetString(0)}.{reader.GetString(1)}";
            if (!byRoutine.TryGetValue(key, out var target)) continue;

            var mode = reader.IsDBNull(4) ? "IN" : reader.GetString(4);
            target.Parameters.Add(new DbRoutineParameter
            {
                Name = reader.IsDBNull(2) ? $"p{reader.GetInt32(5)}" : reader.GetString(2),
                DbType = reader.IsDBNull(3) ? "" : reader.GetString(3),
                Direction = mode.ToUpperInvariant() switch
                {
                    "OUT" => "Out",
                    "INOUT" => "InOut",
                    _ => "In"
                },
                Ordinal = reader.GetInt32(5)
            });
        }
    }

    /// <summary>
    /// A single-quoted literal for the IN lists above. Doubling the quote is MySQL's own escape and
    /// is what the driver would do for a parameter.
    /// </summary>
    static string Quote(string value) => "'" + (value ?? "").Replace("'", "''").Replace("\\", "\\\\") + "'";

    /// <summary>
    /// What a value of this column arrives as in a result row. Coarse on purpose - it tells a
    /// mapper author whether to expect a string or a number, and is not trying to be a type system.
    /// </summary>
    static string ClrTypeOf(string dbType)
    {
        var bare = dbType.Split('(')[0].Trim().ToLowerInvariant();
        var unsigned = dbType.IndexOf("unsigned", StringComparison.OrdinalIgnoreCase) >= 0;

        return bare switch
        {
            // tinyint(1) is how MySQL stores a boolean - there is no separate type - and the
            // driver hands it back as one, so saying "int" here would mislead a mapper author.
            "tinyint" when dbType.StartsWith("tinyint(1)", StringComparison.OrdinalIgnoreCase) => "bool",
            "bool" or "boolean" => "bool",
            "tinyint" or "smallint" or "mediumint" or "int" or "integer" => unsigned ? "long" : "int",
            "bigint" => unsigned ? "decimal" : "long",
            "decimal" or "numeric" => "decimal",
            "float" or "double" or "real" => "double",
            "date" or "datetime" or "timestamp" => "DateTime",
            "time" => "TimeSpan",
            "year" => "int",
            "bit" => "bool",
            "json" => "string",
            "binary" or "varbinary" or "tinyblob" or "blob" or "mediumblob" or "longblob" => "byte[]",
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
    }
}
