using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Domain;
using SW.Bitween.Services.XchangeStates;
using Testcontainers.MsSql;
using Testcontainers.MySql;
using Xunit;

namespace SW.Bitween.HttpTests;

/// <summary>
/// Bitween installed on each metadata database it supports besides PostgreSQL, which every other
/// test runs on: migrated from nothing, started as Program.Main starts it, then configured and
/// used over HTTP - an information type, a partner with a key, a gateway, a call through the queue
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

    /// <summary>Skipped where SQL Server's image can't run - it is x64 only, and an Apple-silicon Docker can't start it.</summary>
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

            // The metrics are written and read with grouped queries each database translates its own
            // way: write this node's counts, then ask every metrics page.
            await MetricsHttpTests.FlushAsync(app);
            foreach (var page in MetricsHttpTests.Pages(subscriptionId))
                await Api.Json(await admin.GetAsync(page));
            var overview = await Api.Json(await admin.GetAsync("/api/metrics/overview"));
            Assert.True((int)overview["last24h"]!["total"]! >= 1, overview.ToJsonString());

            // The exchange status fill is a run of updates each database translates its own way too.
            await FillsExchangeStatusAsync(app, databaseType);

            // And once it's done the pages read the status columns, in queries of their own.
            XchangeStateReadiness.Forget();
            foreach (var status in new[] { "0", "1", "2", "3" })
            {
                var filters = $"filter={Uri.EscapeDataString($"StatusFilter:1:{status}")}" +
                              $"&filter={Uri.EscapeDataString("LatestOnly:1:true")}";
                await Api.Json(await admin.GetAsync($"/api/xchanges?{filters}&size=5"));
                await Api.Json(await admin.GetAsync($"/api/xchanges/summary?{filters}"));
            }
            // Not latest-only: the fill check above made the call's exchange a retry of itself.
            var success = await Api.Json(await admin.GetAsync("/api/xchanges/summary"));
            Assert.True((int)success["counts"]!["success"]! >= 1, success.ToJsonString());
            await Api.Json(await admin.GetAsync("/api/dashboard/failurestoacton"));
            await Api.Json(await admin.GetAsync("/api/dashboard/retrysummary"));

            // Its progress, for the Exchanges page and among Queue health's system jobs.
            var fill = await Api.Json(await admin.GetAsync("/api/xchanges/statefill"));
            Assert.Equal("Done", (string)fill["phase"]!);
            var jobs = (await Api.Json(await admin.GetAsync("/api/ops/background")))["jobs"]!.AsArray();
            Assert.Equal("Done", (string)jobs.Single(j => (string)j!["name"]! == "Exchange status fill")!["progress"]!["phase"]!);

            // Undoing the status columns' migration by hand, with the index the fill built still on them.
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
                var migrations = db.Database.GetMigrations().ToList();
                await db.GetService<IMigrator>()
                    .MigrateAsync(migrations[migrations.FindIndex(m => m.EndsWith("_XchangeState")) - 1]);
            }
        }
        finally
        {
            await Task.WhenAll(database.DisposeAsync().AsTask(), rabbit.DisposeAsync().AsTask());
        }
    }

    /// <summary>
    /// Labels the exchanges the call left as if they came from before the status columns: every one
    /// wrongly failed, and one retried without its flag. The fill must put all of it right.
    /// </summary>
    static async Task FillsExchangeStatusAsync(WebApplicationFactory<Web.Program> app, string databaseType)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        var ids = await db.Set<Xchange>().OrderBy(x => x.StartedOn).Select(x => x.Id).ToListAsync();
        Assert.NotEmpty(ids);
        var original = ids[0];
        var newest = ids[^1];
        await db.Set<Xchange>().ExecuteUpdateAsync(s => s
            .SetProperty(x => x.Status, (XchangeStatus?)XchangeStatus.Failed)
            .SetProperty(x => x.Retried, (bool?)null));
        await db.Set<Xchange>().Where(x => x.Id == newest)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RetryFor, original));
        await db.Set<XchangeStateFill>().ExecuteDeleteAsync();

        var phase = await scope.ServiceProvider.GetRequiredService<XchangeStateFiller>()
            .RunAsync(TimeSpan.FromMinutes(1), batchSize: 1, deployOverlap: TimeSpan.Zero);
        Assert.Equal(XchangeStateFillPhase.Ready, phase);
        var indexes = await db.Database.SqlQueryRaw<int>(databaseType == "MsSql"
            ? $"SELECT COUNT(*) AS Value FROM sys.indexes WHERE name = '{XchangeStateIndex.Name}' AND object_id = OBJECT_ID('[Xchanges]')"
            : $"SELECT COUNT(*) AS Value FROM information_schema.statistics WHERE table_schema = DATABASE() AND table_name = 'Xchanges' AND index_name = '{XchangeStateIndex.Name}'")
            .SingleAsync();
        // MySQL lists an index once per column.
        Assert.Equal(databaseType == "MsSql" ? 1 : 3, indexes);

        var exchanges = await db.Set<Xchange>().AsNoTracking().ToListAsync();
        var results = await db.Set<XchangeResult>().AsNoTracking().ToDictionaryAsync(r => r.Id);
        foreach (var xchange in exchanges)
        {
            var expected = !results.TryGetValue(xchange.Id, out var result) ? XchangeStatus.Processing
                : !result.Success ? XchangeStatus.Failed
                : result.ResponseBad ? XchangeStatus.BadResponse
                : (XchangeStatus?)null;
            Assert.Equal(expected, xchange.Status);
            Assert.Equal(xchange.Id == original ? true : (bool?)null, xchange.Retried);
        }
    }
}
