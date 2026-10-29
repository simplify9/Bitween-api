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
/// The SQL Server data source provider, against a real SQL Server.
///
/// The same suite as the others, which is the point: four engines run on one core, so the parts
/// that are shared should behave identically and the parts that differ should differ VISIBLY.
/// SQL Server is the one where the shared statement CHECK could not be shared - its driver refuses
/// to prepare a command whose parameters have no explicit type - so the check has its own tests
/// here rather than relying on the core's.
/// </summary>
[Collection("Bitween")]
public class SqlServerAdapterTests : IClassFixture<SqlServerDbFixture>
{
    readonly BitweenFixture _fixture;
    readonly SqlServerDbFixture _sqlServer;

    public SqlServerAdapterTests(BitweenFixture fixture, SqlServerDbFixture sqlServer)
    {
        _fixture = fixture;
        _sqlServer = sqlServer;
    }

    static readonly SemaphoreSlim Gate = new(1, 1);
    static int _dataSourceId;

    IResidentAdapterHost Host => _fixture.App.Services.GetRequiredService<IResidentAdapterHost>();

    async Task<ResidentAdapterInstance> AdapterAsync()
    {
        Skip.If(_sqlServer.Unavailable != null, $"SQL Server is not available: {_sqlServer.Unavailable}");

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

        return Host.Get(BusAdapters.SqlServer, _dataSourceId.ToString())
               ?? throw new InvalidOperationException("The SQL Server adapter is not running.");
    }

    // ---------------------------------------------------------------- configuration

    [SkippableFact]
    public async Task Connection_test_reports_each_stage_and_checks_every_statement()
    {
        var adapter = await AdapterAsync();
        var result = await adapter.InvokeAsync<JObject>("TestConnection", timeoutSeconds: 60);

        Assert.True(result.Value<bool>("ok"), result.ToString());

        var steps = result["steps"]!.Select(s => s.Value<string>("step")).ToList();
        Assert.Contains("connect", steps);
        Assert.Contains("authenticate", steps);
        Assert.Contains("privileges", steps);
        Assert.Contains("statement:ordersForCustomer", steps);
        Assert.All(result["steps"]!, s => Assert.True(s.Value<bool>("ok"), s.ToString()));
    }

    /// <summary>
    /// SQL Server is the most capable of the four on paper, and the list says so where it is true -
    /// MERGE, OUTPUT and snapshot isolation are all real here - and stays false where the thing
    /// exists but is not wired, which is Service Broker's query notifications.
    /// </summary>
    [SkippableFact]
    public async Task Describe_reports_the_engine_and_where_it_differs()
    {
        var adapter = await AdapterAsync();
        var described = await adapter.InvokeAsync<JObject>("Describe", timeoutSeconds: 60);

        Assert.Equal("SQL Server", described.Value<string>("engine"));
        Assert.False(string.IsNullOrWhiteSpace(described.Value<string>("serverVersion")));

        Assert.True(described.Value<bool>("storedProcedures"));
        Assert.True(described.Value<bool>("procedureResultSets"));
        Assert.True(described.Value<bool>("merge"));
        Assert.True(described.Value<bool>("returning"));

        // Table-valued parameters are not an array type: a caller cannot bind a list to one
        // parameter the way PostgreSQL allows.
        Assert.False(described.Value<bool>("arrayTypes"));

        var objects = described["supportedObjects"]!.Select(o => o.Value<string>()).ToList();
        Assert.Contains("sequence", objects);

        var isolation = described["isolationLevels"]!.Select(i => i.Value<string>()).ToList();
        Assert.Contains("Snapshot", isolation);

        // Exists, not wired. Declared false so the UI says "not available" rather than leaving a gap.
        Assert.False(described.Value<bool>("changeNotification"));
        Assert.False(described.Value<bool>("logBasedCdc"));

        Assert.NotEmpty(described["privileges"]!);
    }

    // ---------------------------------------------------------------- discovery

