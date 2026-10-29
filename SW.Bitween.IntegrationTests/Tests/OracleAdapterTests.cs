using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using SW.Bitween.Domain.DataSources;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Serverless.Resident;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// The Oracle data source provider, against a real Oracle.
///
/// Nothing here is mocked: a container comes up, a schema is created in it, the adapter is
/// installed from cloud storage and spawned as its own process, and every assertion goes over the
/// resident transport into that process and out to the database. That is the only way to test the
/// parts that actually break - REF CURSOR binding, the data dictionary queries, and a cursor that
/// has to survive the adapter process being restarted underneath it.
///
/// The container and the adapter are started once for the class. Tests are written so they do not
/// depend on each other's leftovers, because xUnit gives no ordering: the ones that write use ids
/// well above the seeded range, and the paging test reads a statement bounded to that range.
/// </summary>
[Collection("Bitween")]
public class OracleAdapterTests : IClassFixture<OracleFixture>
{
    readonly BitweenFixture _fixture;
    readonly OracleFixture _oracle;

    public OracleAdapterTests(BitweenFixture fixture, OracleFixture oracle)
    {
        _fixture = fixture;
        _oracle = oracle;
    }

    // The data source row and the adapter process, created once however many tests run.
    static readonly SemaphoreSlim Gate = new(1, 1);
    static int _dataSourceId;

    IResidentAdapterHost Host => _fixture.App.Services.GetRequiredService<IResidentAdapterHost>();

    /// <summary>
    /// Fetched rather than held: the restart test replaces the process, and a field captured at
    /// construction would point every later test at a handle that is no longer the running one.
    /// </summary>
    async Task<ResidentAdapterInstance> AdapterAsync()
    {
        Skip.If(_oracle.Unavailable != null, $"Oracle is not available here: {_oracle.Unavailable}");

        await Gate.WaitAsync();
        try
        {
            if (_dataSourceId == 0)
            {
                _dataSourceId = await CreateDataSourceAsync();
                await StartAdapterAsync();
            }
        }
        finally
        {
            Gate.Release();
        }

        return Host.Get(BusAdapters.Oracle, _dataSourceId.ToString())
               ?? throw new InvalidOperationException("The Oracle adapter is not running.");
    }

    // ---------------------------------------------------------------- configuration

    /// <summary>
    /// Staged, because "it did not work" is not something an operator can act on. Every configured
    /// statement is PREPARED here too, so a typo or a dropped column is caught on the Test button
    /// rather than by the first message through the subscription.
    /// </summary>
    [SkippableFact]
    public async Task Connection_test_reports_each_stage_and_prepares_every_statement()
    {
        var adapter = await AdapterAsync();
        var result = await adapter.InvokeAsync<JObject>("TestConnection", timeoutSeconds: 120);

        Assert.True(result.Value<bool>("ok"), result.ToString());

        var steps = result["steps"]!.Select(s => s.Value<string>("step")).ToList();
        Assert.Contains("connect", steps);
        Assert.Contains("authenticate", steps);
        Assert.Contains("privileges", steps);

        // Not just "statements were checked": the named ones this data source defines.
        Assert.Contains("statement:recentOrders", steps);
        Assert.Contains("statement:insertOrder", steps);
        Assert.All(result["steps"]!, s => Assert.True(s.Value<bool>("ok"), s.ToString()));
    }

    /// <summary>
    /// What the engine can do AND what this login may do. The second half is the point: a
    /// capability the credentials lack is a capability this data source does not have, and an
    /// operator should see that while they are still on the configuration screen.
    /// </summary>
    [SkippableFact]
    public async Task Describe_reports_the_engine_and_the_login_privileges()
    {
        var adapter = await AdapterAsync();
        var described = await adapter.InvokeAsync<JObject>("Describe", timeoutSeconds: 120);

        Assert.Equal("Oracle", described.Value<string>("engine"));
        Assert.False(string.IsNullOrWhiteSpace(described.Value<string>("serverVersion")));
        Assert.True(described.Value<bool>("storedProcedures"));
        Assert.True(described.Value<bool>("transactions"));

        // Declared false rather than left out, so the UI can say "not available" instead of leaving
        // a gap where an operator has to guess.
        Assert.False(described.Value<bool>("logBasedCdc"));
        Assert.False(described.Value<bool>("changeNotification"));

        var privileges = described["privileges"]!.Select(p => p.Value<string>()).ToList();
        Assert.Contains("CREATE SESSION", privileges);
    }

