using System;
using System.Threading.Tasks;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace SW.Bitween.IntegrationTests.Fixtures;

/// <summary>
/// A PostgreSQL for the adapter tests to point at - deliberately its own container rather than the
/// one <see cref="BitweenFixture"/> runs for the application database.
///
/// Sharing that one would work and would be faster, but it would mean the adapter's test schema
/// lives beside Bitween's own tables, so a migration change could break these tests and a bad
/// statement here could touch application data. The container costs a few seconds.
///
/// A class fixture, not <see cref="IAsyncLifetime"/> on the test class: xUnit builds a new instance
/// of a test class per test method, so a container started there is a container per test.
/// </summary>
public class PostgreSqlDbFixture : IAsyncLifetime
{
    public const string Table = "bitween_orders";

    PostgreSqlContainer _container;

    /// <summary>Set when Docker is unavailable, so the tests skip rather than fail.</summary>
    public string Unavailable { get; private set; }

    public string Host => _container.Hostname;
    public int Port => _container.GetMappedPublicPort(5432);
    public string Database => "bitween_adapter";
    public string User => "bitween";
    public string Password => "bitween_pw";

    public async Task InitializeAsync()
    {
        try
        {
            _container = new PostgreSqlBuilder()
                .WithImage("postgres:16-alpine")
                .WithDatabase(Database)
                .WithUsername(User)
                .WithPassword(Password)
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
        $"Host={Host};Port={Port};Database={Database};Username={User};Password={Password}";

    async Task SeedAsync()
    {
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync();

        await ExecuteAsync(connection, $@"
            create table {Table} (
                id          integer         primary key,
                customer    varchar(50),
                amount      numeric(10,2),
                created_at  timestamptz     not null default now(),
                processed   boolean         not null default false
            )");

        await ExecuteAsync(connection, "comment on table bitween_orders is 'Customer orders'");
        await ExecuteAsync(connection, "create sequence bitween_order_seq start with 1000");

        for (var i = 1; i <= 25; i++)
            await ExecuteAsync(connection,
                $"insert into {Table} (id, customer, amount) " +
                $"values ({i}, '{(i % 2 == 0 ? "acme" : "globex")}', {i * 10}.50)");

        // A set-returning function: PostgreSQL's answer to Oracle's REF CURSOR, and queried with
        // SELECT rather than CALL - which is exactly the difference the capability list declares.
        await ExecuteAsync(connection, $@"
            create or replace function orders_by_customer(p_customer varchar)
            returns setof {Table}
            language sql
            as $$ select * from {Table} where customer = p_customer order by id $$");

        // And a real PROCEDURE, to prove CALL works and that it is not the thing that returns rows.
        await ExecuteAsync(connection, $@"
            create or replace procedure mark_all_processed(p_customer varchar)
            language sql
            as $$ update {Table} set processed = true where customer = p_customer $$");
    }

    static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
