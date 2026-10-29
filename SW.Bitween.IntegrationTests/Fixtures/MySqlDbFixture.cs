using System;
using System.Threading.Tasks;
using MySqlConnector;
using Testcontainers.MySql;
using Xunit;

namespace SW.Bitween.IntegrationTests.Fixtures;

/// <summary>
/// A MySQL for the adapter tests to point at - deliberately its own container rather than the one
/// <see cref="BitweenFixture"/> runs for the application database.
///
/// A class fixture, not <see cref="IAsyncLifetime"/> on the test class: xUnit builds a new instance
/// of a test class per test method, so a container started there is a container per test.
///
/// The seed mirrors the PostgreSQL one object for object, because the point of having both is that
/// the shared core behaves identically and the differences show up where they are real - here, a
/// procedure that returns rows by SELECTing, which is the thing PostgreSQL cannot do.
/// </summary>
public class MySqlDbFixture : IAsyncLifetime
{
    public const string Table = "bitween_orders";

    MySqlContainer _container;

    /// <summary>Set when Docker is unavailable, so the tests skip rather than fail.</summary>
    public string Unavailable { get; private set; }

    public string Host => _container.Hostname;
    public int Port => _container.GetMappedPublicPort(3306);
    public string Database => "bitween_adapter";
    public string User => "bitween";
    public string Password => "bitween_pw";

    public async Task InitializeAsync()
    {
        try
        {
            _container = new MySqlBuilder()
                .WithImage("mysql:8.4")
                .WithDatabase(Database)
                .WithUsername(User)
                .WithPassword(Password)

                // Creating a FUNCTION requires SUPER while binary logging is on, because a
                // non-deterministic one would make the binary log unsafe to replay. Our user is
                // not SUPER and should not be, so the server is told to trust function creators -
                // which is the switch a DBA sets for exactly this, and is why the adapter's own
                // capability list does not promise that creating routines is something it can do.
                .WithCommand("--log-bin-trust-function-creators=1")
                .Build();

            await _container.StartAsync();
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

    public string AdminConnectionString =>
        $"Server={Host};Port={Port};Database={Database};User ID={User};Password={Password};" +
        "AllowUserVariables=true;AllowPublicKeyRetrieval=true";

    async Task SeedAsync()
    {
        await using var connection = new MySqlConnection(AdminConnectionString);
        await connection.OpenAsync();

        await ExecuteAsync(connection, $@"
            create table {Table} (
                id          int             primary key,
                customer    varchar(50),
                amount      decimal(10,2),
                created_at  timestamp       not null default current_timestamp,
                processed   tinyint(1)      not null default 0
            ) comment 'Customer orders'");

        for (var i = 1; i <= 25; i++)
            await ExecuteAsync(connection,
                $"insert into {Table} (id, customer, amount) " +
                $"values ({i}, '{(i % 2 == 0 ? "acme" : "globex")}', {i * 10}.50)");

        // A procedure that returns rows simply by SELECTing - no cursor to declare, nothing to
        // bind. This is the capability PostgreSQL declares false and MySQL declares true, and the
        // reason the two adapters are worth testing against the same shape of schema.
        await ExecuteAsync(connection, $@"
            create procedure orders_by_customer(in p_customer varchar(50))
            begin
                select * from {Table} where customer = p_customer order by id;
            end");

        // A procedure that returns a value through an OUT parameter rather than a result set.
        await ExecuteAsync(connection, $@"
            create procedure count_orders(in p_customer varchar(50), out p_total int)
            begin
                select count(*) into p_total from {Table} where customer = p_customer;
            end");

        // And a scalar function, which is a different object type in the catalog.
        await ExecuteAsync(connection, $@"
            create function total_for(p_customer varchar(50))
            returns decimal(12,2)
            deterministic
            reads sql data
            begin
                declare v_total decimal(12,2);
                select coalesce(sum(amount), 0) into v_total from {Table} where customer = p_customer;
                return v_total;
            end");
    }

    static async Task ExecuteAsync(MySqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
