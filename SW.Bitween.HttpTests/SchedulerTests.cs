using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using Xunit;

namespace SW.Bitween.HttpTests;

/// <summary>
/// The scheduler for real: the hosted app runs Quartz as production does, and a receiving
/// subscription pulls from a partner's feed served on a real port. "Receive now" fires the job,
/// and a schedule fires on its own at its minute.
/// </summary>
[Collection("Http")]
public class SchedulerTests(HttpFixture fixture)
{
    sealed class Feed : IAsyncDisposable
    {
        readonly WebApplication app;
        int calls;
        public int Calls => calls;
        public string Url { get; }

        Feed(WebApplication app, string url) => (this.app, Url) = (app, url);

        public static async Task<Feed> StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            var app = builder.Build();
            Feed feed = null!;
            app.MapGet("/shipments", () =>
            {
                Interlocked.Increment(ref feed.calls);
                return Results.Json(new object[] { new { id = 1, carrier = "DHL" }, new { id = 2, carrier = "UPS" } });
            });
            await app.StartAsync();
            var url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
            return feed = new Feed(app, url + "/shipments");
        }

        public ValueTask DisposeAsync() => app.DisposeAsync();
    }

    static async Task<bool> EventuallyAsync(Func<Task<bool>> condition, TimeSpan within)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < within)
        {
            if (await condition()) return true;
            await Task.Delay(500);
        }
        return await condition();
    }

    async Task<int> CreateReceiverAsync(System.Net.Http.HttpClient admin, string feedUrl, int minute)
    {
        var documentId = await Api.CreateAsync(admin, "/api/documents", new { name = Api.Unique("Shipment"), documentFormat = "Json" });
        return await Api.CreateAsync(admin, "/api/subscriptions", new
        {
            name = Api.Unique("Shipment pull"),
            documentId,
            type = "Receiving",
            inactive = false,
            receiverId = "NativeHttpReceiver",
            receiverProperties = new[]
            {
                new { key = "Url", value = feedUrl },
                new { key = "Verb", value = "get" },
                new { key = "ContentType", value = "application/json" },
            },
            schedules = new[] { new { recurrence = "Hourly", days = 0, hours = 0, minutes = minute } },
        });
    }

    Task<ReceiveAttempt[]> AttemptsAsync(int subscriptionId) =>
        fixture.InDbAsync(db => db.Set<ReceiveAttempt>().AsNoTracking()
            .Where(a => a.SubscriptionId == subscriptionId).OrderBy(a => a.StartedOn).ToArrayAsync());

    [Fact]
    public async Task Receive_now_runs_the_job_which_pulls_the_feed_into_exchanges()
    {
        await using var feed = await Feed.StartAsync();
        using var admin = await fixture.AdminAsync();
        // A schedule that won't come round during the test.
        var subscriptionId = await CreateReceiverAsync(admin, feed.Url, (DateTime.Now.Minute + 30) % 60);

        await Api.Json(await admin.PostAsJsonAsync($"/api/subscriptions/{subscriptionId}/receivenow", new { }));

        Assert.True(await EventuallyAsync(async () => (await AttemptsAsync(subscriptionId)).Length == 1, TimeSpan.FromSeconds(60)),
            "receive now never ran the job");
        var attempt = (await AttemptsAsync(subscriptionId)).Single();
        Assert.Equal(ReceiveOutcome.Received, attempt.Outcome);
        Assert.Equal(2, attempt.ExchangeIds.Length);
        Assert.Equal(1, feed.Calls);
        Assert.Equal(2, await fixture.InDbAsync(db => db.Set<Xchange>().CountAsync(x => x.SubscriptionId == subscriptionId)));

        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.PostAsJsonAsync("/api/subscriptions/999999/receivenow", new { })).StatusCode);
    }

    [Fact]
    public async Task A_schedule_fires_on_its_own_at_its_minute()
    {
        await using var feed = await Feed.StartAsync();
        using var admin = await fixture.AdminAsync();
        // Far enough into the next minute that saving can't miss it.
        var at = DateTime.Now.AddSeconds(DateTime.Now.Second > 45 ? 120 : 60);
        var subscriptionId = await CreateReceiverAsync(admin, feed.Url, at.Minute);

        Assert.Equal(0, feed.Calls);
        Assert.True(await EventuallyAsync(async () => (await AttemptsAsync(subscriptionId)).Length > 0, TimeSpan.FromSeconds(150)),
            $"the {at:HH:mm} schedule never fired");
        Assert.Equal(ReceiveOutcome.Received, (await AttemptsAsync(subscriptionId))[0].Outcome);
        Assert.True(feed.Calls >= 1);
    }
}