    /// <summary>
    /// The catalog, which is what the schema browser is fed by. Columns are asked for explicitly
    /// because a page of tables with every column of each is a download, not a menu.
    /// </summary>
    [SkippableFact]
    public async Task Discover_finds_the_table_with_its_columns_and_key()
    {
        var adapter = await AdapterAsync();
        var result = await adapter.InvokeAsync<JObject>("Discover", new
        {
            objectType = "table",
            schema = OracleFixture.User.ToUpperInvariant(),
            nameLike = "BITWEEN",
            includeColumns = true
        }, timeoutSeconds: 120);

        var table = result["objects"]!.Single(o => o.Value<string>("name") == OracleFixture.Table);
        Assert.Equal("table", table.Value<string>("type"));

        var columns = table["columns"]!.ToDictionary(c => c.Value<string>("name")!);
        Assert.Equal(5, columns.Count);

        Assert.True(columns["ID"].Value<bool>("primaryKey"));
        Assert.Equal("NUMBER", columns["ID"].Value<string>("dbType"));
        Assert.Equal("decimal", columns["AMOUNT"].Value<string>("clrType"));
        Assert.Equal("DateTime", columns["CREATED_AT"].Value<string>("clrType"));
        Assert.True(columns["CUSTOMER"].Value<bool>("nullable"));
    }

    /// <summary>A sequence is a counter, and "what is it up to" is the only question worth asking of one.</summary>
    [SkippableFact]
    public async Task Discover_lists_sequences_with_their_current_value()
    {
        var adapter = await AdapterAsync();
        var result = await adapter.InvokeAsync<JObject>("Discover", new
        {
            objectType = "sequence",
            schema = OracleFixture.User.ToUpperInvariant()
        }, timeoutSeconds: 120);

        var sequence = result["objects"]!.Single(o => o.Value<string>("name") == "BITWEEN_ORDER_SEQ");
        Assert.NotNull(sequence.Value<long?>("rowCount"));
    }

    /// <summary>Procedure arguments, with a REF CURSOR called out as one - a caller must bind it differently.</summary>
    [SkippableFact]
    public async Task Discover_reports_a_ref_cursor_argument_as_such()
    {
        var adapter = await AdapterAsync();
        var result = await adapter.InvokeAsync<JObject>("Discover", new
        {
            objectType = "procedure",
            schema = OracleFixture.User.ToUpperInvariant(),
            nameLike = "ORDERS_BY_CUSTOMER"
        }, timeoutSeconds: 120);

        var procedure = result["objects"]!.Single();
        var parameters = procedure["parameters"]!.ToDictionary(p => p.Value<string>("name")!);

        Assert.Equal("In", parameters["P_CUSTOMER"].Value<string>("direction"));
        Assert.Equal("RefCursor", parameters["P_RESULT"].Value<string>("direction"));
    }

    // ---------------------------------------------------------------- statements

