using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using DotNet.Testcontainers.Containers;
using Testcontainers.MsSql;
using Testcontainers.MySql;
using Xunit;

namespace SW.Bitween.HttpTests;

/// <summary>
/// Bitween installed on each metadata database it supports besides PostgreSQL, which every other
/// test runs on: migrated from nothing, started as Program.Main starts it, then configured and
/// used over HTTP — an information type, a partner with a key, a gateway, a call through the queue
/// answered with its result, and the pages that list them. These databases were only ever checked
/// for model drift before.
/// </summary>
[Collection("Http")]
public class MetadataDatabaseTests
{
    [Fact]
    public Task A_fresh_install_on_MySql_migrates_starts_and_carries_a_gateway_call_end_to_end() =>
        RunAsync("MySql", new MySqlBuilder().WithImage("mysql:8.0").WithDatabase("bitween").Build(),
            mustStart: true);

    /// <summary>Skipped where SQL Server's image can't run — it is x64 only, and an Apple-silicon Docker can't start it.</summary>
    [SkippableFact]
    public Task A_fresh_install_on_MsSql_migrates_starts_and_carries_a_gateway_call_end_to_end() =>
        RunAsync("MsSql", new MsSqlBuilder().Build(), mustStart: false);

    static async Task RunAsync(string databaseType, DockerContainer database, bool mustStart)
    {
        var rabbit = HttpFixture.NewRabbit();
        try
        {
            try
            {
                await database.StartAsync();
            }
            catch (Exception ex) when (!mustStart)
            {
                Skip.If(true, $"{databaseType} could not run here: {ex.Message}");
            }
            await rabbit.StartAsync();

            var connectionString = databaseType == "MsSql"
                ? ((IDatabaseContainer)database).GetConnectionString().Replace("Database=master", "Database=bitween")
                : ((IDatabaseContainer)database).GetConnectionString();
            await using var app = await HttpFixture.StartAsync(HttpFixture.Settings(databaseType, connectionString, rabbit,
                $"bitween-{databaseType.ToLowerInvariant()}-{Guid.NewGuid():N}"));

            using var admin = await HttpFixture.AdminOf(app);
            var (_, urlName, _, partnerId, subscriptionId) = await Api.GatewayAsync(admin);
            var key = await Api.NewKeyAsync(admin);
            await Api.SetKeysAsync(admin, partnerId, ("main", key));

            var call = await Api.CallSyncAsync(HttpFixture.ClientOf(app), urlName, key);
            Assert.Equal(HttpStatusCode.OK, call.StatusCode);

            foreach (var page in new[] { "/api/documents", "/api/partners", "/api/subscriptions", "/api/apigateways",
                         "/api/accounts", "/api/roles", $"/api/audit?entityName=Partner&entityKey={partnerId}" })
                await Api.Json(await admin.GetAsync(page));
            Assert.Contains(subscriptionId.ToString(),
                (await Api.Json(await admin.GetAsync("/api/subscriptions"))).ToJsonString());
        }
        finally
        {
            await Task.WhenAll(database.DisposeAsync().AsTask(), rabbit.DisposeAsync().AsTask());
        }
    }
}
