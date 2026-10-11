using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SW.PrimitiveTypes;
using SW.Serverless.Sdk.Resident;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SW.Bitween.Adapters.Db;

/// <summary>
/// Everything a relational adapter does that is not engine-specific: the command surface, parameter
/// binding, paging, the statement allow-list, the polling receiver, and the counters the heartbeat
/// reports. An engine adds a driver, a connection string, a catalog query and a capability list.
///
/// It is RESIDENT for one reason: the ADO.NET connection pool lives in this process and survives
/// between Xchanges. An ephemeral adapter gets a cold pool per invocation and pays a connect, a TLS
/// handshake and an authentication round trip on every message.
///
/// It is also shared. One instance serves every subscription bound to its data source, concurrently
/// - so nothing here may keep per-message state in a field. Paging cursors and receive batches are
/// keyed and held in concurrent maps for exactly that reason.
/// </summary>
public abstract partial class DbResidentAdapterBase : IResidentAdapter, IInfolinkHandler, IInfolinkReceiver
{
    protected DbResidentAdapterBase(DbOptionsBase options, ILogger logger)
    {
        Options = options;
        Logger = logger;
    }

    protected DbOptionsBase Options { get; }
    protected ILogger Logger { get; }
    protected IAdapterContext Context { get; private set; }

    StatementRegistry statements;
    string connectionString;
    DbCapabilities capabilities;
    CancellationTokenSource stopping;
    Timer cursorSweeper;

    long executed, failed, rowsRead, rowsWritten;
    long totalElapsedMs;
    DateTimeOffset? lastStatementOn;
    string lastError;
    volatile string state = "Starting";

    readonly ConcurrentDictionary<string, OpenCursor> cursors = new();

    // ------------------------------------------------------------------ engine hooks

    /// <summary>The driver's factory. One line in each adapter.</summary>
    protected abstract DbProviderFactory Factory { get; }

    /// <summary>Built from the engine's own settings; never logged.</summary>
    protected abstract string BuildConnectionString();

    /// <summary>What this engine can do, before any probing. Privileges are added on top.</summary>
    protected abstract DbCapabilities DescribeEngine();

    /// <summary>
    /// The catalog query. <see cref="DbConnection.GetSchema(string)"/> covers most of it, but every
    /// engine keeps something worth having outside the common collections.
    /// </summary>
    protected abstract Task<List<DbObject>> DiscoverAsync(DbConnection connection,
        DiscoverRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// What this LOGIN may do, asked of the server. A capability the engine has and the credentials
    /// do not is a capability this data source does not have, and the operator should learn that
    /// from the Test button rather than from a failure at three in the morning.
    /// </summary>
    protected abstract Task<List<string>> ProbePrivilegesAsync(DbConnection connection,
        CancellationToken cancellationToken);

    /// <summary>The marker a parameter is written with in SQL: <c>:</c>, <c>@</c>.</summary>
    protected abstract string ParameterPrefix { get; }

    /// <summary>Anything the driver needs before a command runs - ODP.NET's BindByName, say.</summary>
    protected virtual void PrepareCommand(DbCommand command) { }

    /// <summary>A one-row, no-side-effect query used to prove the connection answers.</summary>
    protected virtual string PingStatement => "select 1";

    // ------------------------------------------------------------------ lifecycle

    public virtual async Task StartAsync(IAdapterContext context, CancellationToken cancellationToken)
    {
        Context = context;
        stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        statements = new StatementRegistry(Options.Statements);
        connectionString = BuildConnectionString();
        capabilities = DescribeEngine();

        // Opened once here rather than lazily on the first message: a data source whose credentials
        // are wrong should say so on its health page immediately, not on whichever unlucky Xchange
        // arrives first.
        try
        {
            using var connection = await OpenAsync(stopping.Token);
            capabilities.ServerVersion = connection.ServerVersion;
            capabilities.Privileges = await ProbePrivilegesAsync(connection, stopping.Token);

            state = "Connected";
            Logger.LogInformation(
                "Connected to {Engine} {Version}; {Statements} statement(s) configured, pool {Min}-{Max}.",
                capabilities.Engine, capabilities.ServerVersion, statements.Count,
                Options.MinPoolSize, Options.MaxPoolSize);
        }
        catch (Exception ex)
        {
            // Not rethrown: the supervisor's restart-and-quarantine loop is a worse answer than a
            // process that stays up reporting Disconnected, because a database that is briefly
            // down is the ordinary case and the pool recovers on its own.
            state = "Disconnected";
            lastError = ex.Message;
            Logger.LogError(ex, "Could not connect at startup. The adapter stays up and will retry per statement.");
        }

        var sweep = TimeSpan.FromSeconds(Math.Max(5, Options.CursorIdleTimeoutSeconds / 2));
        cursorSweeper = new Timer(_ => SweepCursors(), null, sweep, sweep);
    }

    public virtual Task StopAsync(CancellationToken cancellationToken)
    {
        state = "Draining";
        stopping?.Cancel();
        cursorSweeper?.Dispose();

        foreach (var id in cursors.Keys.ToList()) CloseCursorCore(id, "the adapter is shutting down");

        // The pool itself is the driver's, and it goes with the process. Nothing to close by hand,
        // and closing it here would race whatever is still in flight.
        state = "Stopped";
        return Task.CompletedTask;
    }

    public virtual Task<AdapterStatus> GetStatusAsync()
    {
        var status = new AdapterStatus
        {
            Connected = state == "Connected" || state == "Idle",
            State = state == "Connected" && executed == 0 ? "Idle" : state,
            LastMessageOn = lastStatementOn,
            LastError = lastError,
            InFlight = cursors.Count
        };

        status.Details["engine"] = capabilities?.Engine ?? "";
        status.Details["server"] = capabilities?.ServerVersion ?? "";
        status.Details["pool.min"] = Options.MinPoolSize.ToString();
        status.Details["pool.max"] = Options.MaxPoolSize.ToString();
        status.Details["statements.configured"] = (statements?.Count ?? 0).ToString();
        status.Details["statements.executed"] = executed.ToString();
        status.Details["statements.failed"] = failed.ToString();
        status.Details["rows.read"] = rowsRead.ToString();
        status.Details["rows.written"] = rowsWritten.ToString();
        status.Details["latency.meanMs"] =
            executed == 0 ? "0" : (totalElapsedMs / executed).ToString();
        status.Details["cursors.open"] = cursors.Count.ToString();

        foreach (var kv in ExtraStatusDetails()) status.Details[kv.Key] = kv.Value;

        return Task.FromResult(status);
    }

    /// <summary>Engine-specific heartbeat detail - Oracle's session count, say.</summary>
    protected virtual IEnumerable<KeyValuePair<string, string>> ExtraStatusDetails() =>
        Array.Empty<KeyValuePair<string, string>>();

    // ------------------------------------------------------------------ connections

    protected async Task<DbConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = Factory.CreateConnection();
        connection.ConnectionString = connectionString;
        await connection.OpenAsync(cancellationToken);
        await OnConnectionOpenedAsync(connection, cancellationToken);
        return connection;
    }