    [SkippableFact]
    public async Task Discover_finds_the_table_with_its_columns_and_key()
    {
        var adapter = await AdapterAsync();
        var result = await adapter.InvokeAsync<JObject>("Discover", new
        {
            objectType = "table",
            schema = "sales",
            nameLike = SqlServerDbFixture.Table,
            includeColumns = true,
            includeRowCounts = true
        }, timeoutSeconds: 60);

        var table = result["objects"]!.Single(o => o.Value<string>("name") == SqlServerDbFixture.Table);

        Assert.Equal("sales", table.Value<string>("schema"));

        // An extended property, which is where SQL Server keeps what other engines call a comment.
        Assert.Equal("Customer orders", table.Value<string>("comment"));

        // The partition stats, not COUNT(*) - so this is an estimate, and asserting an exact
        // number would be asserting something the adapter deliberately does not promise. Other
        // tests in this class insert rows too, which is the second reason: 25 is the floor.
        Assert.True(table.Value<long>("rowCount") >= 25,
            $"expected at least the 25 seeded rows, got {table.Value<long>("rowCount")}");

        var columns = table["columns"]!.ToList();
        Assert.Equal("id", columns[0].Value<string>("name"));
        Assert.True(columns[0].Value<bool>("primaryKey"));
        Assert.False(columns[0].Value<bool>("nullable"));

        // max_length is in BYTES and an nvarchar stores two per character, so the declared length
        // is half of it - 50, not 100.
        var customer = columns.Single(c => c.Value<string>("name") == "customer");
        Assert.Equal("nvarchar(50)", customer.Value<string>("dbType"));
        Assert.Equal(50, customer.Value<int>("length"));

        var amount = columns.Single(c => c.Value<string>("name") == "amount");
        Assert.Equal("decimal(10,2)", amount.Value<string>("dbType"));
        Assert.Equal("decimal", amount.Value<string>("clrType"));

        // A default constraint is a value the database fills in.
        var created = columns.Single(c => c.Value<string>("name") == "created_at");
        Assert.True(created.Value<bool>("generated"));

        var processed = columns.Single(c => c.Value<string>("name") == "processed");
        Assert.Equal("bool", processed.Value<string>("clrType"));
    }

    /// <summary>
    /// The schema filter does something, which is why the fixture puts everything in `sales` rather
    /// than dbo: against a schema-less seed a filter that was ignored would still pass.
    /// </summary>
    [SkippableFact]
    public async Task Discover_filters_by_schema()
    {
        var adapter = await AdapterAsync();

        var inSales = await adapter.InvokeAsync<JObject>("Discover",
            new { objectType = "table", schema = "sales" }, timeoutSeconds: 60);
        Assert.NotEmpty(inSales["objects"]!);

        var inDbo = await adapter.InvokeAsync<JObject>("Discover",
            new { objectType = "table", schema = "dbo" }, timeoutSeconds: 60);
        Assert.Empty(inDbo["objects"]!);
    }

    [SkippableFact]
    public async Task Discover_finds_procedures_functions_and_sequences()
    {
        var adapter = await AdapterAsync();

        var procedures = await adapter.InvokeAsync<JObject>("Discover",
            new { objectType = "procedure", schema = "sales" }, timeoutSeconds: 60);

        var counting = procedures["objects"]!.Single(o => o.Value<string>("name") == "count_orders");
        var parameters = counting["parameters"]!.ToList();

        // The @ is stripped: a caller binds by name, and the prefix is the driver's business.
        Assert.Equal("p_customer", parameters[0].Value<string>("name"));
        Assert.Equal("In", parameters[0].Value<string>("direction"));
        Assert.Equal("p_total", parameters[1].Value<string>("name"));
        Assert.Equal("Out", parameters[1].Value<string>("direction"));

        // An inline table-valued function is a function, not a procedure - the shape a receive
        // statement would select from.
        var functions = await adapter.InvokeAsync<JObject>("Discover",
            new { objectType = "function", schema = "sales" }, timeoutSeconds: 60);
        Assert.Contains(functions["objects"]!, o => o.Value<string>("name") == "orders_for");

        var sequences = await adapter.InvokeAsync<JObject>("Discover",
            new { objectType = "sequence", schema = "sales" }, timeoutSeconds: 60);
        Assert.Contains(sequences["objects"]!, o => o.Value<string>("name") == "bitween_order_seq");
    }

    // ---------------------------------------------------------------- running