    [SkippableFact]
    public async Task A_named_statement_runs_with_bound_parameters()
    {
        var adapter = await AdapterAsync();
        var result = await adapter.InvokeAsync<JObject>("Query", new
        {
            name = "ordersForCustomer",
            parameters = new Dictionary<string, object> { ["customer"] = "acme" }
        }, timeoutSeconds: 120);

        var rows = result["rows"]!.ToList();
        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.Equal("acme", r.Value<string>("CUSTOMER")));
    }

    /// <summary>
    /// The rule the whole statement registry exists for. A mapper is a template evaluated over
    /// message content; if it can emit SQL text then every inbound message is a way to steer a
    /// statement against the customer's database.
    /// </summary>
    [SkippableFact]
    public async Task Ad_hoc_sql_is_refused_when_the_data_source_does_not_allow_it()
    {
        var adapter = await AdapterAsync();

        var error = await Assert.ThrowsAnyAsync<Exception>(() =>
            adapter.InvokeAsync<JObject>("Query", new
            {
                sql = $"select * from {OracleFixture.Table}"
            }, timeoutSeconds: 120));

        Assert.Contains("does not allow ad-hoc SQL", error.Message);
    }

    [SkippableFact]
    public async Task An_unknown_statement_name_says_which_ones_exist()
    {
        var adapter = await AdapterAsync();

        var error = await Assert.ThrowsAnyAsync<Exception>(() =>
            adapter.InvokeAsync<JObject>("Query", new { name = "nope" }, timeoutSeconds: 120));

        Assert.Contains("not a statement this data source defines", error.Message);
        Assert.Contains("recentOrders", error.Message);
    }

    /// <summary>
    /// Paging, and specifically that it loses nothing. Finding out whether there is another page
    /// means reading a row, and a reader cannot be rewound - so that row has to be carried into the
    /// next page rather than dropped. Dropped, it costs exactly one row per page, which surfaces
    /// months later as a single missing order and is close to unfindable.
    /// </summary>
    [SkippableFact]
    public async Task A_query_past_the_row_ceiling_pages_without_losing_a_row()
    {
        var adapter = await AdapterAsync();
        var seen = new List<int>();

        // Bounded to the seeded range, so the tests that insert cannot change the arithmetic here.
        var page = await adapter.InvokeAsync<JObject>("Query", new
        {
            name = "seededOrders",
            maxRows = 7
        }, timeoutSeconds: 120);

        Collect(page, seen);
        Assert.True(page.Value<bool>("hasMore"));

        var cursorId = page.Value<string>("cursorId");
        Assert.False(string.IsNullOrEmpty(cursorId));

        while (!string.IsNullOrEmpty(cursorId))
        {
            var next = await adapter.InvokeAsync<JObject>("Fetch", new { cursorId, take = 7 },
                timeoutSeconds: 120);

            Collect(next, seen);
            cursorId = next.Value<string>("cursorId");
        }

        // Twenty-five seeded rows, in pages of seven, with nothing repeated and nothing missing.
        Assert.Equal(25, seen.Count);
        Assert.Equal(Enumerable.Range(1, 25), seen.OrderBy(i => i));
    }

    static void Collect(JObject page, List<int> into) =>
        into.AddRange(page["rows"]!.Select(r => r.Value<int>("ID")));

    [SkippableFact]
    public async Task A_write_reports_what_it_changed()
    {
        var adapter = await AdapterAsync();

        var result = await adapter.InvokeAsync<JObject>("Execute", new
        {
            name = "insertOrder",
            parameters = new Dictionary<string, object>
            {
                ["id"] = 900,
                ["customer"] = "written-by-test",
                ["amount"] = 12.5
            }
        }, timeoutSeconds: 120);

        Assert.Equal(1, result.Value<int>("affectedRows"));

        var back = await adapter.InvokeAsync<JObject>("Query", new
        {
            name = "ordersForCustomer",
            parameters = new Dictionary<string, object> { ["customer"] = "written-by-test" }
        }, timeoutSeconds: 120);

        Assert.Single(back["rows"]!);
    }

    /// <summary>
    /// The case that forces an Oracle-specific hook to exist at all. An Oracle procedure does not
    /// return rows the way every other engine's does - it returns them through a REF CURSOR out
    /// parameter the caller has to declare, and a plain output parameter gets an ORA-06550 about
    /// argument types that points nowhere near the real problem.
    /// </summary>
    [SkippableFact]
    public async Task A_procedure_returns_rows_through_a_ref_cursor()
    {
        var adapter = await AdapterAsync();

        var result = await adapter.InvokeAsync<JObject>("Call", new
        {
            name = "ordersByCustomerProc",
            parameters = new Dictionary<string, object> { ["p_customer"] = "acme" },
            outParameters = new[] { new { name = "p_result", direction = "RefCursor" } }
        }, timeoutSeconds: 120);

        var rows = result["rows"]!.ToList();
        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.Equal("acme", r.Value<string>("CUSTOMER")));
    }

    /// <summary>All or nothing: a batch whose second statement fails leaves the first undone.</summary>
    [SkippableFact]
    public async Task A_failing_batch_rolls_the_whole_thing_back()
    {
        var adapter = await AdapterAsync();

        await Assert.ThrowsAnyAsync<Exception>(() => adapter.InvokeAsync<JObject>("Batch", new
        {
            statements = new object[]
            {
                new
                {
                    name = "insertOrder",
                    parameters = new Dictionary<string, object>
                        { ["id"] = 950, ["customer"] = "rolled-back", ["amount"] = 1 }
                },
                // The same primary key twice: the second insert violates it, and the first has to
                // go with it.
                new
                {
                    name = "insertOrder",
                    parameters = new Dictionary<string, object>
                        { ["id"] = 950, ["customer"] = "rolled-back", ["amount"] = 1 }
                }
            }
        }, timeoutSeconds: 120));

        var back = await adapter.InvokeAsync<JObject>("Query", new
        {
            name = "ordersForCustomer",
            parameters = new Dictionary<string, object> { ["customer"] = "rolled-back" }
        }, timeoutSeconds: 120);

        Assert.Empty(back["rows"]!);
    }

    // ---------------------------------------------------------------- receiving

    /// <summary>
    /// The receiver end to end, including the part that cannot work without host-held state: the
    /// cursor is written through Bitween, so restarting the adapter process resumes where it got to
    /// rather than replaying from the beginning.
    ///
    /// One test rather than three, because they all consume from the same cursor and xUnit gives no
    /// ordering - split up, they would race each other for the rows.
    /// </summary>
    [SkippableFact]
    public async Task The_receiver_advances_a_cursor_that_survives_a_restart()
    {
        var adapter = await AdapterAsync();

        // The host stamps this on every invocation, and it is what scopes the cursor: one resident
        // instance serves every subscription bound to the data source, so an unscoped cursor would
        // be shared between them.
        var me = new Dictionary<string, string> { ["__subscriptionId__"] = "1" };

        await adapter.InvokeAsync<object>("Initialize", timeoutSeconds: 120, properties: me);
        var first = await adapter.InvokeAsync<List<string>>("ListFiles", timeoutSeconds: 120,
            properties: me);

        // ReceiveBatchSize is five, so a first poll takes five of the twenty-five seeded rows.
        Assert.Equal(5, first.Count);

        foreach (var id in first)
        {
            var file = await adapter.InvokeAsync<JObject>("GetFile", id, timeoutSeconds: 120,
                properties: me);

            // XchangeFile is SW.PrimitiveTypes' type, not one of the contracts this adapter
            // controls, so its casing is whatever that library serialises - read it either way
            // rather than pinning a shape we do not own.
            var data = file.Value<string>("data") ?? file.Value<string>("Data");
            Assert.False(string.IsNullOrWhiteSpace(data));

            // What the pipeline calls once Bitween has durably accepted the row - and therefore the
            // only point at which the cursor may move.
            await adapter.InvokeAsync<object>("DeleteFile", id, timeoutSeconds: 120,
                properties: me);
        }

        await adapter.InvokeAsync<object>("Finalize", timeoutSeconds: 120, properties: me);

        // The cursor is Bitween's row, not the adapter's memory, so it is readable from here.
        var store = _fixture.App.Services.GetRequiredService<IAdapterStateStore>();
        var saved = await store.GetAsync(new AdapterStateKey
        {
            AdapterId = BusAdapters.Oracle,
            InstanceKey = _dataSourceId.ToString(),
            Name = "receive.cursor.1"
        }, default);

        Assert.Equal("5", saved);

        // A new process, same instance key: exactly what the supervisor does after a crash.
        var restarted = await Host.RestartAsync(BusAdapters.Oracle, _dataSourceId.ToString(), drain: false);

        await restarted.InvokeAsync<object>("Initialize", timeoutSeconds: 120, properties: me);
        var second = await restarted.InvokeAsync<List<string>>("ListFiles", timeoutSeconds: 120,
            properties: me);

        var keys = second.Select(id => int.Parse(id.Substring(id.IndexOf(':') + 1))).OrderBy(i => i).ToList();
        Assert.Equal(new[] { 6, 7, 8, 9, 10 }, keys);
    }

    // ---------------------------------------------------------------- validating

    /// <summary>
    /// ODP.NET's Prepare is a client-side no-op - Oracle compiles a statement when it is executed,
    /// not when it is prepared - so the shared check passed EVERYTHING here, including a select
    /// from a table that does not exist. These pin the replacement: DBMS_SQL.PARSE, which compiles
    /// and resolves names without running anything.
    /// </summary>
    [SkippableFact]
    public async Task Valid_sql_passes_validation()
    {
        var adapter = await AdapterAsync();

        var result = await adapter.InvokeAsync<JObject>("ValidateStatement",
            new { sql = $"select id from {OracleFixture.Table} where id = :id" },
            timeoutSeconds: 120);

        Assert.True(result.Value<bool>("ok"), result.Value<string>("error"));
    }

    [SkippableFact]
    public async Task A_dropped_table_is_caught_before_the_statement_is_stored()
    {
        var adapter = await AdapterAsync();

        var result = await adapter.InvokeAsync<JObject>("ValidateStatement",
            new { sql = "select 1 from nothing_of_the_sort" }, timeoutSeconds: 120);

        Assert.False(result.Value<bool>("ok"));

        // ORA-00942: table or view does not exist. Named rather than matched on words, because the
        // message is localised and the number is not.
        Assert.Contains("ORA-00942", result.Value<string>("error"));
    }

    [SkippableFact]
    public async Task A_syntax_error_is_caught()
    {
        var adapter = await AdapterAsync();

        var result = await adapter.InvokeAsync<JObject>("ValidateStatement",
            new { sql = $"select id from {OracleFixture.Table} where id = @id" },
            timeoutSeconds: 120);

        Assert.False(result.Value<bool>("ok"));

        // @ is a database link on Oracle, not a placeholder, so this is ORA-00936 missing
        // expression - and the hint says to write :id instead.
        Assert.Contains(":id", result.Value<string>("error"));
    }

    [SkippableFact]
    public async Task A_bare_procedure_name_is_accepted_and_says_what_was_not_checked()
    {
        var adapter = await AdapterAsync();

        var result = await adapter.InvokeAsync<JObject>("ValidateStatement",
            new { sql = "some_procedure" }, timeoutSeconds: 120);

        Assert.True(result.Value<bool>("ok"));
        Assert.Contains("existence not checked", result.Value<string>("note"));
    }

    // ---------------------------------------------------------------- setup

    async Task<int> CreateDataSourceAsync()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        var dataSource = new DataSource
        {
            Name = $"oracle-{Guid.NewGuid():N}",
            AdapterId = BusAdapters.Oracle,
            Kind = DataSourceKind.Relational,
            Properties = Properties(),
            SecretProperties = ["Password"]
        };

        db.Add(dataSource);
        await db.SaveChangesAsync();
        return dataSource.Id;
    }

    Dictionary<string, string> Properties() => new()
    {
        ["Host"] = _oracle.Host,
        ["Port"] = _oracle.Port.ToString(),
        ["ServiceName"] = OracleFixture.Service,
        ["UserName"] = OracleFixture.User,
        ["Password"] = OracleFixture.Password,
        ["Schema"] = OracleFixture.User.ToUpperInvariant(),
        ["MinPoolSize"] = "1",
        ["MaxPoolSize"] = "5",

        // Statements are configuration. A message names one and supplies values; it never supplies
        // SQL, which is why AllowAdHocSql stays off here and one test proves it.
        ["Statements"] = $@"{{
            ""recentOrders"":         ""select * from {OracleFixture.Table} order by id desc"",
            ""seededOrders"":         ""select * from {OracleFixture.Table} where id <= 25 order by id"",
            ""ordersForCustomer"":    ""select * from {OracleFixture.Table} where customer = :customer order by id"",
            ""insertOrder"":          ""insert into {OracleFixture.Table} (id, customer, amount) values (:id, :customer, :amount)"",
            ""ordersByCustomerProc"": ""ORDERS_BY_CUSTOMER""
        }}",

        ["ReceiveMode"] = "incrementing",
        ["ReceiveStatement"] =
            $"select * from {OracleFixture.Table} where id > :cursor order by id fetch first 5 rows only",
        ["CursorColumn"] = "ID",
        ["KeyColumn"] = "ID",
        ["ReceiveBatchSize"] = "5"
    };

    async Task StartAdapterAsync()
    {
        var spec = new AdapterSpec
        {
            AdapterId = BusAdapters.Oracle,

            // The data source id, exactly as the supervisor keys it - which is what lets the
            // Inspect endpoint find this instance, and what scopes its host-held state.
            InstanceKey = _dataSourceId.ToString()
        };

        foreach (var kv in Properties()) spec.StartupValues[kv.Key] = kv.Value;

        await Host.StartExclusiveAsync(spec);
    }
}
