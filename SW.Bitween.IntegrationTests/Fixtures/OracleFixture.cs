using System;
using System.Threading.Tasks;
using Oracle.ManagedDataAccess.Client;
using Testcontainers.Oracle;
using Xunit;

namespace SW.Bitween.IntegrationTests.Fixtures;

/// <summary>
/// One Oracle, for the whole test class.
///
/// A class fixture rather than <see cref="IAsyncLifetime"/> on the test class itself, and the
/// distinction is not academic: xUnit builds a new instance of a test class for every test method,
/// so a container started in the class's own InitializeAsync is a container per test. Oracle takes
/// a minute to become healthy and the image is nearly five gigabytes, so that is the difference
/// between a two-minute run and one that fills the machine.
/// </summary>
public class OracleFixture : IAsyncLifetime
{
    public const string Table = "BITWEEN_ORDERS";
    public const string User = "bitween";
    public const string Password = "bitween_pw";
    public const string Service = "FREEPDB1";

    OracleContainer _container;

    public string Host => _container.Hostname;
    public int Port => _container.GetMappedPublicPort(1521);

    /// <summary>Set when Docker or the image is not available, so the tests skip rather than fail.</summary>
    public string Unavailable { get; private set; }

    public async Task InitializeAsync()
    {
        try
        {
            _container = new OracleBuilder()
                // Pinned, and Free rather than XE: XE is the older 21c line, and the data
                // dictionary columns this adapter reads (ALL_TAB_COLS.identity_column in
                // particular) want a version that has them.
                .WithImage("gvenzl/oracle-free:23-slim-faststart")
                .WithUsername(User)
                .WithPassword(Password)
                .Build();

            await _container.StartAsync();
            await SeedAsync();
        }
        catch (Exception ex)
        {
            // Recorded rather than thrown. Oracle is the one dependency in this suite that a
            // developer may not have pulled, and failing the whole collection over it would hide
            // every other result.
            Unavailable = ex.Message;
        }
    }

    public async Task DisposeAsync()
    {
        if (_container != null) await _container.DisposeAsync();
    }

    /// <summary>
    /// Built here rather than taken from <c>GetConnectionString()</c>. Testcontainers spells the
    /// service name for the XE image it defaults to (XEPDB1); this image is Free, which serves
    /// FREEPDB1, and the mismatch surfaces as ORA-50201 "failed to parse connect string" - which
    /// says nothing about the actual cause.
    /// </summary>
    public string AdminConnectionString =>
        $"User Id={User};Password={Password};Data Source={Host}:{Port}/{Service};Connection Timeout=30";

    async Task SeedAsync()
    {
        await using var connection = await OpenWithRetryAsync();

        await ExecuteAsync(connection, $@"
            create table {Table} (
                id          number(10)      not null primary key,
                customer    varchar2(50),
                amount      number(10,2),
                created_at  timestamp       default systimestamp,
                processed   char(1)         default 'N'
            )");

        await ExecuteAsync(connection, "create sequence BITWEEN_ORDER_SEQ start with 1000");

        for (var i = 1; i <= 25; i++)
            await ExecuteAsync(connection,
                $"insert into {Table} (id, customer, amount, processed) " +
                $"values ({i}, '{(i % 2 == 0 ? "acme" : "globex")}', {i * 10}.50, 'N')");

        await ExecuteAsync(connection, $@"
            create or replace procedure ORDERS_BY_CUSTOMER(
                p_customer in varchar2,
                p_result   out sys_refcursor
            ) as
            begin
                open p_result for select * from {Table} where customer = p_customer order by id;
            end;");

        await ExecuteAsync(connection, "commit");
    }

    /// <summary>
    /// The container is reported healthy once the database is open, but the APP_USER the image
    /// creates on first boot can be a moment behind that. A handful of retries is the difference
    /// between a reliable suite and one that fails on a cold machine.
    /// </summary>
    async Task<OracleConnection> OpenWithRetryAsync()
    {
        Exception last = null;

        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                var connection = new OracleConnection(AdminConnectionString);
                await connection.OpenAsync();
                return connection;
            }
            catch (Exception ex)
            {
                last = ex;
                await Task.Delay(TimeSpan.FromSeconds(5));
            }
        }

        throw new InvalidOperationException(
            $"Could not connect to the Oracle container at {Host}:{Port}/{Service}: {last?.Message}", last);
    }

    static async Task ExecuteAsync(OracleConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