    /// <summary>
    /// Narrows the declared capability list to what THIS server actually has.
    ///
    /// The list is written per engine and is therefore about the engine at its newest. Called once
    /// the server version is known, which is the only point at which the difference can be told.
    /// Does nothing unless an engine overrides it - most capabilities have been there for a decade.
    /// </summary>
    protected virtual void AdjustForVersion(DbCapabilities described) { }

    /// <summary>
    /// The major version this server reports, or 0 when it cannot be read. Drivers spell it
    /// differently - "16.14", "8.4.11", "16.00.4135" - but all of them lead with the major.
    /// </summary>
    protected static int MajorVersionOf(string serverVersion)
    {
        if (string.IsNullOrWhiteSpace(serverVersion)) return 0;

        var lead = serverVersion.Split('.', ' ')[0];
        return int.TryParse(lead, out var major) ? major : 0;
    }

    /// <summary>
    /// Runs once on every connection this adapter opens, before anything uses it.
    ///
    /// For the settings an engine will not take in a connection string. PostgreSQL puts search_path
    /// there and Oracle takes CURRENT_SCHEMA the same way; SQL Server has neither, because its
    /// default schema belongs to the login rather than to the connection - so it is the one that
    /// needs this.
    ///
    /// A pooled connection carries whatever this did into its next use, which is the point: it is
    /// paid once per physical connection rather than once per message. Anything set here therefore
    /// has to be true for every caller of this data source, not for one of them.
    /// </summary>
    protected virtual Task OnConnectionOpenedAsync(DbConnection connection,
        CancellationToken cancellationToken) => Task.CompletedTask;