    [SkippableFact]
    public async Task Query_returns_rows_for_a_named_statement()
    {
        var adapter = await AdapterAsync();

        var result = await adapter.InvokeAsync<JObject>("Query", new
        {
            name = "ordersForCustomer",
            parameters = new Dictionary<string, object> { ["customer"] = "acme" }
        }, timeoutSeconds: 60);

        var rows = result["rows"]!.ToList();
        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.Equal("acme", r.Value<string>("customer")));
    }

    [SkippableFact]
    public async Task Call_returns_rows_straight_from_a_procedure()
    {
        var adapter = await AdapterAsync();

        var result = await adapter.InvokeAsync<JObject>("Call", new
        {
            name = "ordersByCustomerProc",
            parameters = new Dictionary<string, object> { ["p_customer"] = "globex" }
        }, timeoutSeconds: 60);

        var rows = result["rows"]!.ToList();
        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.Equal("globex", r.Value<string>("customer")));
    }

    /// <summary>
    /// OUTPUT is SQL Server's RETURNING, and the capability list claims it - so a statement using
    /// it has to actually work, not merely be declared possible.
    /// </summary>
    [SkippableFact]
    public async Task An_insert_with_output_returns_the_row_it_wrote()
    {
        var adapter = await AdapterAsync();

        var result = await adapter.InvokeAsync<JObject>("Query", new
        {
            name = "insertOrderOutput",
            parameters = new Dictionary<string, object>
                { ["id"] = 902, ["customer"] = "outputted", ["amount"] = 5.5 }
        }, timeoutSeconds: 60);

        var rows = result["rows"]!.ToList();
        Assert.Single(rows);
        Assert.Equal("outputted", rows[0].Value<string>("customer"));
    }

    [SkippableFact]
    public async Task Execute_reports_what_it_changed()
    {
        var adapter = await AdapterAsync();

        var result = await adapter.InvokeAsync<JObject>("Execute", new
        {
            name = "insertOrder",
            parameters = new Dictionary<string, object>
                { ["id"] = 801, ["customer"] = "inserted", ["amount"] = 12.34 }
        }, timeoutSeconds: 60);

        Assert.Equal(1, result.Value<int>("affectedRows"));
    }

    [SkippableFact]
    public async Task A_failed_batch_rolls_back_what_came_before_it()
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
                        { ["id"] = 850, ["customer"] = "rolled-back", ["amount"] = 1 }
                },
                new
                {
                    // id 1 is seeded, so this violates the primary key.
                    name = "insertOrder",
                    parameters = new Dictionary<string, object>
                        { ["id"] = 1, ["customer"] = "rolled-back", ["amount"] = 1 }
                }
            }
        }, timeoutSeconds: 60));

        var back = await adapter.InvokeAsync<JObject>("Query", new
        {
            name = "ordersForCustomer",
            parameters = new Dictionary<string, object> { ["customer"] = "rolled-back" }
        }, timeoutSeconds: 60);

        Assert.Empty(back["rows"]!);
    }

    [SkippableFact]
    public async Task Sql_sent_with_a_message_is_refused()
    {
        var adapter = await AdapterAsync();

        var error = await Assert.ThrowsAnyAsync<Exception>(() =>
            adapter.InvokeAsync<JObject>("Query",
                new { sql = $"select * from sales.{SqlServerDbFixture.Table}" }, timeoutSeconds: 60));

        Assert.Contains("does not allow ad-hoc SQL", error.Message);
    }

    // ---------------------------------------------------------------- receiving

    [SkippableFact]
    public async Task The_receiver_advances_a_cursor_that_survives_a_restart()
    {
        var adapter = await AdapterAsync();
        var me = Subscription(1);

        await adapter.InvokeAsync<object>("Initialize", timeoutSeconds: 60, properties: me);
        var first = await adapter.InvokeAsync<List<string>>("ListFiles", timeoutSeconds: 60,
            properties: me);

        Assert.Equal(5, first.Count);

        foreach (var id in first)
        {
            var file = await adapter.InvokeAsync<JObject>("GetFile", id, timeoutSeconds: 60,
                properties: me);
            var data = file.Value<string>("data") ?? file.Value<string>("Data");
            Assert.False(string.IsNullOrWhiteSpace(data));

            await adapter.InvokeAsync<object>("DeleteFile", id, timeoutSeconds: 60, properties: me);
        }

        await adapter.InvokeAsync<object>("Finalize", timeoutSeconds: 60, properties: me);

        Assert.Equal("5", await CursorAsync("receive.cursor.1"));

        var restarted = await Host.RestartAsync(BusAdapters.SqlServer, _dataSourceId.ToString(),
            drain: false);

        await restarted.InvokeAsync<object>("Initialize", timeoutSeconds: 60, properties: me);
        var second = await restarted.InvokeAsync<List<string>>("ListFiles", timeoutSeconds: 60,
            properties: me);

        Assert.Equal(new[] { 6, 7, 8, 9, 10 }, second.Select(KeyOf).OrderBy(i => i));
    }

    [SkippableFact]
    public async Task Two_subscriptions_on_one_data_source_do_not_share_a_cursor()
    {
        var adapter = await AdapterAsync();

        var first = Subscription(101);
        var second = Subscription(202);

        await adapter.InvokeAsync<object>("Initialize", timeoutSeconds: 60, properties: first);
        var forFirst = await adapter.InvokeAsync<List<string>>("ListFiles", timeoutSeconds: 60,
            properties: first);
        foreach (var id in forFirst)
            await adapter.InvokeAsync<object>("DeleteFile", id, timeoutSeconds: 60, properties: first);
        await adapter.InvokeAsync<object>("Finalize", timeoutSeconds: 60, properties: first);

        Assert.NotEmpty(forFirst);

        await adapter.InvokeAsync<object>("Initialize", timeoutSeconds: 60, properties: second);
        var forSecond = await adapter.InvokeAsync<List<string>>("ListFiles", timeoutSeconds: 60,
            properties: second);
        await adapter.InvokeAsync<object>("Finalize", timeoutSeconds: 60, properties: second);

        Assert.Equal(
            forFirst.Select(KeyOf).OrderBy(k => k),
            forSecond.Select(KeyOf).OrderBy(k => k));
    }

    [SkippableFact]
    public async Task Mark_processed_runs_for_each_accepted_row()
    {
        var adapter = await AdapterAsync();
        var me = Subscription(2);

        await adapter.InvokeAsync<object>("Initialize", timeoutSeconds: 60, properties: me);
        var listed = await adapter.InvokeAsync<List<string>>("ListFiles", timeoutSeconds: 60,
            properties: me);

        foreach (var id in listed)
            await adapter.InvokeAsync<object>("DeleteFile", id, timeoutSeconds: 60, properties: me);

        await adapter.InvokeAsync<object>("Finalize", timeoutSeconds: 60, properties: me);

        var processed = await adapter.InvokeAsync<JObject>("Query", new { name = "processedOrders" },
            timeoutSeconds: 60);

        Assert.NotEmpty(processed["rows"]!);
    }

    // ---------------------------------------------------------------- validating

    /// <summary>
    /// The check that could not be shared. SqlCommand.Prepare refuses unless every parameter has an
    /// explicit type, which a caller checking someone else's SQL does not know - so this adapter
    /// asks sp_describe_undeclared_parameters instead, and these prove it answers the same
    /// questions the prepared form does everywhere else.
    /// </summary>
    [SkippableFact]
    public async Task Valid_sql_passes_validation_even_with_parameters()
    {
        var adapter = await AdapterAsync();

        var result = await adapter.InvokeAsync<JObject>("ValidateStatement",
            new { sql = $"select id from sales.{SqlServerDbFixture.Table} where id = @id" },
            timeoutSeconds: 60);

        Assert.True(result.Value<bool>("ok"), result.Value<string>("error"));
    }

    [SkippableFact]
    public async Task A_dropped_table_is_caught_before_the_statement_is_stored()
    {
        var adapter = await AdapterAsync();

        var result = await adapter.InvokeAsync<JObject>("ValidateStatement",
            new { sql = "select 1 from sales.nothing_of_the_sort" }, timeoutSeconds: 60);

        Assert.False(result.Value<bool>("ok"));
        Assert.Contains("Invalid object name", result.Value<string>("error"));
    }

    [SkippableFact]
    public async Task A_syntax_error_is_caught()
    {
        var adapter = await AdapterAsync();

        var result = await adapter.InvokeAsync<JObject>("ValidateStatement",
            new { sql = "selct 1" }, timeoutSeconds: 60);

        Assert.False(result.Value<bool>("ok"));
        Assert.False(string.IsNullOrWhiteSpace(result.Value<string>("error")));
    }

    [SkippableFact]
    public async Task A_bare_procedure_name_is_accepted_and_says_what_was_not_checked()
    {
        var adapter = await AdapterAsync();

        var result = await adapter.InvokeAsync<JObject>("ValidateStatement",
            new { sql = "sales.some_procedure" }, timeoutSeconds: 60);

        Assert.True(result.Value<bool>("ok"));
        Assert.Contains("existence not checked", result.Value<string>("note"));
    }

    // ---------------------------------------------------------------- setup

    static int KeyOf(string fileId) => int.Parse(fileId.Substring(fileId.IndexOf(':') + 1));

    static Dictionary<string, string> Subscription(int id) =>
        new() { ["__subscriptionId__"] = id.ToString() };

    async Task<string> CursorAsync(string name) =>
        await _fixture.App.Services.GetRequiredService<IAdapterStateStore>()
            .GetAsync(new AdapterStateKey
            {
                AdapterId = BusAdapters.SqlServer,
                InstanceKey = _dataSourceId.ToString(),
                Name = name
            }, default);

    async Task<int> CreateDataSourceAsync()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        var dataSource = new DataSource
        {
            Name = $"sqlserver-{Guid.NewGuid():N}",
            AdapterId = BusAdapters.SqlServer,
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
        ["Host"] = _sqlServer.Host,
        ["Port"] = _sqlServer.Port.ToString(),
        ["Database"] = _sqlServer.Database,
        ["UserName"] = _sqlServer.User,
        ["Password"] = _sqlServer.Password,
        ["Schema"] = "sales",
        ["MinPoolSize"] = "1",
        ["MaxPoolSize"] = "5",

        // The container presents a self-signed certificate, which is the case this setting exists
        // for - and the reason the adapter offers it rather than pretending every server has a
        // certificate chain that validates.
        ["Encrypt"] = "true",
        ["TrustServerCertificate"] = "true",

        ["Statements"] = $@"{{
            ""seededOrders"":         ""select * from sales.{SqlServerDbFixture.Table} where id <= 25 order by id"",
            ""ordersForCustomer"":    ""select * from sales.{SqlServerDbFixture.Table} where customer = @customer order by id"",
            ""ordersByCustomerProc"": ""sales.orders_by_customer"",
            ""ordersForFunction"":    ""select * from sales.orders_for(@customer)"",
            ""processedOrders"":      ""select * from sales.{SqlServerDbFixture.Table} where processed = 1 order by id"",
            ""insertOrder"":          ""insert into sales.{SqlServerDbFixture.Table} (id, customer, amount) values (@id, @customer, @amount)"",
            ""insertOrderOutput"":    ""insert into sales.{SqlServerDbFixture.Table} (id, customer, amount) output inserted.* values (@id, @customer, @amount)"",

            ""earlyOrders"": {{
                ""sql"":          ""select * from sales.{SqlServerDbFixture.Table} where id > @cursor and id <= 10 order by id"",
                ""cursorColumn"": ""id"",
                ""keyColumn"":    ""id""
            }}
        }}",

        ["ReceiveMode"] = "incrementing",

        // TOP rather than LIMIT, and it has to come before the column list - one of the small
        // dialect differences a statement writer meets immediately.
        ["ReceiveStatement"] =
            $"select top 5 * from sales.{SqlServerDbFixture.Table} where id > @cursor order by id",
        ["CursorColumn"] = "id",
        ["KeyColumn"] = "id",
        ["MarkProcessedStatement"] =
            $"update sales.{SqlServerDbFixture.Table} set processed = 1 where id = @key",
        ["ReceiveBatchSize"] = "5"
    };

    async Task StartAdapterAsync()
    {
        var spec = new AdapterSpec
        {
            AdapterId = BusAdapters.SqlServer,
            InstanceKey = _dataSourceId.ToString()
        };

        foreach (var kv in Properties()) spec.StartupValues[kv.Key] = kv.Value;

        await Host.StartExclusiveAsync(spec);
    }
}
