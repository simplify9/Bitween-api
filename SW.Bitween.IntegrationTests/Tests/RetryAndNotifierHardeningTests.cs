using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using SW.Bitween.Domain;
using SW.Bitween.IntegrationTests.Adapters;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

[Collection("Bitween")]
public class RetryAndNotifierHardeningTests(BitweenFixture fixture)
{
    /// <summary>
    /// A scheduled retry whose run throws used to be deleted on the spot, so a moment's database
    /// trouble lost it for good. It is moved later instead, and dropped only after repeated failures.
    /// </summary>
    [Fact]
    public async Task A_retry_whose_run_fails_is_rescheduled_then_dropped_after_repeated_failures()
    {
        var id = $"failing-{Guid.NewGuid():N}"[..30];
        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            db.Add(new DelayedRetry { Id = id, On = DateTime.UtcNow.AddMinutes(-1) });
            await db.SaveChangesAsync();
        }

        await Fail(id);

        await using (var scope = fixture.CreateScope())
        {
            var row = await scope.ServiceProvider.GetRequiredService<BitweenDbContext>()
                .Set<DelayedRetry>().AsNoTracking().SingleAsync(r => r.Id == id);
            Assert.Equal(1, row.RunFailures);
            Assert.True(row.On > DateTime.UtcNow);
        }

        for (var i = 1; i < RetryJob.MaxRunFailures; i++) await Fail(id);

        await using var check = fixture.CreateScope();
        Assert.False(await check.ServiceProvider.GetRequiredService<BitweenDbContext>()
            .Set<DelayedRetry>().AnyAsync(r => r.Id == id));
    }

    private async Task Fail(string id)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var row = await db.Set<DelayedRetry>().AsNoTracking().SingleAsync(r => r.Id == id);
        await RetryJob.AfterRunFailure(db, row, new TimeoutException("the database went away"),
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
    }

    /// <summary>
    /// An outage used to mean one notification per failure. Within the quiet window only the first
    /// is sent; the rest are recorded as held back and counted into the next one that goes out.
    /// </summary>
    [Fact]
    public async Task A_burst_of_failures_sends_one_notification_and_counts_the_rest()
    {
        var token = $"quiet-{Guid.NewGuid():N}";
        var (subscriptionId, notifierId) = await NotifierOnFailures(token);

        var failures = new List<string>();
        for (var i = 0; i < 4; i++) failures.Add(await FailedExchange(subscriptionId));

        foreach (var id in failures) await ProcessResult(id);

        Assert.Equal(1, NativeTestResponder.Deliveries.GetValueOrDefault(token));

        await using var scope = fixture.CreateScope();
        var rows = await scope.ServiceProvider.GetRequiredService<BitweenDbContext>()
            .Set<XchangeNotification>().AsNoTracking()
            .Where(n => n.NotifierId == notifierId).ToListAsync();
        Assert.Equal(1, rows.Count(n => n.Success));
        Assert.Equal(3, rows.Count(n => n.Suppressed));
    }

    /// <summary>The result event delivered twice notifies once.</summary>
    [Fact]
    public async Task A_repeat_of_the_result_event_does_not_notify_twice()
    {
        var token = $"repeat-{Guid.NewGuid():N}";
        var (subscriptionId, _) = await NotifierOnFailures(token);
        var id = await FailedExchange(subscriptionId);

        await ProcessResult(id);
        await ProcessResult(id);

        Assert.Equal(1, NativeTestResponder.Deliveries.GetValueOrDefault(token));
    }

    private async Task ProcessResult(string xchangeId)
    {
        await using var scope = fixture.CreateScope();
        await scope.ServiceProvider.GetRequiredService<XchangeService>()
            .Process("Hardening" + XchangeService.ResultQueueSuffix, JsonConvert.SerializeObject(new { Id = xchangeId }));
    }

    private async Task<(int SubscriptionId, int NotifierId)> NotifierOnFailures(string token)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var document = new Document(null, $"Hardening {Guid.NewGuid():N}", DocumentFormat.Json);
        db.Add(document);
        await db.SaveChangesAsync();
        var subscription = new Subscription($"Hardening {Guid.NewGuid():N}", document.Id) { Inactive = false };
        db.Add(subscription);
        await db.SaveChangesAsync();

        var notifier = new Notifier($"Hardening {Guid.NewGuid():N}")
        {
            HandlerId = nameof(NativeTestResponder),
            RunOnFailedResult = true,
            RunOnSubscriptions = [subscription.Id],
        };
        notifier.SetDictionaries(new Dictionary<string, string> { ["Body"] = token });
        db.Add(notifier);
        await db.SaveChangesAsync();
        scope.ServiceProvider.GetRequiredService<IInfolinkCache>().Revoke();
        return (subscription.Id, notifier.Id);
    }

    private async Task<string> FailedExchange(int subscriptionId)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var subscription = await db.Set<Subscription>().SingleAsync(s => s.Id == subscriptionId);
        var xchange = await scope.ServiceProvider.GetRequiredService<XchangeService>()
            .CreateXchange(subscription, new XchangeFile("{}"));
        await db.SaveChangesAsync();
        db.Add(new XchangeResult(xchange.Id, null, null, exception: "System.Exception: the partner is down"));
        await db.SaveChangesAsync();
        return xchange.Id;
    }
}