    DbCommand CreateCommand(DbConnection connection, string sql, IDictionary<string, object> parameters,
        int? timeoutSeconds)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = timeoutSeconds ?? Options.CommandTimeoutSeconds;
        Bind(command, parameters);
        PrepareCommand(command);
        return command;
    }

    /// <summary>
    /// Values only, always as parameters. A value is never concatenated into the text, whatever it
    /// looks like - that rule and the statement allow-list are the two halves of the same defence.
    /// </summary>
    void Bind(DbCommand command, IDictionary<string, object> parameters)
    {
        if (parameters == null) return;

        foreach (var kv in parameters)
        {
            var parameter = command.CreateParameter();

            // Written with or without the marker, because both look right to whoever configures it.
            parameter.ParameterName = kv.Key.StartsWith(ParameterPrefix, StringComparison.Ordinal)
                ? kv.Key.Substring(ParameterPrefix.Length)
                : kv.Key;

            parameter.Value = Normalise(kv.Value);
            command.Parameters.Add(parameter);
        }

        if (Options.LogParameterValues && Logger.IsEnabled(LogLevel.Debug))
            Logger.LogDebug("Parameters: {Parameters}",
                string.Join(", ", parameters.Select(p => $"{p.Key}={p.Value}")));
    }

    /// <summary>
    /// JSON.NET hands back JValue and JObject; a driver wants a CLR value or DBNull. Anything
    /// structured goes across as its JSON text, which is what a json/jsonb column wants anyway.
    /// </summary>
    static object Normalise(object value) => value switch
    {
        null => DBNull.Value,
        JValue jv => jv.Value ?? DBNull.Value,
        JToken token => token.ToString(Formatting.None),
        _ => value
    };

    // ------------------------------------------------------------------ commands: operations

    /// <summary>
    /// Staged, so a failure names the step. Runs on its OWN connection rather than the pool's warm
    /// one: the point of a test is to prove these settings work from cold, including the login.
    /// </summary>
    public virtual async Task<object> TestConnection()
    {
        var result = new DbTestResult();
        var watch = Stopwatch.StartNew();

        DbConnection connection = null;
        try
        {
            connection = await OpenAsync(CancellationToken.None);
            result.Steps.Add(new DbTestStage
            {
                Step = "connect", Ok = true,
                Detail = $"{connection.DataSource} in {watch.ElapsedMilliseconds} ms"
            });

            result.Steps.Add(new DbTestStage
            {
                Step = "authenticate", Ok = true,
                Detail = $"{connection.ServerVersion}"
            });

            using (var command = CreateCommand(connection, PingStatement, null, 10))
                await command.ExecuteScalarAsync();

            result.Steps.Add(new DbTestStage { Step = "query", Ok = true, Detail = PingStatement });

            var privileges = await ProbePrivilegesAsync(connection, CancellationToken.None);
            result.Steps.Add(new DbTestStage
            {
                Step = "privileges", Ok = true,
                Detail = privileges.Count == 0 ? "none reported" : string.Join(", ", privileges)
            });

            // Every configured statement is PREPARED, not run. That catches a typo, a dropped table
            // and a renamed column now rather than on the first message, and it changes nothing.
            // Every configured statement is PREPARED, not run. That catches a typo, a dropped
            // table and a renamed column now rather than on the first message, and it changes
            // nothing.
            //
            // All of them, not up to the first failure. Stopping early meant fixing one statement
            // only to be told about the next, one connection test at a time - and the second
            // failure is often the same mistake repeated, which is obvious when both are on
            // screen and invisible when they arrive a fix apart.
            var bad = 0;
            foreach (var name in statements.Names)
            {
                var check = await CheckStatementAsync(connection, statements.Resolve(name, null, false));
                if (!check.Ok) bad++;

                result.Steps.Add(new DbTestStage
                {
                    Step = $"statement:{name}", Ok = check.Ok, Detail = check.Detail
                });
            }

            if (bad > 0)
            {
                result.Ok = false;
                result.Details["engine"] = capabilities?.Engine ?? DescribeEngine().Engine;
                result.Details["statementsFailed"] = bad.ToString();
                return result;
            }

            result.Ok = true;
            result.Details["engine"] = capabilities?.Engine ?? DescribeEngine().Engine;
            result.Details["server"] = connection.ServerVersion ?? "";
            return result;
        }
        catch (Exception ex)
        {
            result.Steps.Add(new DbTestStage { Step = "failed", Ok = false, Detail = ex.Message });
            result.Ok = false;
            return result;
        }
        finally
        {
            if (connection != null) { try { await connection.CloseAsync(); } catch { } connection.Dispose(); }
        }
    }

    /// <summary>What this engine and this login can do. Read-only, and safe to relay to the UI.</summary>
    public virtual async Task<object> Describe()
    {
        var described = capabilities ?? DescribeEngine();

        if (described.Privileges.Count == 0)
        {
            try
            {
                using var connection = await OpenAsync(CancellationToken.None);
                described.ServerVersion ??= connection.ServerVersion;
                described.Privileges = await ProbePrivilegesAsync(connection, CancellationToken.None);
            }
            catch (Exception ex)
            {
                // Describing must answer even when the database is down; what it cannot probe it
                // says nothing about rather than failing the whole call.
                described.Details["privilegeProbe"] = $"failed: {ex.Message}";
            }
        }

        // Filled in here rather than declared per engine, so the prefix a caller is told to write
        // and the prefix this adapter actually binds with cannot drift apart.
        described.ParameterPrefix = ParameterPrefix;

        // A chance for an engine to correct what it declared once it knows which VERSION it is
        // talking to. MERGE arrived in PostgreSQL 15 and sequences in SQL Server 2012; a list that
        // says "yes" against an older server is a statement somebody writes and cannot run.
        AdjustForVersion(described);

        described.Details["statements"] = string.Join(", ", statements.Names.OrderBy(n => n));
        described.Details["allowAdHocSql"] = Options.AllowAdHocSql.ToString();
        return described;
    }

    /// <summary>The catalog: what is actually in there. Paged, filtered, and never SELECT COUNT(*).</summary>
    public virtual async Task<object> Discover(DiscoverRequest request)
    {
        request ??= new DiscoverRequest();
        request.Take = Math.Clamp(request.Take <= 0 ? 200 : request.Take, 1, 1000);
        request.Skip = Math.Max(0, request.Skip);
        request.ObjectType = string.IsNullOrWhiteSpace(request.ObjectType) ? "table" : request.ObjectType.ToLowerInvariant();

        using var connection = await OpenAsync(stopping?.Token ?? CancellationToken.None);
        var objects = await DiscoverAsync(connection, request, stopping?.Token ?? CancellationToken.None);

        return new DiscoverResult
        {
            Objects = objects,
            HasMore = objects.Count >= request.Take,
            Applied = new Dictionary<string, string>
            {
                ["objectType"] = request.ObjectType,
                ["schema"] = request.Schema ?? "(all visible)",
                ["nameLike"] = request.NameLike ?? "",
                ["skip"] = request.Skip.ToString(),
                ["take"] = request.Take.ToString()
            }
        };
    }

    public virtual Task<object> GetStats() => Task.FromResult<object>(new
    {
        executed,
        failed,
        rowsRead,
        rowsWritten,
        meanElapsedMs = executed == 0 ? 0 : totalElapsedMs / executed,
        openCursors = cursors.Count,
        statements = statements.Names.OrderBy(n => n).ToArray(),
        lastStatementOn,
        lastError
    });

    // ------------------------------------------------------------------ commands: data

    public virtual async Task<object> Query(StatementRequest request) => await QueryCore(request);

    /// <summary>
    /// <paramref name="adHocAllowed"/> is for SQL that came from the DATA SOURCE rather than from a
    /// message - the receive statement, the mark-processed statement. Those are configuration, so
    /// they are already what the allow-list is protecting; running them through it would mean
    /// forcing AllowAdHocSql on to use the receiver at all.
    /// </summary>
    async Task<QueryResult> QueryCore(StatementRequest request, bool adHocAllowed = false)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        var sql = statements.Resolve(request.Name, request.Sql, Options.AllowAdHocSql || adHocAllowed);
        var maxRows = request.MaxRows > 0 ? request.MaxRows : Options.MaxRows;
        var watch = Stopwatch.StartNew();

        var connection = await OpenAsync(stopping?.Token ?? CancellationToken.None);
        DbDataReader reader = null;
        try
        {
            var command = CreateCommand(connection, sql, request.Parameters, request.TimeoutSeconds);
            reader = await command.ExecuteReaderAsync();

            var result = new QueryResult { Columns = ColumnNames(reader) };
            var carried = await ReadPageAsync(reader, result.Rows, maxRows);

            result.ElapsedMs = watch.ElapsedMilliseconds;
            Succeeded(watch.ElapsedMilliseconds, read: result.Rows.Count);

            if (carried == null)
            {
                await reader.DisposeAsync();
                await connection.DisposeAsync();
                return result;
            }

            // More rows than the ceiling. The connection and the reader stay OPEN behind a cursor
            // id, which is why cursors have an idle timeout: an abandoned one is a pooled
            // connection nobody else can have.
            var id = Guid.NewGuid().ToString("N");
            cursors[id] = new OpenCursor(connection, reader, request.Name ?? "(ad-hoc)") { Carried = carried };
            result.CursorId = id;
            result.HasMore = true;
            return result;
        }
        catch
        {
            Failed();
            if (reader != null) await reader.DisposeAsync();
            await connection.DisposeAsync();
            throw;
        }
    }

    /// <summary>The next page of a query that exceeded MaxRows.</summary>
    public virtual async Task<object> Fetch(FetchRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.CursorId))
            throw new ArgumentException("A cursor id is required.", nameof(request));

        if (!cursors.TryGetValue(request.CursorId, out var cursor))
            throw new InvalidOperationException(
                $"Cursor '{request.CursorId}' is not open. It was either already read to the end, "
                + $"closed, or left idle longer than {Options.CursorIdleTimeoutSeconds} seconds - "
                + "an open cursor holds a pooled connection, so idle ones are reclaimed.");

        var take = request.Take > 0 ? request.Take : Options.MaxRows;
        var result = new QueryResult { Columns = ColumnNames(cursor.Reader), CursorId = request.CursorId };

        cursor.Touch();
        var carried = await ReadPageAsync(cursor.Reader, result.Rows, take, cursor.Carried);
        cursor.Carried = carried;
        Interlocked.Add(ref rowsRead, result.Rows.Count);

        result.HasMore = carried != null;
        if (carried == null)
        {
            CloseCursorCore(request.CursorId, "read to the end");
            result.CursorId = null;
        }

        return result;
    }

    public virtual Task<object> CloseCursor(string cursorId)
    {
        var closed = CloseCursorCore(cursorId, "closed by the caller");
        return Task.FromResult<object>(new { closed });
    }

    public virtual Task<object> Execute(StatementRequest request) => Execute(request, adHocAllowed: false);

    /// <summary>See <see cref="QueryCore"/> for what adHocAllowed means.</summary>
    async Task<object> Execute(StatementRequest request, bool adHocAllowed)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        var sql = statements.Resolve(request.Name, request.Sql, Options.AllowAdHocSql || adHocAllowed);
        var watch = Stopwatch.StartNew();

        using var connection = await OpenAsync(stopping?.Token ?? CancellationToken.None);
        try
        {
            using var command = CreateCommand(connection, sql, request.Parameters, request.TimeoutSeconds);
            var result = new QueryResult();

            // A write with RETURNING / OUTPUT hands back rows, and losing them would make the
            // engine's most useful write form unusable - so read them when they are there.
            if (capabilities != null && capabilities.Returning)
            {
                using var reader = await command.ExecuteReaderAsync();
                result.Columns = ColumnNames(reader);
                if (result.Columns.Count > 0)
                    await ReadPageAsync(reader, result.Rows,
                        request.MaxRows > 0 ? request.MaxRows : Options.MaxRows);

                result.AffectedRows = reader.RecordsAffected < 0 ? result.Rows.Count : reader.RecordsAffected;
            }
            else
            {
                result.AffectedRows = await command.ExecuteNonQueryAsync();
            }

            result.ElapsedMs = watch.ElapsedMilliseconds;
            Succeeded(watch.ElapsedMilliseconds, written: result.AffectedRows);
            return result;
        }
        catch
        {
            Failed();
            throw;
        }
    }

    /// <summary>
    /// A stored procedure or function. Out parameters have to be declared by the caller because
    /// ADO.NET cannot infer them without a round trip the engines do not all support.
    /// </summary>
    public virtual async Task<object> Call(ProcedureRequest request)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        if (capabilities is { StoredProcedures: false })
            throw new NotSupportedException(
                $"{capabilities.Engine} through this adapter does not support stored procedures.");

        var procedure = !string.IsNullOrWhiteSpace(request.Name)
            ? statements.Resolve(request.Name, null, false)
            : Options.AllowAdHocSql
                ? request.Procedure
                : throw new InvalidOperationException(
                    "Name a configured statement holding the procedure name. This data source does "
                    + "not allow one to be sent with the message.");

        var watch = Stopwatch.StartNew();
        using var connection = await OpenAsync(stopping?.Token ?? CancellationToken.None);
        try
        {
            using var command = CreateCommand(connection, procedure, request.Parameters, request.TimeoutSeconds);
            command.CommandType = CommandType.StoredProcedure;

            foreach (var declared in request.OutParameters ?? new List<DbRoutineParameter>())
                AddOutParameter(command, declared);

            var result = new QueryResult();
            using (var reader = await command.ExecuteReaderAsync())
            {
                result.Columns = ColumnNames(reader);
                if (result.Columns.Count > 0)
                    await ReadPageAsync(reader, result.Rows,
                        request.MaxRows > 0 ? request.MaxRows : Options.MaxRows);
            }

            // Read AFTER the reader closes: on most drivers an out parameter is not populated until
            // then, and reading it earlier hands back null with no error to explain it.
            foreach (DbParameter parameter in command.Parameters)
                if (parameter.Direction != ParameterDirection.Input)
                    result.Output[parameter.ParameterName] =
                        parameter.Value == DBNull.Value ? null : parameter.Value;

            result.ElapsedMs = watch.ElapsedMilliseconds;
            Succeeded(watch.ElapsedMilliseconds, read: result.Rows.Count);
            return result;
        }
        catch
        {
            Failed();
            throw;
        }
    }

    /// <summary>
    /// Whether a statement is a bare routine name - <c>release_order</c>, <c>SALES.PKG.RELEASE</c> -
    /// rather than SQL. Identifier characters only, so anything with a space, a parenthesis or a
    /// keyword is treated as SQL and prepared.
    /// </summary>
    static bool IsBareRoutineName(string sql)
    {
        var text = (sql ?? "").Trim();
        return text.Length > 0 &&
               System.Text.RegularExpressions.Regex.IsMatch(
                   text, @"^[A-Za-z_][A-Za-z0-9_$#]*(\.[A-Za-z_][A-Za-z0-9_$#]*){0,2}$");
    }

    /// <summary>
    /// Adds an empty parameter for each placeholder written in the SQL, so a statement can be
    /// prepared without being run. Nothing is bound to a value: this is a syntax and schema check.
    /// </summary>
    /// <summary>
    /// Asks the database whether one piece of SQL is valid, by PREPARING it - parsed and planned,
    /// never run. The check behind both the connection test and the one a statement gets when it
    /// is saved, so the two cannot disagree about what is acceptable.
    /// </summary>
    async Task<(bool Ok, string Detail)> CheckStatementAsync(DbConnection connection, string sql)
    {
        // A statement meant for Call holds a PROCEDURE NAME, not SQL - that is what
        // CommandType.StoredProcedure takes, and on Oracle it is the only form that works.
        // Preparing it as text is a syntax error every time, so the check would fail on a
        // statement that is perfectly correct. Say what was and was not verified instead of
        // quietly passing it.
        if (IsBareRoutineName(sql))
            return (true, "procedure name - existence not checked, it is resolved when called");

        try
        {
            await CheckSyntaxAsync(connection, sql);
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message + WrongPrefixHint(sql));
        }
    }

    /// <summary>
    /// Asks the engine to accept this SQL without running it. Throws when it will not.
    ///
    /// PREPARE is the portable form and is what most drivers want: the server parses the text and
    /// binds every name in it, which is the whole check. It is overridable because one engine
    /// cannot be asked that way - see the SQL Server adapter, whose driver refuses to prepare a
    /// command unless every parameter has been given an explicit type, which is precisely what a
    /// caller checking someone else's SQL does not know.
    /// </summary>
    protected virtual async Task CheckSyntaxAsync(DbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 10;

        // The placeholders have to be declared before Prepare, because some drivers validate that
        // every parameter in the text has been supplied - Npgsql refuses outright - and a check
        // that fell over on every parameterised statement would be worse than none.
        DeclarePlaceholders(command);
        PrepareCommand(command);
        await Task.Run(() => command.Prepare());
    }

    /// <summary>
    /// The one mistake worth naming rather than leaving to a position offset.
    ///
    /// Every engine has its own placeholder character, and the driver reports the other one as a
    /// bare syntax error at a column number - true, and no help at all to someone who copied a
    /// working statement from an Oracle data source into a PostgreSQL one. If the SQL uses the
    /// other convention, say so.
    /// </summary>
    string WrongPrefixHint(string sql)
    {
        var other = ParameterPrefix == ":" ? "@" : ":";
        var escaped = System.Text.RegularExpressions.Regex.Escape(other);

        // Same exclusion as the placeholder scan: `::` is PostgreSQL's cast, not a parameter.
        var pattern = $@"(?<![{escaped}\w]){escaped}([A-Za-z_][A-Za-z0-9_]*)";
        var match = System.Text.RegularExpressions.Regex.Match(sql ?? "", pattern);
        if (!match.Success) return "";

        return $" - this looks like the wrong placeholder for {DescribeEngine().Engine}: it binds "
             + $"parameters as {ParameterPrefix}name, so write {ParameterPrefix}{match.Groups[1].Value} "
             + $"rather than {other}{match.Groups[1].Value}.";
    }

    /// <summary>
    /// Validates one piece of SQL without storing or running it. Called when a statement is saved,
    /// so a typo is refused at the point it was made rather than surfacing later as a failed
    /// connection test or, worse, a failed message.
    /// </summary>
    public virtual async Task<object> ValidateStatement(StatementValidationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.Sql))
            return new StatementValidationResult { Ok = false, Error = "There is no SQL to check." };

        using var connection = await OpenAsync(stopping?.Token ?? CancellationToken.None);
        var check = await CheckStatementAsync(connection, request.Sql);

        return new StatementValidationResult
        {
            Ok = check.Ok,
            Error = check.Ok ? null : check.Detail,
            Note = check.Ok ? check.Detail : null
        };
    }

    void DeclarePlaceholders(DbCommand command)
    {
        var prefix = System.Text.RegularExpressions.Regex.Escape(ParameterPrefix);

        // A doubled prefix is excluded because PostgreSQL writes a cast as `value::text`, and
        // reading that as a parameter named "text" would fail the check on perfectly good SQL.
        var pattern = $"(?<!{prefix}){prefix}(?!{prefix})([A-Za-z_][A-Za-z0-9_]*)";

        foreach (System.Text.RegularExpressions.Match match in
                 System.Text.RegularExpressions.Regex.Matches(command.CommandText, pattern))
        {
            var name = match.Groups[1].Value;
            if (command.Parameters.Contains(name)) continue;

            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = DBNull.Value;
            command.Parameters.Add(parameter);
        }
    }

    /// <summary>
    /// Adds one declared out parameter. Overridden where the driver needs a provider-specific type
    /// - Oracle's REF CURSOR being the case that forces this to exist at all.
    /// </summary>
    protected virtual void AddOutParameter(DbCommand command, DbRoutineParameter declared)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = declared.Name;
        parameter.Direction = string.Equals(declared.Direction, "InOut", StringComparison.OrdinalIgnoreCase)
            ? ParameterDirection.InputOutput
            : ParameterDirection.Output;
        parameter.Size = 4000;
        command.Parameters.Add(parameter);
    }

    /// <summary>Several statements, one transaction, all or nothing.</summary>
    public virtual async Task<object> Batch(BatchRequest request)
    {
        if (request?.Statements == null || request.Statements.Count == 0)
            throw new ArgumentException("A batch needs at least one statement.", nameof(request));

        if (capabilities is { Transactions: false })
            throw new NotSupportedException($"{capabilities.Engine} does not support transactions here.");

        var watch = Stopwatch.StartNew();
        using var connection = await OpenAsync(stopping?.Token ?? CancellationToken.None);
        using var transaction = await connection.BeginTransactionAsync(
            ParseIsolation(request.IsolationLevel), stopping?.Token ?? CancellationToken.None);

        try
        {
            var results = new List<QueryResult>();

            foreach (var statement in request.Statements)
            {
                var sql = statements.Resolve(statement.Name, statement.Sql, Options.AllowAdHocSql);

                using var command = CreateCommand(connection, sql, statement.Parameters,
                    statement.TimeoutSeconds ?? request.TimeoutSeconds);
                command.Transaction = transaction;

                var result = new QueryResult { AffectedRows = await command.ExecuteNonQueryAsync() };
                results.Add(result);
                Interlocked.Add(ref rowsWritten, Math.Max(0, result.AffectedRows));
            }

            await transaction.CommitAsync();
            Succeeded(watch.ElapsedMilliseconds);

            return new { committed = true, results, elapsedMs = watch.ElapsedMilliseconds };
        }
        catch
        {
            // Rollback failing on top of the original failure must not replace it: the first
            // exception is the one that says what went wrong.
            try { await transaction.RollbackAsync(); } catch { }
            Failed();
            throw;
        }
    }

    static IsolationLevel ParseIsolation(string level) =>
        Enum.TryParse<IsolationLevel>(level, ignoreCase: true, out var parsed)
            ? parsed
            : IsolationLevel.Unspecified;

    // ------------------------------------------------------------------ handler role

    /// <summary>
    /// The subscription pipeline's handler and mapper contract.
    ///
    /// The message body is a <see cref="StatementRequest"/> - or, when the subscription names a
    /// statement in its adapter properties, just the parameters. That second form is the one worth
    /// having: the mapper produces a flat object of values and the statement is configuration, so
    /// nothing about the SQL depends on message content.
    /// </summary>
    public virtual async Task<XchangeFile> Handle(XchangeFile xchangeFile)
    {
        var body = xchangeFile?.Data ?? "";
        // ValueOf, not StartupValueOf: this instance is shared by every subscription bound to the
        // data source, so which statement to run is the CALLER's configuration, not the process's.
        // A data source may still set a default for both, which one subscription can override.
        var configured = Context?.ValueOf("Statement");
        var operation = (Context?.ValueOf("Operation") ?? "query").ToLowerInvariant();

        StatementRequest request;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            request = new StatementRequest
            {
                Name = configured,
                Parameters = ParametersFrom(body)
            };
        }
        else
        {
            try
            {
                request = JsonConvert.DeserializeObject<StatementRequest>(body) ?? new StatementRequest();
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(
                    "The message is neither a statement request nor a set of parameters. Either set "
                    + "the Statement adapter property, so the body is read as parameter values, or "
                    + "send {\"name\":\"…\",\"parameters\":{…}}. "
                    + $"The body did not parse: {ex.Message}");
            }
        }

        object result = operation switch
        {
            "execute" => await Execute(request),
            "call" => await Call(new ProcedureRequest
            {
                Name = request.Name,
                Procedure = request.Sql,
                Parameters = request.Parameters,
                TimeoutSeconds = request.TimeoutSeconds,
                MaxRows = request.MaxRows
            }),
            _ => await QueryCore(request)
        };

        return new XchangeFile(JsonConvert.SerializeObject(result), xchangeFile?.Filename);
    }

    static Dictionary<string, object> ParametersFrom(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return new Dictionary<string, object>();

        var token = JToken.Parse(body);
        if (token is not JObject obj)
            throw new InvalidOperationException(
                "With a Statement configured, the message body has to be a JSON object of parameter "
                + $"names to values. This one is a {token.Type}.");

        // A nested object stays as its JSON text - which is what a json column wants, and what a
        // driver would otherwise refuse outright.
        return obj.Properties().ToDictionary(p => p.Name, p => (object)p.Value);
    }

    // ------------------------------------------------------------------ rows

    static List<string> ColumnNames(DbDataReader reader)
    {
        var names = new List<string>(reader.FieldCount);
        for (var i = 0; i < reader.FieldCount; i++) names.Add(reader.GetName(i));
        return names;
    }

    /// <summary>
    /// Fills one page and reports what is behind it.
    ///
    /// Returns the row that PROVES there is more. A reader cannot be rewound, so asking "is there
    /// another row" consumes one - and dropping it loses exactly one row per page, which surfaces
    /// months later as a single missing order and is close to unfindable. So it is handed back and
    /// carried into the next page. Null means the result set ended.
    /// </summary>
    static async Task<Dictionary<string, object>> ReadPageAsync(DbDataReader reader,
        List<Dictionary<string, object>> into, int take, Dictionary<string, object> carried = null)
    {
        if (carried != null) into.Add(carried);

        while (into.Count < take && await reader.ReadAsync())
            into.Add(RowOf(reader));

        if (into.Count < take) return null;

        return await reader.ReadAsync() ? RowOf(reader) : null;
    }

    static Dictionary<string, object> RowOf(DbDataReader reader)
    {
        var row = new Dictionary<string, object>(reader.FieldCount);
        for (var i = 0; i < reader.FieldCount; i++)
        {
            var value = reader.GetValue(i);
            row[reader.GetName(i)] = value == DBNull.Value ? null : value;
        }
        return row;
    }

    // ------------------------------------------------------------------ cursors

    sealed class OpenCursor
    {
        public OpenCursor(DbConnection connection, DbDataReader reader, string statement)
        {
            Connection = connection;
            Reader = reader;
            Statement = statement;
            Touch();
        }

        public DbConnection Connection { get; }
        public DbDataReader Reader { get; }
        public string Statement { get; }
        public DateTimeOffset LastUsed { get; private set; }

        /// <summary>The row already read from the reader to prove there was another page.</summary>
        public Dictionary<string, object> Carried { get; set; }

        public void Touch() => LastUsed = DateTimeOffset.UtcNow;
    }

    bool CloseCursorCore(string cursorId, string why)
    {
        if (cursorId == null || !cursors.TryRemove(cursorId, out var cursor)) return false;

        try { cursor.Reader.Dispose(); } catch { }
        try { cursor.Connection.Dispose(); } catch { }

        Logger.LogDebug("Cursor {CursorId} for {Statement} {Why}.", cursorId, cursor.Statement, why);
        return true;
    }

    void SweepCursors()
    {
        if (Options.CursorIdleTimeoutSeconds <= 0) return;

        var deadline = DateTimeOffset.UtcNow.AddSeconds(-Options.CursorIdleTimeoutSeconds);
        foreach (var kv in cursors.ToArray())
            if (kv.Value.LastUsed < deadline)
            {
                Logger.LogWarning(
                    "Reclaiming cursor {CursorId} for {Statement}: idle past {Timeout}s, and it was "
                    + "holding a pooled connection.", kv.Key, kv.Value.Statement,
                    Options.CursorIdleTimeoutSeconds);

                CloseCursorCore(kv.Key, "idle timeout");
            }
    }

    // ------------------------------------------------------------------ counters

    void Succeeded(long elapsedMs, int read = 0, int written = 0)
    {
        Interlocked.Increment(ref executed);
        Interlocked.Add(ref totalElapsedMs, elapsedMs);
        if (read > 0) Interlocked.Add(ref rowsRead, read);
        if (written > 0) Interlocked.Add(ref rowsWritten, written);

        lastStatementOn = DateTimeOffset.UtcNow;
        lastError = null;
        state = "Connected";

        Context?.Metric("bitween.db.query.duration", elapsedMs,
            new Dictionary<string, string> { ["engine"] = capabilities?.Engine ?? "" });
    }

    void Failed()
    {
        Interlocked.Increment(ref failed);
        Context?.Metric("bitween.db.errors", 1,
            new Dictionary<string, string> { ["engine"] = capabilities?.Engine ?? "" });
    }

    /// <summary>Recorded for the heartbeat. The message only - never the SQL, never the values.</summary>
    protected void RecordError(Exception exception)
    {
        lastError = exception.Message;
        Failed();
    }

    protected StatementRegistry Statements => statements;
    protected DbCapabilities Capabilities => capabilities;
    protected CancellationToken Stopping => stopping?.Token ?? CancellationToken.None;
}
