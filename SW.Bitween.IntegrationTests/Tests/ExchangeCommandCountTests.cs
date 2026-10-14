using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using SW.Bitween.Controllers;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Gateway;
using SW.Bitween.IntegrationTests.Adapters;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.Bitween.Services.Metrics;
using SW.PrimitiveTypes;
using Xunit;
using Xunit.Abstractions;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// The database commands one synchronous gateway call costs, from the gateway's first query to the
/// answer: creation, processing, the result lane and the sync wait. This is the number finding 4 of
/// docs/design/processing-performance.md works down, phase by phase, so it is held to its budget
/// here and a change that adds a round trip to every exchange fails a test rather than a customer.
/// </summary>
/// <remarks>
/// Counted the way production counts it, through <see cref="DbCommandTally"/> into the exchange's
/// metrics row. The fixture runs no bus consumers, so the test plays the worker and the result lane
/// itself, each in its own scope as the consumer would, while the call waits.
/// </remarks>
[Collection("Bitween")]
public class ExchangeCommandCountTests(BitweenFixture fixture, ITestOutputHelper output)
{
    /// <summary>
    /// The sync path with a native handler, no mapper, no notifications, no duplicate window, no
    /// idempotency key, auto-pause off, caches warm. 13 before finding 4; 9 once the gateway, its
    /// partners and keys come from the cache, the exchange loads with its has-result flag and the
    /// retry budget is asked only of policies that have one (phase 1); 7 once published outbox rows
    /// are deleted in batches off the exchange's path instead of updated inline (phase 2); 5 once the
    /// result lane only hears of results something notifies on (phase 3); 4 once the sync wait reads
    /// the result in the poll that finds it (phase 4). Promoted properties ride in the creation
    /// batch, and cost no command of their own.
    /// </summary>
    private const int SyncCallBudget = 4;

    [Fact]
    public async Task A_sync_gateway_call_stays_within_its_database_command_budget()
    {
        var (urlName, key, subscriptionId) = await GatewayWithKeyAsync();

        // Warms the caches and spends the key's last-used write, which is once per five minutes.
        await SyncCallAsync(urlName, key, subscriptionId);
        var recorder = fixture.App.Services.GetRequiredService<MetricsRecorder>();
        recorder.Drain();

        var answer = await SyncCallAsync(urlName, key, subscriptionId);
        Assert.Equal(200, (answer as ContentResult)?.StatusCode);

        var rows = recorder.Drain().Exchanges.Where(r => r.SubscriptionId == subscriptionId).ToList();
        var commands = rows.Sum(r => r.DbCommands);
        output.WriteLine($"Database commands for one sync gateway call: {commands}");

        Assert.Equal(1, rows.Sum(r => r.Total));
        Assert.InRange(commands, 1, SyncCallBudget);
    }

    private async Task<IActionResult> SyncCallAsync(string urlName, string key, int subscriptionId)
    {
        var call = Task.Run(async () =>
        {
            await using var scope = fixture.CreateScope();
            scope.ServiceProvider.GetRequiredService<RequestContext>().Set(
                new ClaimsPrincipal(new ClaimsIdentity()),
                [new RequestValue("partnerkey", key, RequestValueType.HttpHeader)]);
            var controller = ActivatorUtilities.CreateInstance<GatewayController>(scope.ServiceProvider);
            controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
            controller.HttpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{\"order\":1}"));
            return await controller.Post($"{urlName}/sync");
        });

        var xchangeId = await WaitForUnprocessedExchangeAsync(subscriptionId, call);
        if (xchangeId == null) return await call;

        var message = JsonConvert.SerializeObject(new { Id = xchangeId });
        await using (var worker = fixture.CreateScope())
            await worker.ServiceProvider.GetRequiredService<XchangeService>().Process("commands-test", message);
        // The result lane runs only when the worker queued it a message. The fixture leaves published
        // outbox rows in place until a test flushes them, so the row says whether it did.
        if (await ResultEventQueuedAsync(xchangeId))
            await using (var resultLane = fixture.CreateScope())
                await resultLane.ServiceProvider.GetRequiredService<XchangeService>()
                    .Process("commands-test" + XchangeService.ResultQueueSuffix, message);

        return await call;
    }

    private async Task<bool> ResultEventQueuedAsync(string xchangeId)
    {
        await using var scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<BitweenDbContext>().Set<OutboxMessage>().AsNoTracking()
            .AnyAsync(m => m.MessageType.EndsWith(XchangeService.ResultQueueSuffix) && m.Body.Contains(xchangeId));
    }

    private async Task<string> WaitForUnprocessedExchangeAsync(int subscriptionId, Task call)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline && !call.IsCompleted)
        {
            await using var scope = fixture.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var id = await db.Set<Xchange>().AsNoTracking()
                .Where(x => x.SubscriptionId == subscriptionId && !db.Set<XchangeResult>().Any(r => r.Id == x.Id))
                .Select(x => x.Id)
                .FirstOrDefaultAsync();
            if (id != null) return id;
            await Task.Delay(50);
        }

        return null;
    }

    private async Task<(string UrlName, string Key, int SubscriptionId)> GatewayWithKeyAsync()
    {
        var urlName = $"commands-{Guid.NewGuid():N}";
        var key = Guid.NewGuid().ToString("N");

        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var partner = new Partner($"Commands partner {Guid.NewGuid():N}");
        partner.SetApiCredentials([new ApiCredential("load", key)]);
        var document = new Document(null, $"Commands doc {Guid.NewGuid():N}", DocumentFormat.Json);
        db.AddRange(partner, document);
        await db.SaveChangesAsync();

        var subscription = new Subscription($"Commands sub {Guid.NewGuid():N}", document.Id,
            SubscriptionType.GatewayApiCall) { Inactive = false, HandlerId = nameof(NativeTestResponder) };
        subscription.SetDictionaries(
            new Dictionary<string, string>(), new Dictionary<string, string>(), new Dictionary<string, string>(),
            new Dictionary<string, string>(), new Dictionary<string, string>());
        db.Add(subscription);
        await db.SaveChangesAsync();

        db.Add(new ApiGateway
        {
            Name = $"Commands gateway {Guid.NewGuid():N}",
            UrlName = urlName,
            Partners = [new ApiGatewayPartner { PartnerId = partner.Id, SubscriptionId = subscription.Id }],
        });
        await db.SaveChangesAsync();
        scope.ServiceProvider.GetRequiredService<IInfolinkCache>().Revoke();
        return (urlName, key, subscription.Id);
    }
}
