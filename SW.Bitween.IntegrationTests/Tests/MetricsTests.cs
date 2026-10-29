using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Metrics;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.Bitween.Services.Metrics;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// Metrics end to end against a real database: an exchange run through the pipeline lands in its
/// hourly row and its adapter's, its result says where and how it ran, and history from before
/// metrics existed is folded in exactly once however many times the backfill runs.
/// </summary>
[Collection("Bitween")]
public class MetricsTests(BitweenFixture fixture)
{
    private const string Responder = nameof(Adapters.NativeTestResponder);
    private static int _seq;
    private static string Unique(string prefix) => $"{prefix}-{Interlocked.Increment(ref _seq)}-{Guid.NewGuid():N}"[..40];

    async Task<int> SubscriptionAsync()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var doc = new Document(null, Unique("Metrics type"), DocumentFormat.Other);
        db.Add(doc);
        await db.SaveChangesAsync();
        var sub = new Subscription(Unique("Metrics job"), doc.Id) { Inactive = false, HandlerId = Responder };
        db.Add(sub);
        await db.SaveChangesAsync();
        return sub.Id;
    }

    async Task<string> RunExchangeAsync(int subscriptionId, Func<BitweenDbContext, string, Task> beforeProcessing = null)
    {
        string id;
        await using (var scope = fixture.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<IInfolinkCache>().Revoke();
            id = await scope.ServiceProvider.GetRequiredService<XchangeService>()
                .SubmitSubscriptionXchange(subscriptionId, new XchangeFile("hello", "m.txt"));
        }
        if (beforeProcessing != null)
            await using (var scope = fixture.CreateScope())
                await beforeProcessing(scope.ServiceProvider.GetRequiredService<BitweenDbContext>(), id);
        await using (var scope = fixture.CreateScope())
            await scope.ServiceProvider.GetRequiredService<XchangeService>()
                .Process("XchangeCreated", JsonConvert.SerializeObject(new { Id = id }));
        return id;
    }

    [Fact]
    public async Task A_processed_exchange_is_counted_for_its_subscription_and_its_handler()
    {
        var subscriptionId = await SubscriptionAsync();
        var xchangeId = await RunExchangeAsync(subscriptionId);
        await using var scope = fixture.CreateScope();
        await scope.ServiceProvider.GetRequiredService<MetricsFlusher>().FlushAsync(CancellationToken.None);
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var row = await db.Set<ExchangeStats>().AsNoTracking()
            .SingleAsync(s => s.SubscriptionId == subscriptionId && s.Node == MetricsRecorder.Node);
        Assert.Equal((1, 1, 0), (row.Total, row.Succeeded, row.Failed));
        Assert.Equal(5, row.BytesIn);

        var calls = await db.Set<AdapterStats>().AsNoTracking()
            .Where(s => s.AdapterId == Responder.ToLowerInvariant() && s.Role == "handler").SumAsync(s => s.Calls);
        Assert.True(calls >= 1);

        // The result says which node ran it and how long it took.
        var result = await db.Set<XchangeResult>().AsNoTracking().SingleAsync(r => r.Id == xchangeId);
        Assert.Equal(MetricsRecorder.Node, result.NodeName);
        Assert.NotNull(result.DurationMs);
    }

    [Fact]
    public async Task History_is_folded_in_once_however_many_times_the_backfill_runs()
    {
        var subscriptionId = await SubscriptionAsync();
        var past = MetricsRecorder.HourOf(DateTime.UtcNow).AddHours(-3).AddMinutes(20);
        // An exchange from before metrics were recorded: started three hours ago, finished then.
        await RunExchangeAsync(subscriptionId, async (db, id) =>
            await db.Set<Xchange>().Where(x => x.Id == id).ExecuteUpdateAsync(u => u.SetProperty(x => x.StartedOn, past)));
        await using (var scope = fixture.CreateScope())
            await scope.ServiceProvider.GetRequiredService<BitweenDbContext>().Set<ExchangeStats>()
                .Where(s => s.SubscriptionId == subscriptionId).ExecuteDeleteAsync();

        await using var services = fixture.CreateScope();
        var backfill = new MetricsBackfill(services.ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
            new BitweenOptions { MetricsBackfillDays = 1 }, NullLogger<MetricsBackfill>.Instance);
        MetricsBackfill.PauseBetweenHours = TimeSpan.Zero;
        await backfill.RunAsync(CancellationToken.None);
        await backfill.RunAsync(CancellationToken.None);

        await using var check = fixture.CreateScope();
        var rows = await check.ServiceProvider.GetRequiredService<BitweenDbContext>().Set<ExchangeStats>().AsNoTracking()
            .Where(s => s.SubscriptionId == subscriptionId && s.Node == ExchangeStats.HistoryNode).ToListAsync();
        var row = Assert.Single(rows);
        Assert.Equal(MetricsRecorder.HourOf(past), DateTime.SpecifyKind(row.Hour, DateTimeKind.Utc));
        Assert.Equal((1, 1), (row.Total, row.Succeeded));
    }
}
