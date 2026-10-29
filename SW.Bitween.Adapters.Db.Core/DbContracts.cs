using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;

namespace SW.Bitween.Adapters.Db;

// Every contract below is serialised camelCase, and it is declared per type rather than left to the
// host's serializer settings: these objects cross a process boundary as JSON, and the reader on the
// far side is Bitween's UI, which reads what the bus adapters already emit. Those are anonymous
// objects, so they are camelCase by construction - a typed contract defaulting to PascalCase would
// have the data source screen reading `Ok` from one provider and `ok` from the next.

// ---------------------------------------------------------------------------- capabilities

/// <summary>
/// What this engine, through this login, can actually be asked to do.
///
/// Answered offline-ish - most of it is a constant per adapter, the rest is probed once at connect
/// - and cached by the UI, so a screen can grey out what will not work instead of offering it and
/// failing at run time.
///
/// The privilege list matters more than the feature list. "Oracle supports change notification" is
/// worthless if the configured user lacks CHANGE NOTIFICATION, and the operator needs to learn that
/// while they are still on the configuration screen.
/// </summary>
[JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
public class DbCapabilities
{
    public string Engine { get; set; }
    public string ServerVersion { get; set; }

    /// <summary>table, view, materialized_view, procedure, function, package, sequence, synonym.</summary>
    public string[] SupportedObjects { get; set; } = Array.Empty<string>();

    public bool StoredProcedures { get; set; }
    public bool ProcedureOutParameters { get; set; }

    /// <summary>Oracle needs an explicit REF CURSOR out-parameter; most engines just return rows.</summary>
    public bool ProcedureResultSets { get; set; }

    public bool MultipleResultSets { get; set; }
    public bool NamedParameters { get; set; }
    public bool Transactions { get; set; }
    public string[] IsolationLevels { get; set; } = Array.Empty<string>();

    /// <summary>COPY / SqlBulkCopy / LOAD DATA / array binding.</summary>
    public bool BulkCopy { get; set; }

    /// <summary>MERGE, ON CONFLICT, ON DUPLICATE KEY - whatever the engine calls an upsert.</summary>
    public bool Merge { get; set; }

    /// <summary>RETURNING / OUTPUT: can a write hand back the rows it wrote.</summary>
    public bool Returning { get; set; }

    public bool Json { get; set; }
    public bool ArrayTypes { get; set; }

    /// <summary>LISTEN/NOTIFY, CQN, Service Broker. False in v1 for every engine.</summary>
    public bool ChangeNotification { get; set; }

    /// <summary>Log-based CDC. False everywhere: that is a different provider, not a mode of this one.</summary>
    public bool LogBasedCdc { get; set; }

    public bool SchemaDiscovery { get; set; }

    /// <summary>Estimated row counts from the catalog. Never SELECT COUNT(*) on someone's table.</summary>
    public bool RowCountEstimates { get; set; }

    /// <summary>bulk, incrementing, timestamp, timestamp+incrementing, marker.</summary>
    public string[] ReceiveModes { get; set; } = Array.Empty<string>();

    /// <summary>
    /// How this engine writes a bind placeholder - <c>:</c> or <c>@</c> - filled in by the base
    /// from the adapter's own setting rather than declared per engine, so the two cannot disagree.
    ///
    /// Here because a caller that WRITES SQL needs it: the schema browser drafts a statement from
    /// a table or a procedure, and a draft using the wrong prefix is a statement the engine refuses.
    /// </summary>
    public string ParameterPrefix { get; set; }

    /// <summary>
    /// How this engine limits a result to the first N rows. Three shapes, and they are not
    /// interchangeable: <c>limit</c> (PostgreSQL, MySQL) and <c>fetchFirst</c> (Oracle, and SQL
    /// Server 2012+) go after the query, while <c>top</c> (SQL Server's idiom) goes before the
    /// column list.
    ///
    /// Same reason as the prefix: a drafted statement carries a row limit, and the wrong one does
    /// not parse.
    /// </summary>
    public string LimitStyle { get; set; } = "limit";

    /// <summary>Probed with the real credentials - what the engine allows AND this login has.</summary>
    public List<string> Privileges { get; set; } = new();

    /// <summary>Anything else worth showing: NLS settings, edition, connection pool ceiling.</summary>
    public Dictionary<string, string> Details { get; set; } = new();
}

// ---------------------------------------------------------------------------- discovery

[JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
public class DiscoverRequest
{
    /// <summary>table | view | procedure | function | sequence | package. Null means tables.</summary>
    public string ObjectType { get; set; }

    /// <summary>Null means every schema this login can see - which on Oracle is a great many.</summary>
    public string Schema { get; set; }

    /// <summary>Case-insensitive contains. Not a LIKE pattern: no wildcards to get wrong.</summary>
    public string NameLike { get; set; }

    /// <summary>Off by default. Columns for four thousand tables is not a menu, it is a download.</summary>
    public bool IncludeColumns { get; set; }

    /// <summary>Estimates from the catalog only.</summary>
    public bool IncludeRowCounts { get; set; }

    public int Skip { get; set; }
    public int Take { get; set; } = 200;
}

[JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
public class DiscoverResult
{
    public List<DbObject> Objects { get; set; } = new();

    /// <summary>True when the page was full, so a caller knows to ask for the next one.</summary>
    public bool HasMore { get; set; }

    /// <summary>What the request was actually interpreted as, defaults filled in.</summary>
    public Dictionary<string, string> Applied { get; set; } = new();
}

[JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
public class DbObject
{
    public string Schema { get; set; }
    public string Name { get; set; }

    /// <summary>table, view, procedure, …</summary>
    public string Type { get; set; }

    /// <summary>Estimated, and null unless asked for.</summary>
    public long? RowCount { get; set; }

    /// <summary>Empty unless IncludeColumns was set. Empty for a routine - see Parameters.</summary>
    public List<DbColumn> Columns { get; set; } = new();

    /// <summary>Routines only.</summary>
    public List<DbRoutineParameter> Parameters { get; set; } = new();

    public string Comment { get; set; }
}

[JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
public class DbColumn
{
    public string Name { get; set; }

    /// <summary>The engine's own name for it: NUMBER(10,2), VARCHAR2(50), TIMESTAMP WITH TIME ZONE.</summary>
    public string DbType { get; set; }

    /// <summary>What it arrives as in a result row, so a mapper author knows what to expect.</summary>
    public string ClrType { get; set; }

    public bool Nullable { get; set; }
    public int? Length { get; set; }
    public int? Precision { get; set; }
    public int? Scale { get; set; }
    public bool PrimaryKey { get; set; }

    /// <summary>Identity, sequence-defaulted, GENERATED ALWAYS - anything the database fills in.</summary>
    public bool Generated { get; set; }

    public int Ordinal { get; set; }
}

[JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
public class DbRoutineParameter
{
    public string Name { get; set; }
    public string DbType { get; set; }

    /// <summary>In | Out | InOut | ReturnValue | RefCursor.</summary>
    public string Direction { get; set; }

    public int Ordinal { get; set; }
}

[JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
public class StatementValidationRequest
{
    /// <summary>The SQL to check. Prepared, never run, and never stored by this call.</summary>
    public string Sql { get; set; }
}

[JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
public class StatementValidationResult
{
    public bool Ok { get; set; }

    /// <summary>Why it was refused, in the database's own words plus a hint where one applies.</summary>
    public string Error { get; set; }

    /// <summary>
    /// Set when it passed but not everything could be checked - a bare procedure name is accepted
    /// without confirming the routine exists, because it is resolved when called.
    /// </summary>
    public string Note { get; set; }
}

// ---------------------------------------------------------------------------- statements

/// <summary>
/// What the pipeline asks the database to run.
///
/// <see cref="Name"/> is the normal path: statements are configured on the data source and the
/// message supplies parameter values only. <see cref="Sql"/> is refused unless the data source
/// explicitly allows ad-hoc SQL, because a mapper is a template evaluated over message content -
/// if that can emit SQL text, every Xchange is an injection vector into the customer's database.
/// </summary>
[JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
public class StatementRequest
{
    public string Name { get; set; }
    public string Sql { get; set; }
    public Dictionary<string, object> Parameters { get; set; } = new();

    /// <summary>Null uses the data source's default command timeout.</summary>
    public int? TimeoutSeconds { get; set; }

    /// <summary>Null uses the connection default. Only meaningful inside a batch.</summary>
    public string IsolationLevel { get; set; }

    /// <summary>Zero uses the data source's MaxRows. A query that exceeds it fails rather than truncating.</summary>
    public int MaxRows { get; set; }
}

[JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
public class ProcedureRequest
{
    /// <summary>Configured name, or the procedure itself when ad-hoc is allowed.</summary>
    public string Name { get; set; }
    public string Procedure { get; set; }
    public Dictionary<string, object> Parameters { get; set; } = new();

    /// <summary>
    /// Parameters the procedure writes back, by name. Oracle needs the shape declared up front;
    /// this is where a REF CURSOR is named so its rows come back as a result set.
    /// </summary>
    public List<DbRoutineParameter> OutParameters { get; set; } = new();

    public int? TimeoutSeconds { get; set; }
    public int MaxRows { get; set; }
}

[JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
public class BatchRequest
{
    public List<StatementRequest> Statements { get; set; } = new();

    /// <summary>Null uses the connection default.</summary>
    public string IsolationLevel { get; set; }

    public int? TimeoutSeconds { get; set; }
}

[JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
public class QueryResult
{
    public List<Dictionary<string, object>> Rows { get; set; } = new();

    /// <summary>Non-query statements, and the row count of a write inside a batch.</summary>
    public int AffectedRows { get; set; }

    /// <summary>Out and in-out parameter values after a procedure call.</summary>
    public Dictionary<string, object> Output { get; set; } = new();

    /// <summary>Set when there are more rows behind a paging cursor. Pass it back to Fetch.</summary>
    public string CursorId { get; set; }

    public bool HasMore { get; set; }

    /// <summary>Column names in the order the database returned them, even when Rows is empty.</summary>
    public List<string> Columns { get; set; } = new();

    public long ElapsedMs { get; set; }
}

[JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
public class FetchRequest
{
    public string CursorId { get; set; }
    public int Take { get; set; }
}

// ---------------------------------------------------------------------------- test

/// <summary>
/// Staged on purpose. "It did not work" is not something an operator can act on; the failing stage
/// names what to go and fix - a host name, a password, a grant, a missing table.
/// </summary>
[JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
public class DbTestResult
{
    public bool Ok { get; set; }
    public List<DbTestStage> Steps { get; set; } = new();
    public Dictionary<string, string> Details { get; set; } = new();
}

[JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
public class DbTestStage
{
    public string Step { get; set; }
    public bool Ok { get; set; }
    public string Detail { get; set; }
}
