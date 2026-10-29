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
/// The MySQL data source provider, against a real MySQL.
///
/// The same suite as PostgreSQL's and Oracle's, which is the point: three engines run on one core,
/// so the parts that are shared should behave identically and the parts that differ should differ
/// VISIBLY - in the capability list, and in how a routine returns rows. Everything here goes over
/// the resident transport into a real adapter process and out to a real database.
/// </summary>
[Collection("Bitween")]
public class MySqlAdapterTests : IClassFixture<MySqlDbFixture>
{
    readonly BitweenFixture _fixture;
    readonly MySqlDbFixture _mysql;

    public MySqlAdapterTests(BitweenFixture fixture, MySqlDbFixture mysql)
    {
        _fixture = fixture;
        _mysql = mysql;
    }

    static readonly SemaphoreSlim Gate = new(1, 1);
    static int _dataSourceId;

    IResidentAdapterHost Host => _fixture.App.Services.GetRequiredService<IResidentAdapterHost>();

    async Task<ResidentAdapterInstance> AdapterAsync()
    {
        Skip.If(_mysql.Unavailable != null, $"MySQL is not available: {_mysql.Unavailable}");

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

        return Host.Get(BusAdapters.MySql, _dataSourceId.ToString())
               ?? throw new InvalidOperationException("The MySQL adapter is not running.");
    }

    // ---------------------------------------------------------------- configuration

