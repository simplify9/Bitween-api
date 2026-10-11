using System;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using DotNet.Testcontainers.Builders;
using Testcontainers.MsSql;
using Xunit;

namespace SW.Bitween.IntegrationTests.Fixtures;

/// <summary>
/// A SQL Server for the adapter tests to point at - deliberately its own container rather than the
/// one <see cref="BitweenFixture"/> runs for the application database.
///
/// A class fixture, not <see cref="IAsyncLifetime"/> on the test class: xUnit builds a new instance
/// of a test class per test method, so a container started there is a container per test.
///
/// The image is the 2022 developer edition, which is what Testcontainers defaults to and what runs
/// on both x64 and, through emulation, Apple silicon. It is around 1.5 GB - an order of magnitude
/// less than Oracle's, so unlike those these run on every pass rather than nightly.
/// </summary>
public class SqlServerDbFixture : IAsyncLifetime
{
    public const string Table = "bitween_orders";

    MsSqlContainer _container;

    /// <summary>Set when Docker is unavailable, so the tests skip rather than fail.</summary>
    public string Unavailable { get; private set; }

    public string Host => _container.Hostname;
    public int Port => _container.GetMappedPublicPort(1433);

    /// <summary>
    /// A database of our own rather than the container's master. Discovery filters by schema and
    /// master is full of the engine's own objects, so a test asserting on what it can see would be
    /// asserting about Microsoft's schema as much as ours.
    /// </summary>
    public string Database => "bitween_adapter";

    public string User => "sa";
    public string Password => "yourStrong(!)Password";

    public async Task InitializeAsync()
    {
        try
        {
            _container = new MsSqlBuilder()
                // Pinned, and not to Testcontainers' default. That default is 2019, which has no
                // arm64 image and no emulation path - it exits the moment it starts on an Apple
                // silicon machine, which surfaces as "container is not running" rather than as
                // anything about the architecture. 2022 is the first release Microsoft publishes
                // for arm64, and it runs on both.
                .WithImage("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04")
                .WithPassword(Password)

                // Wait on the PORT, not on the built-in sqlcmd probe. SQL Server listens well
                // before it will accept a login, and under emulation on Apple silicon the gap is
                // long enough that the default readiness check gives up and the container is torn
                // down - which surfaces as "container is not running" rather than as a timeout.
                .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(1433))
                .Build();

            await _container.StartAsync();
            await WaitForLoginsAsync();
            await SeedAsync();
        }
        catch (Exception ex)
        {
            Unavailable = ex.Message;
        }
    }

    public async Task DisposeAsync()
    {
        if (_container != null) await _container.DisposeAsync();
    }

    /// <summary>
    /// TrustServerCertificate because the container presents a self-signed certificate, which is
    /// the same reason the adapter offers the setting at all.
    /// </summary>
    public string AdminConnectionString =>
        $"Server={Host},{Port};Database={Database};User ID={User};Password={Password};" +
        "Encrypt=True;TrustServerCertificate=True";

    string MasterConnectionString =>
        $"Server={Host},{Port};Database=master;User ID={User};Password={Password};" +
        "Encrypt=True;TrustServerCertificate=True";

    /// <summary>
    /// Polls until a login succeeds. The port opening only means the process is up; recovery of
    /// master, msdb and tempdb runs after that, and a connection during it is refused with "not
    /// currently available". Two minutes, because emulation is slow and a flaky skip reads as a
    /// broken adapter.
    /// </summary>
    async Task WaitForLoginsAsync()
    {
        var deadline = DateTime.UtcNow.AddMinutes(2);
        while (true)
        {
            try
            {
                await using var probe = new SqlConnection(MasterConnectionString);
                await probe.OpenAsync();
                return;
            }
            catch when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
        }
    }

    async Task SeedAsync()
    {
        await using (var master = new SqlConnection(MasterConnectionString))
        {
            await master.OpenAsync();
            await ExecuteAsync(master, $"create database [{Database}]");
        }

        await using var connection = new SqlConnection(AdminConnectionString);
        await connection.OpenAsync();

        // A schema of its own, so the discovery tests can prove the schema filter does something -
        // everything would otherwise be dbo, where a filter that did nothing would still pass.
        await ExecuteAsync(connection, "create schema sales");

        await ExecuteAsync(connection, $@"
            create table sales.{Table} (
                id          int             not null primary key,
                customer    nvarchar(50)    null,
                amount      decimal(10,2)   null,
                created_at  datetime2(3)    not null constraint df_created default sysutcdatetime(),
                processed   bit             not null constraint df_processed default 0
            )");

        await ExecuteAsync(connection, $@"
            exec sys.sp_addextendedproperty
                @name = N'MS_Description', @value = N'Customer orders',
                @level0type = N'SCHEMA', @level0name = N'sales',
                @level1type = N'TABLE',  @level1name = N'{Table}'");

        await ExecuteAsync(connection, "create sequence sales.bitween_order_seq as bigint start with 1000");

        for (var i = 1; i <= 25; i++)
            await ExecuteAsync(connection,
                $"insert into sales.{Table} (id, customer, amount) " +
                $"values ({i}, '{(i % 2 == 0 ? "acme" : "globex")}', {i * 10}.50)");

        // A procedure that returns rows simply by SELECTing, like MySQL and unlike PostgreSQL.
        await ExecuteAsync(connection, $@"
            create procedure sales.orders_by_customer @p_customer nvarchar(50)
            as
            begin
                set nocount on;
                select * from sales.{Table} where customer = @p_customer order by id;
            end");

        // And one that answers through an OUT parameter instead.
        await ExecuteAsync(connection, $@"
            create procedure sales.count_orders @p_customer nvarchar(50), @p_total int output
            as
            begin
                set nocount on;
                select @p_total = count(*) from sales.{Table} where customer = @p_customer;
            end");

        // An inline table-valued function - the SQL Server shape a receive statement would select
        // from, and a different object type in the catalog from a scalar one.
        await ExecuteAsync(connection, $@"
            create function sales.orders_for(@p_customer nvarchar(50))
            returns table
            as
            return (select * from sales.{Table} where customer = @p_customer)");
    }

    static async Task ExecuteAsync(SqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
