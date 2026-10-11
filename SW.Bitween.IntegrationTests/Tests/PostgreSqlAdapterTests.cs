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
/// The PostgreSQL data source provider, against a real PostgreSQL.
///
/// The same suite as Oracle's, which is the point: both engines run on one core, so the parts that
/// are shared should behave identically and the parts that differ should differ *visibly* - in the
/// capability list, and in how a routine returns rows. Everything here goes over the resident
/// transport into a real adapter process and out to a real database.
/// </summary>
[Collection("Bitween")]
public class PostgreSqlAdapterTests : IClassFixture<PostgreSqlDbFixture>
{
    readonly BitweenFixture _fixture;
    readonly PostgreSqlDbFixture _postgres;

    public PostgreSqlAdapterTests(BitweenFixture fixture, PostgreSqlDbFixture postgres)
    {
        _fixture = fixture;
        _postgres = postgres;
    }

    static readonly SemaphoreSlim Gate = new(1, 1);
    static int _dataSourceId;

    IResidentAdapterHost Host => _fixture.App.Services.GetRequiredService<IResidentAdapterHost>();

    async Task<ResidentAdapterInstance> AdapterAsync()
    {
        Skip.If(_postgres.Unavailable != null, $"PostgreSQL is not available: {_postgres.Unavailable}");

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

        return Host.Get(BusAdapters.PostgreSql, _dataSourceId.ToString())
               ?? throw new InvalidOperationException("The PostgreSQL adapter is not running.");
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

        // Npgsql refuses to prepare a statement whose parameters have not been supplied, so the
        // core declares an empty placeholder for each before preparing. Without that, every
        // parameterised statement would fail this check - which is why one is named here.
        Assert.Contains("statement:ordersForCustomer", steps);
        Assert.All(result["steps"]!, s => Assert.True(s.Value<bool>("ok"), s.ToString()));
    }

    /// <summary>
    /// Where the two engines are honestly different. PostgreSQL has no REF CURSOR, so a CALL cannot
    /// hand back rows - declared false rather than glossed over - but it does have arrays and
    /// repeatable-read, which Oracle's list does not claim.
    /// </summary>
    [SkippableFact]
    public async Task Describe_reports_the_engine_and_where_it_differs_from_Oracle()
    {
        var adapter = await AdapterAsync();
        var described = await adapter.InvokeAsync<JObject>("Describe", timeoutSeconds: 60);

        Assert.Equal("PostgreSQL", described.Value<string>("engine"));
        Assert.False(string.IsNullOrWhiteSpace(described.Value<string>("serverVersion")));

        Assert.True(described.Value<bool>("storedProcedures"));
        Assert.False(described.Value<bool>("procedureResultSets"));
        Assert.True(described.Value<bool>("multipleResultSets"));
        Assert.True(described.Value<bool>("arrayTypes"));

        var isolation = described["isolationLevels"]!.Select(i => i.Value<string>()).ToList();
        Assert.Contains("RepeatableRead", isolation);

        Assert.False(described.Value<bool>("logBasedCdc"));
        Assert.False(described.Value<bool>("changeNotification"));

        // Role attributes and database privileges are separate things here, and both are probed.
        var privileges = described["privileges"]!.Select(p => p.Value<string>()).ToList();
        Assert.Contains("CONNECT", privileges);
    }

    [SkippableFact]
    public async Task Discover_finds_the_table_with_its_columns_and_key()
    {
        var adapter = await AdapterAsync();
        var result = await adapter.InvokeAsync<JObject>("Discover", new
        {
            objectType = "table",
            schema = "public",
            nameLike = "bitween",
            includeColumns = true,
            includeRowCounts = true
        }, timeoutSeconds: 60);

        var table = result["objects"]!.Single(o => o.Value<string>("name") == PostgreSqlDbFixture.Table);
        Assert.Equal("table", table.Value<string>("type"));
        Assert.Equal("Customer orders", table.Value<string>("comment"));

        var columns = table["columns"]!.ToDictionary(c => c.Value<string>("name")!);
        Assert.Equal(5, columns.Count);

        Assert.True(columns["id"].Value<bool>("primaryKey"));
        Assert.Equal("int", columns["id"].Value<string>("clrType"));
        Assert.Equal("decimal", columns["amount"].Value<string>("clrType"));
        Assert.Equal("DateTime", columns["created_at"].Value<string>("clrType"));
        Assert.Equal("bool", columns["processed"].Value<string>("clrType"));

        // A column with a default is filled in by the database, which is worth knowing before
        // writing an insert that supplies it.
        Assert.True(columns["created_at"].Value<bool>("generated"));
        Assert.True(columns["customer"].Value<bool>("nullable"));
        Assert.False(columns["id"].Value<bool>("nullable"));
    }