    [SkippableFact]
    public async Task Connection_test_reports_each_stage_and_prepares_every_statement()
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
    /// Where MySQL is honestly different from the other two. A procedure returns rows by SELECTing,
    /// which PostgreSQL cannot do - but there is no MERGE, no RETURNING, no array type and no
    /// sequence, and claiming any of them would have a statement written against it fail on a real
    /// server.
    /// </summary>
    [SkippableFact]
    public async Task Describe_reports_the_engine_and_where_it_differs()
    {
        var adapter = await AdapterAsync();
        var described = await adapter.InvokeAsync<JObject>("Describe", timeoutSeconds: 60);

        Assert.Equal("MySQL", described.Value<string>("engine"));
        Assert.False(string.IsNullOrWhiteSpace(described.Value<string>("serverVersion")));

        // The capability PostgreSQL declares false and this one declares true.
        Assert.True(described.Value<bool>("storedProcedures"));
        Assert.True(described.Value<bool>("procedureResultSets"));

        // And the three it does not have.
        Assert.False(described.Value<bool>("merge"));
        Assert.False(described.Value<bool>("returning"));
        Assert.False(described.Value<bool>("arrayTypes"));

        var objects = described["supportedObjects"]!.Select(o => o.Value<string>()).ToList();
        Assert.Contains("procedure", objects);
        Assert.Contains("function", objects);
        // No sequences in MySQL - AUTO_INCREMENT belongs to a column, not to an object.
        Assert.DoesNotContain("sequence", objects);

        // READ UNCOMMITTED does something here, unlike PostgreSQL where it is silently promoted.
        var isolation = described["isolationLevels"]!.Select(i => i.Value<string>()).ToList();
        Assert.Contains("ReadUncommitted", isolation);

        Assert.False(described.Value<bool>("logBasedCdc"));
        Assert.False(described.Value<bool>("changeNotification"));

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
            nameLike = MySqlDbFixture.Table,
            includeColumns = true,
            includeRowCounts = true
        }, timeoutSeconds: 60);

        var table = result["objects"]!.Single(o => o.Value<string>("name") == MySqlDbFixture.Table);

        // A schema IS a database in MySQL, and this is what that means in practice.
        Assert.Equal(_mysql.Database, table.Value<string>("schema"));
        Assert.Equal("Customer orders", table.Value<string>("comment"));

        var columns = table["columns"]!.ToList();
        Assert.Equal("id", columns[0].Value<string>("name"));
        Assert.True(columns[0].Value<bool>("primaryKey"));
        Assert.False(columns[0].Value<bool>("nullable"));

        // tinyint(1) IS the boolean type in MySQL, and the driver returns one - so reporting "int"
        // would mislead whoever writes the mapper.
        var processed = columns.Single(c => c.Value<string>("name") == "processed");
        Assert.Equal("bool", processed.Value<string>("clrType"));

        // A CURRENT_TIMESTAMP default reports as DEFAULT_GENERATED in `extra`, which is a value the
        // database fills in - the only distinction an insert cares about.
        var created = columns.Single(c => c.Value<string>("name") == "created_at");
        Assert.True(created.Value<bool>("generated"));

        var amount = columns.Single(c => c.Value<string>("name") == "amount");
        Assert.Equal("decimal", amount.Value<string>("clrType"));
        Assert.Equal(10, amount.Value<int>("precision"));
        Assert.Equal(2, amount.Value<int>("scale"));
    }

    [SkippableFact]
    public async Task Discover_finds_procedures_and_functions_with_their_parameters()
    {
        var adapter = await AdapterAsync();

        var procedures = await adapter.InvokeAsync<JObject>("Discover",
            new { objectType = "procedure" }, timeoutSeconds: 60);

        var counting = procedures["objects"]!.Single(o => o.Value<string>("name") == "count_orders");
        var parameters = counting["parameters"]!.ToList();

        Assert.Equal("p_customer", parameters[0].Value<string>("name"));
        Assert.Equal("In", parameters[0].Value<string>("direction"));
        Assert.Equal("p_total", parameters[1].Value<string>("name"));
        Assert.Equal("Out", parameters[1].Value<string>("direction"));

        var functions = await adapter.InvokeAsync<JObject>("Discover",
            new { objectType = "function" }, timeoutSeconds: 60);

        var total = functions["objects"]!.Single(o => o.Value<string>("name") == "total_for");

        // A function's return type comes from dtd_identifier and is listed once, not twice.
        var returns = total["parameters"]!.Where(p => p.Value<string>("direction") == "ReturnValue").ToList();
        Assert.Single(returns);
    }

    [SkippableFact]
    public async Task Discover_filters_by_name_against_the_database_not_the_page()
    {
        var adapter = await AdapterAsync();

        var found = await adapter.InvokeAsync<JObject>("Discover",
            new { objectType = "table", nameLike = "ORDERS" }, timeoutSeconds: 60);
        Assert.NotEmpty(found["objects"]!);

        var nothing = await adapter.InvokeAsync<JObject>("Discover",
            new { objectType = "table", nameLike = "no_such_table_anywhere" }, timeoutSeconds: 60);
        Assert.Empty(nothing["objects"]!);
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

    /// <summary>
    /// The difference that matters: CALL hands back a result set with nothing declared and nothing
    /// bound. On PostgreSQL this same shape needs a set-returning function queried with SELECT, and
    /// on Oracle an explicit REF CURSOR.
    /// </summary>
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

        var back = await adapter.InvokeAsync<JObject>("Query", new
        {
            name = "ordersForCustomer",
            parameters = new Dictionary<string, object> { ["customer"] = "inserted" }
        }, timeoutSeconds: 60);

        Assert.Single(back["rows"]!);
    }

    /// <summary>
    /// A batch is one transaction: either every statement in it happened or none did. Proven by
    /// making the second one fail - a duplicate primary key - and then looking for the first.
    /// </summary>
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

    /// <summary>
    /// Ad-hoc SQL is refused unless the data source allows it, whatever the engine. The mapper is a
    /// template over message content - if it can emit SQL text, every Xchange is an injection
    /// vector into the customer's database.
    /// </summary>
    [SkippableFact]
    public async Task Sql_sent_with_a_message_is_refused()
    {
        var adapter = await AdapterAsync();

        var error = await Assert.ThrowsAnyAsync<Exception>(() =>
            adapter.InvokeAsync<JObject>("Query",
                new { sql = $"select * from {MySqlDbFixture.Table}" }, timeoutSeconds: 60));

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

        var restarted = await Host.RestartAsync(BusAdapters.MySql, _dataSourceId.ToString(),
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

    [SkippableFact]
    public async Task Valid_sql_passes_validation()
    {
        var adapter = await AdapterAsync();

        var result = await adapter.InvokeAsync<JObject>("ValidateStatement",
            new { sql = $"select id from {MySqlDbFixture.Table} where id = @id" },
            timeoutSeconds: 60);

        Assert.True(result.Value<bool>("ok"), result.Value<string>("error"));
    }

    [SkippableFact]
    public async Task A_dropped_table_is_caught_before_the_statement_is_stored()
    {
        var adapter = await AdapterAsync();

        var result = await adapter.InvokeAsync<JObject>("ValidateStatement",
            new { sql = "select 1 from nothing_of_the_sort" }, timeoutSeconds: 60);

        Assert.False(result.Value<bool>("ok"));
        Assert.False(string.IsNullOrWhiteSpace(result.Value<string>("error")));
    }

    /// <summary>
    /// The mistake worth naming rather than leaving to a character offset: SQL copied from an
    /// Oracle data source, where a parameter is :name, into one where it is @name.
    /// </summary>
    [SkippableFact]
    public async Task The_wrong_placeholder_prefix_is_named()
    {
        var adapter = await AdapterAsync();

        var result = await adapter.InvokeAsync<JObject>("ValidateStatement",
            new { sql = $"select id from {MySqlDbFixture.Table} where id = :ident" },
            timeoutSeconds: 60);

        Assert.False(result.Value<bool>("ok"));
        Assert.Contains("@ident", result.Value<string>("error"));
    }

    [SkippableFact]
    public async Task A_bare_procedure_name_is_accepted_and_says_what_was_not_checked()
    {
        var adapter = await AdapterAsync();

        var result = await adapter.InvokeAsync<JObject>("ValidateStatement",
            new { sql = "some_procedure" }, timeoutSeconds: 60);

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
                AdapterId = BusAdapters.MySql,
                InstanceKey = _dataSourceId.ToString(),
                Name = name
            }, default);

    async Task<int> CreateDataSourceAsync()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        var dataSource = new DataSource
        {
            Name = $"mysql-{Guid.NewGuid():N}",
            AdapterId = BusAdapters.MySql,
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
        ["Host"] = _mysql.Host,
        ["Port"] = _mysql.Port.ToString(),
        ["Database"] = _mysql.Database,
        ["UserName"] = _mysql.User,
        ["Password"] = _mysql.Password,
        ["MinPoolSize"] = "1",
        ["MaxPoolSize"] = "5",

        ["Statements"] = $@"{{
            ""seededOrders"":          ""select * from {MySqlDbFixture.Table} where id <= 25 order by id"",
            ""ordersForCustomer"":     ""select * from {MySqlDbFixture.Table} where customer = @customer order by id"",
            ""ordersByCustomerProc"":  ""orders_by_customer"",
            ""processedOrders"":       ""select * from {MySqlDbFixture.Table} where processed = 1 order by id"",
            ""insertOrder"":           ""insert into {MySqlDbFixture.Table} (id, customer, amount) values (@id, @customer, @amount)"",

            ""earlyOrders"": {{
                ""sql"":          ""select * from {MySqlDbFixture.Table} where id > @cursor and id <= 10 order by id"",
                ""cursorColumn"": ""id"",
                ""keyColumn"":    ""id""
            }}
        }}",

        ["ReceiveMode"] = "incrementing",
        ["ReceiveStatement"] =
            $"select * from {MySqlDbFixture.Table} where id > @cursor order by id limit 5",
        ["CursorColumn"] = "id",
        ["KeyColumn"] = "id",
        ["MarkProcessedStatement"] =
            $"update {MySqlDbFixture.Table} set processed = 1 where id = @key",
        ["ReceiveBatchSize"] = "5"
    };

    async Task StartAdapterAsync()
    {
        var spec = new AdapterSpec
        {
            AdapterId = BusAdapters.MySql,
            InstanceKey = _dataSourceId.ToString()
        };

        foreach (var kv in Properties()) spec.StartupValues[kv.Key] = kv.Value;

        await Host.StartExclusiveAsync(spec);
    }
}