    [SkippableFact]
    public async Task Discover_lists_sequences_with_their_current_value()
    {
        var adapter = await AdapterAsync();
        var result = await adapter.InvokeAsync<JObject>("Discover", new
        {
            objectType = "sequence",
            schema = "public"
        }, timeoutSeconds: 60);

        Assert.Contains(result["objects"]!, o => o.Value<string>("name") == "bitween_order_seq");
    }

    /// <summary>
    /// Functions and procedures are listed separately, because on PostgreSQL they are genuinely
    /// different things - one is queried, the other is called - and lumping them together is how a
    /// caller ends up using the wrong verb.
    /// </summary>
    [SkippableFact]
    public async Task Discover_separates_functions_from_procedures_and_parses_their_arguments()
    {
        var adapter = await AdapterAsync();

        var functions = await adapter.InvokeAsync<JObject>("Discover", new
        {
            objectType = "function",
            schema = "public",
            nameLike = "orders_by_customer"
        }, timeoutSeconds: 60);

        var function = functions["objects"]!.Single();
        Assert.Equal("function", function.Value<string>("type"));

        var arguments = function["parameters"]!.ToDictionary(p => p.Value<string>("name")!);
        Assert.Equal("In", arguments["p_customer"].Value<string>("direction"));

        // A set-returning function is the thing to use INSTEAD of a REF CURSOR, so it is called out
        // where whoever is configuring the subscription will see it.
        Assert.True(arguments.ContainsKey("(returns)"));
        Assert.Contains("SETOF", arguments["(returns)"].Value<string>("dbType"));

        var procedures = await adapter.InvokeAsync<JObject>("Discover", new
        {
            objectType = "procedure",
            schema = "public",
            nameLike = "mark_all_processed"
        }, timeoutSeconds: 60);

        Assert.Equal("procedure", procedures["objects"]!.Single().Value<string>("type"));
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
        }, timeoutSeconds: 60);

        var rows = result["rows"]!.ToList();
        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.Equal("acme", r.Value<string>("customer")));
    }

    [SkippableFact]
    public async Task Ad_hoc_sql_is_refused_when_the_data_source_does_not_allow_it()
    {
        var adapter = await AdapterAsync();

        var error = await Assert.ThrowsAnyAsync<Exception>(() =>
            adapter.InvokeAsync<JObject>("Query", new
            {
                sql = $"select * from {PostgreSqlDbFixture.Table}"
            }, timeoutSeconds: 60));

        Assert.Contains("does not allow ad-hoc SQL", error.Message);
    }

    /// <summary>
    /// The same paging property Oracle's suite pins: the row read to discover there IS another page
    /// is carried into the next one rather than dropped, which would otherwise lose exactly one row
    /// per page.
    /// </summary>
    [SkippableFact]
    public async Task A_query_past_the_row_ceiling_pages_without_losing_a_row()
    {
        var adapter = await AdapterAsync();
        var seen = new List<int>();

        var page = await adapter.InvokeAsync<JObject>("Query", new
        {
            name = "seededOrders",
            maxRows = 7
        }, timeoutSeconds: 60);

        Collect(page, seen);
        Assert.True(page.Value<bool>("hasMore"));

        var cursorId = page.Value<string>("cursorId");
        while (!string.IsNullOrEmpty(cursorId))
        {
            var next = await adapter.InvokeAsync<JObject>("Fetch", new { cursorId, take = 7 },
                timeoutSeconds: 60);

            Collect(next, seen);
            cursorId = next.Value<string>("cursorId");
        }

        Assert.Equal(25, seen.Count);
        Assert.Equal(Enumerable.Range(1, 25), seen.OrderBy(i => i));
    }

    static void Collect(JObject page, List<int> into) =>
        into.AddRange(page["rows"]!.Select(r => r.Value<int>("id")));

    /// <summary>
    /// RETURNING, which is the PostgreSQL write form worth having: the row the database actually
    /// wrote, defaults filled in, without a second round trip to go and read it back.
    /// </summary>
    [SkippableFact]
    public async Task A_write_with_returning_hands_back_the_row_it_wrote()
    {
        var adapter = await AdapterAsync();

        var result = await adapter.InvokeAsync<JObject>("Execute", new
        {
            name = "insertOrderReturning",
            parameters = new Dictionary<string, object>
            {
                ["id"] = 900,
                ["customer"] = "written-by-test",
                ["amount"] = 12.5
            }
        }, timeoutSeconds: 60);

        var row = Assert.Single(result["rows"]!);
        Assert.Equal(900, row.Value<int>("id"));

        // created_at is a database default, so getting it back is the point of RETURNING.
        Assert.NotNull(row.Value<DateTime?>("created_at"));
    }

    /// <summary>
    /// The PostgreSQL substitute for Oracle's REF CURSOR. Same outcome - rows from a routine - but
    /// reached with Query rather than Call, which is what `procedureResultSets: false` is telling
    /// whoever reads the capability list.
    /// </summary>
    [SkippableFact]
    public async Task A_set_returning_function_is_read_with_query()
    {
        var adapter = await AdapterAsync();

        var result = await adapter.InvokeAsync<JObject>("Query", new
        {
            name = "ordersByCustomerFunction",
            parameters = new Dictionary<string, object> { ["customer"] = "acme" }
        }, timeoutSeconds: 60);

        var rows = result["rows"]!.ToList();
        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.Equal("acme", r.Value<string>("customer")));
    }

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
                new
                {
                    name = "insertOrder",
                    parameters = new Dictionary<string, object>
                        { ["id"] = 950, ["customer"] = "rolled-back", ["amount"] = 1 }
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

    // ---------------------------------------------------------------- receiving

    [SkippableFact]
    public async Task The_receiver_advances_a_cursor_that_survives_a_restart()
    {
        var adapter = await AdapterAsync();

        // Its own subscription, so its cursor is its own. Every test in this class shares one data
        // source and one table; before cursors were scoped they also shared one cursor, and which
        // of them passed depended on the order xUnit happened to run them in.
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

        var restarted = await Host.RestartAsync(BusAdapters.PostgreSql, _dataSourceId.ToString(),
            drain: false);

        await restarted.InvokeAsync<object>("Initialize", timeoutSeconds: 60, properties: me);
        var second = await restarted.InvokeAsync<List<string>>("ListFiles", timeoutSeconds: 60,
            properties: me);

        Assert.Equal(new[] { 6, 7, 8, 9, 10 }, second.Select(KeyOf).OrderBy(i => i));
    }

    /// <summary>
    /// Two subscriptions polling ONE data source keep separate cursors.
    ///
    /// They did not. State is keyed by (adapter, instance, name) and the instance is the data
    /// source, so a fixed cursor name meant one cursor for the whole connection: whichever
    /// subscription polled first advanced it, and the rows it took were invisible to the other.
    /// Half the rows each, no error. The name now carries the subscription, which arrives as a
    /// per-invocation value.
    /// </summary>
    [SkippableFact]
    public async Task Two_subscriptions_on_one_data_source_do_not_share_a_cursor()
    {
        var adapter = await AdapterAsync();

        var first = Subscription(101);
        var second = Subscription(202);

        // Subscription 101 reads and accepts everything it was given.
        await adapter.InvokeAsync<object>("Initialize", timeoutSeconds: 60, properties: first);
        var forFirst = await adapter.InvokeAsync<List<string>>("ListFiles", timeoutSeconds: 60,
            properties: first);
        foreach (var id in forFirst)
            await adapter.InvokeAsync<object>("DeleteFile", id, timeoutSeconds: 60, properties: first);
        await adapter.InvokeAsync<object>("Finalize", timeoutSeconds: 60, properties: first);

        Assert.NotEmpty(forFirst);

        // Subscription 202 has never read anything, so it must see the same rows from the start -
        // not the ones left over after 101 moved the cursor.
        await adapter.InvokeAsync<object>("Initialize", timeoutSeconds: 60, properties: second);
        var forSecond = await adapter.InvokeAsync<List<string>>("ListFiles", timeoutSeconds: 60,
            properties: second);
        await adapter.InvokeAsync<object>("Finalize", timeoutSeconds: 60, properties: second);

        Assert.Equal(
            forFirst.Select(KeyOf).OrderBy(k => k),
            forSecond.Select(KeyOf).OrderBy(k => k));

        // And each was saved under its own name, rather than one overwriting the other.
        Assert.NotNull(await CursorAsync("receive.cursor.101"));
        // 202 listed but accepted nothing, so it has no cursor yet - which is the point: its
        // progress is its own, and reading did not move it.
        Assert.Null(await CursorAsync("receive.cursor.202"));
    }

    /// <summary>
    /// A receiver upgraded into scoped cursors resumes where it was, rather than replaying every
    /// row it has already processed. The unscoped name is read as a fallback and never written.
    /// </summary>
    [SkippableFact]
    public async Task An_existing_unscoped_cursor_is_inherited_rather_than_ignored()
    {
        var adapter = await AdapterAsync();
        var store = _fixture.App.Services.GetRequiredService<IAdapterStateStore>();

        // What a pre-upgrade adapter would have left behind.
        await store.SetAsync(new AdapterStateKey
        {
            AdapterId = BusAdapters.PostgreSql,
            InstanceKey = _dataSourceId.ToString(),
            Name = "receive.cursor"
        }, "5", default);

        var subscription = Subscription(303);

        await adapter.InvokeAsync<object>("Initialize", timeoutSeconds: 60, properties: subscription);
        var listed = await adapter.InvokeAsync<List<string>>("ListFiles", timeoutSeconds: 60,
            properties: subscription);
        await adapter.InvokeAsync<object>("Finalize", timeoutSeconds: 60, properties: subscription);

        // Rows 1-5 are behind the inherited cursor, so they are not read again.
        Assert.All(listed, id => Assert.True(KeyOf(id) > 5));

        // And only once. A subscription added afterwards is a NEW reader, not a continuation of
        // the old one, so it starts at the beginning rather than being handed 303's progress.
        var newcomer = Subscription(304);

        await adapter.InvokeAsync<object>("Initialize", timeoutSeconds: 60, properties: newcomer);
        var forNewcomer = await adapter.InvokeAsync<List<string>>("ListFiles", timeoutSeconds: 60,
            properties: newcomer);
        await adapter.InvokeAsync<object>("Finalize", timeoutSeconds: 60, properties: newcomer);

        Assert.Contains(forNewcomer, id => KeyOf(id) <= 5);
    }

    /// <summary>
    /// One connection, two receivers, different statements - the case the settings could not
    /// express while they lived on the data source.
    ///
    /// What each reads and how comes from the invocation: the statement by name, the mode and
    /// batch size as this reader's policy. The cursor and key columns come from the statement,
    /// because they describe its rows.
    /// </summary>
    [SkippableFact]
    public async Task Two_receivers_on_one_connection_poll_different_statements()
    {
        var adapter = await AdapterAsync();

        // Same connection, different questions, different batch sizes.
        var early = Subscription(401);
        early["ReceiveStatement"] = "earlyOrders";
        early["ReceiveMode"] = "incrementing";
        early["ReceiveBatchSize"] = "3";

        var late = Subscription(402);
        late["ReceiveStatement"] = "lateOrders";
        late["ReceiveMode"] = "incrementing";
        late["ReceiveBatchSize"] = "2";

        await adapter.InvokeAsync<object>("Initialize", timeoutSeconds: 60, properties: early);
        var forEarly = await adapter.InvokeAsync<List<string>>("ListFiles", timeoutSeconds: 60,
            properties: early);
        await adapter.InvokeAsync<object>("Finalize", timeoutSeconds: 60, properties: early);

        await adapter.InvokeAsync<object>("Initialize", timeoutSeconds: 60, properties: late);
        var forLate = await adapter.InvokeAsync<List<string>>("ListFiles", timeoutSeconds: 60,
            properties: late);
        await adapter.InvokeAsync<object>("Finalize", timeoutSeconds: 60, properties: late);

        // Its own statement, and its own batch size.
        Assert.Equal(new[] { 1, 2, 3 }, forEarly.Select(KeyOf).OrderBy(i => i));
        Assert.Equal(new[] { 21, 22 }, forLate.Select(KeyOf).OrderBy(i => i));
    }

    /// <summary>
    /// A named statement that does not exist is refused by name, rather than being run as SQL.
    /// That distinction is the security property: a per-invocation property has partner values
    /// templated into it before the adapter sees it, so treating one as SQL would make every
    /// poll steerable by ordinary partner data.
    /// </summary>
    [SkippableFact]
    public async Task A_receive_statement_that_is_not_configured_is_refused()
    {
        var adapter = await AdapterAsync();

        var me = Subscription(403);
        me["ReceiveStatement"] = "select * from orders";
        me["ReceiveMode"] = "bulk";

        var error = await Assert.ThrowsAnyAsync<Exception>(() =>
            adapter.InvokeAsync<List<string>>("ListFiles", timeoutSeconds: 60, properties: me));

        Assert.Contains("is not a statement this data source defines", error.Message);
    }

    // ---------------------------------------------------------------- validating

    /// <summary>
    /// A statement is checked by PREPARING it - parsed and planned by the engine, never run - so
    /// a typo is refused where it was made instead of surfacing later as a failed connection test
    /// or a failed message.
    /// </summary>
    [SkippableFact]
    public async Task Valid_sql_passes_validation()
    {
        var adapter = await AdapterAsync();

        var result = await adapter.InvokeAsync<JObject>("ValidateStatement",
            new { sql = $"select id from {PostgreSqlDbFixture.Table} where id = @id" },
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
        Assert.Contains("does not exist", result.Value<string>("error"));
    }

    /// <summary>
    /// The mistake worth naming rather than leaving to a character offset: SQL copied from an
    /// Oracle data source, where a parameter is :name, into a PostgreSQL one, where it is @name.
    /// The driver calls it a syntax error at a column number, which is true and no help.
    /// </summary>
    [SkippableFact]
    public async Task The_wrong_placeholder_prefix_is_named_rather_than_left_to_a_column_number()
    {
        var adapter = await AdapterAsync();

        var result = await adapter.InvokeAsync<JObject>("ValidateStatement",
            new { sql = $"select id from {PostgreSqlDbFixture.Table} where id = :ident" },
            timeoutSeconds: 60);

        var error = result.Value<string>("error");

        Assert.False(result.Value<bool>("ok"));
        Assert.Contains("@ident", error);
        Assert.Contains("rather than :ident", error);
    }

    /// <summary>
    /// A statement meant to be CALLED holds a procedure name, not SQL. Preparing it as text is a
    /// syntax error every time, so it is accepted with a note saying what was not checked -
    /// refusing it would block a statement that is perfectly correct.
    /// </summary>
    [SkippableFact]
    public async Task A_bare_procedure_name_is_accepted_and_says_what_was_not_checked()
    {
        var adapter = await AdapterAsync();

        var result = await adapter.InvokeAsync<JObject>("ValidateStatement",
            new { sql = "public.some_procedure" }, timeoutSeconds: 60);

        Assert.True(result.Value<bool>("ok"));
        Assert.Contains("existence not checked", result.Value<string>("note"));
    }

    /// <summary>
    /// Every statement, not up to the first bad one. Fixing one only to be told about the next,
    /// one connection test at a time, hides that the second is usually the same mistake repeated.
    /// </summary>
    [SkippableFact]
    public async Task The_connection_test_reports_every_failing_statement()
    {
        var adapter = await AdapterAsync();

        var result = await adapter.InvokeAsync<JObject>("TestConnection", timeoutSeconds: 60);
        var steps = result["steps"]!
            .Select(s => s.Value<string>("step"))
            .Where(s => s!.StartsWith("statement:"))
            .ToList();

        // The fixture configures several; all of them are reported, in one answer.
        Assert.True(steps.Count >= 4, $"only {steps.Count} statement steps were reported");
    }

    static int KeyOf(string fileId) => int.Parse(fileId.Substring(fileId.IndexOf(':') + 1));

    /// <summary>What the host stamps on every invocation, and what scopes the cursor.</summary>
    static Dictionary<string, string> Subscription(int id) =>
        new() { ["__subscriptionId__"] = id.ToString() };

    async Task<string> CursorAsync(string name) =>
        await _fixture.App.Services.GetRequiredService<IAdapterStateStore>()
            .GetAsync(new AdapterStateKey
            {
                AdapterId = BusAdapters.PostgreSql,
                InstanceKey = _dataSourceId.ToString(),
                Name = name
            }, default);

    /// <summary>
    /// The mark-processed statement - Camel's onConsume - running per row once Bitween has accepted
    /// it. Proven by reading the flag back through the adapter, not by trusting that it ran.
    /// </summary>
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
        Assert.All(processed["rows"]!, r => Assert.True(r.Value<bool>("processed")));
    }

    // ---------------------------------------------------------------- setup

    async Task<int> CreateDataSourceAsync()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        var dataSource = new DataSource
        {
            Name = $"postgres-{Guid.NewGuid():N}",
            AdapterId = BusAdapters.PostgreSql,
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
        ["Host"] = _postgres.Host,
        ["Port"] = _postgres.Port.ToString(),
        ["Database"] = _postgres.Database,
        ["UserName"] = _postgres.User,
        ["Password"] = _postgres.Password,
        ["Schema"] = "public",
        ["MinPoolSize"] = "1",
        ["MaxPoolSize"] = "5",

        // Note @name, not :name - the colon collides with PostgreSQL's :: cast operator.
        ["Statements"] = $@"{{
            ""seededOrders"":             ""select * from {PostgreSqlDbFixture.Table} where id <= 25 order by id"",
            ""ordersForCustomer"":        ""select * from {PostgreSqlDbFixture.Table} where customer = @customer order by id"",
            ""ordersByCustomerFunction"": ""select * from orders_by_customer(@customer)"",
            ""processedOrders"":          ""select * from {PostgreSqlDbFixture.Table} where processed order by id"",
            ""insertOrder"":              ""insert into {PostgreSqlDbFixture.Table} (id, customer, amount) values (@id, @customer, @amount)"",
            ""insertOrderReturning"":     ""insert into {PostgreSqlDbFixture.Table} (id, customer, amount) values (@id, @customer, @amount) returning *"",

            ""earlyOrders"": {{
                ""sql"":          ""select * from {PostgreSqlDbFixture.Table} where id > @cursor and id <= 10 order by id"",
                ""cursorColumn"": ""id"",
                ""keyColumn"":    ""id""
            }},
            ""lateOrders"": {{
                ""sql"":          ""select * from {PostgreSqlDbFixture.Table} where id > @cursor and id > 20 order by id"",
                ""cursorColumn"": ""id"",
                ""keyColumn"":    ""id""
            }}
        }}",

        ["ReceiveMode"] = "incrementing",
        ["ReceiveStatement"] =
            $"select * from {PostgreSqlDbFixture.Table} where id > @cursor order by id limit 5",
        ["CursorColumn"] = "id",
        ["KeyColumn"] = "id",
        ["MarkProcessedStatement"] =
            $"update {PostgreSqlDbFixture.Table} set processed = true where id = @key",
        ["ReceiveBatchSize"] = "5"
    };

    async Task StartAdapterAsync()
    {
        var spec = new AdapterSpec
        {
            AdapterId = BusAdapters.PostgreSql,
            InstanceKey = _dataSourceId.ToString()
        };

        foreach (var kv in Properties()) spec.StartupValues[kv.Key] = kv.Value;

        await Host.StartExclusiveAsync(spec);
    }
}
