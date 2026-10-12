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

/// <summary>
/// A subscription that expects traffic and gets none: nothing fails, so without QuietSubscriptionsJob
/// nobody hears of it. The job runs here at the real time, against exchanges made by XchangeService.
/// </summary>
[Collection("Bitween")]
public class QuietSubscriptionsTests(BitweenFixture fixture)
{
    static readonly string[] DayNames = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

    // Every day, all day, UTC: the window is always open, so only the gap decides.
    static ExpectTraffic Always(int every = 5) => new() { EveryMinutes = every };

    [Fact]
    public async Task Silence_inside_the_window_is_raised_once_and_tells_the_notifier()
    {
        var (id, name, token) = await Watched(Always(15));

        await RunJob();
        await RunJob();

        var quietSince = await QuietSince(id);
        Assert.NotNull(quietSince);
        var sent = Assert.Single(Received(token));
        Assert.Equal("gone-quiet", sent.Event);
        Assert.False(sent.Success);
        Assert.Equal(id, sent.SubscriptionId);
        Assert.Equal(name, sent.SubscriptionName);
        Assert.Equal("Quiet doc " + token, sent.DocumentName);
        Assert.Equal($"No exchanges in the last 7 days; {name} expects at least one every 15 minutes, every day UTC.",
            sent.Exception);
        Assert.False(string.IsNullOrEmpty(sent.CorrelationId));

        // Logged, not recorded against an exchange: there is none.
        await using var scope = fixture.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<BitweenDbContext>()
            .Set<XchangeNotification>().AnyAsync(n => n.XchangeId == sent.Id));
    }

    [Fact]
    public async Task An_exchange_inside_the_gap_keeps_it_from_going_quiet()
    {
        var (id, _, token) = await Watched(Always(15));
        await Exchange(id);

        await RunJob();

        Assert.Null(await QuietSince(id));
        Assert.Empty(Received(token));
    }

    [Fact]
    public async Task The_last_exchange_is_named_when_it_goes_quiet()
    {
        var (id, name, token) = await Watched(Always());
        var last = await Exchange(id);

        // Ten minutes on, the five-minute gap has passed.
        await RunJob(DateTime.UtcNow.AddMinutes(10));

        var sent = Assert.Single(Received(token));
        Assert.Equal($"No exchanges since {last:yyyy-MM-dd HH:mm} UTC; {name} expects at least one every 5 minutes, every day UTC.",
            sent.Exception);
    }

    [Fact]
    public async Task Outside_the_window_silence_is_not_raised()
    {
        // Only on the day after tomorrow, so never today.
        var (id, _, token) = await Watched(new ExpectTraffic
            { EveryMinutes = 5, Days = [DayNames[((int)DateTime.UtcNow.DayOfWeek + 2) % 7]] });

        await RunJob();

        Assert.Null(await QuietSince(id));
        Assert.Empty(Received(token));
    }

    [Fact]
    public async Task An_exchange_after_it_went_quiet_clears_it_and_says_it_is_back()
    {
        var (id, _, token) = await Watched(Always());
        await RunJob();
        Assert.NotNull(await QuietSince(id));

        await Exchange(id);
        await RunJob();
        await RunJob();

        Assert.Null(await QuietSince(id));
        var sent = Received(token);
        Assert.Equal(["gone-quiet", "back"], sent.Select(s => s.Event));
        Assert.True(sent[1].Success);
        Assert.Matches(@"^Exchanges arriving again after \d+ minutes?\.$", sent[1].Exception);
    }

    [Fact]
    public async Task Traffic_coming_back_outside_the_window_still_clears_it()
    {
        var (id, _, token) = await Watched(Always());
        await RunJob();

        // The window moves away from now; the flag it raised stays until traffic returns.
        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var subscription = await db.Set<Subscription>().SingleAsync(s => s.Id == id);
            subscription.SetExpectTraffic(new ExpectTraffic
                { EveryMinutes = 5, Days = [DayNames[((int)DateTime.UtcNow.DayOfWeek + 2) % 7]] });
            await db.SaveChangesAsync();
        }
        Assert.NotNull(await QuietSince(id));

        await Exchange(id);
        await RunJob();

        Assert.Null(await QuietSince(id));
        Assert.Equal(["gone-quiet", "back"], Received(token).Select(s => s.Event));
    }

    [Fact]
    public async Task A_paused_or_inactive_subscription_is_not_raised()
    {
        var (paused, _, pausedToken) = await Watched(Always());
        var (inactive, _, inactiveToken) = await Watched(Always());
        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            (await db.Set<Subscription>().SingleAsync(s => s.Id == paused)).Pause();
            (await db.Set<Subscription>().SingleAsync(s => s.Id == inactive)).Inactive = true;
            await db.SaveChangesAsync();
        }

        await RunJob();

        Assert.Null(await QuietSince(paused));
        Assert.Null(await QuietSince(inactive));
        Assert.Empty(Received(pausedToken));
        Assert.Empty(Received(inactiveToken));
    }

    [Fact]
    public async Task Only_notifiers_on_failed_results_are_told()
    {
        var (id, _, token) = await Watched(Always());
        var successOnly = $"success-only-{Guid.NewGuid():N}";
        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var notifier = new Notifier($"Quiet success {Guid.NewGuid():N}")
            {
                HandlerId = nameof(NativeTestResponder), RunOnSuccessfulResult = true, RunOnSubscriptions = [id],
            };
            notifier.SetDictionaries(new Dictionary<string, string> { ["Body"] = successOnly });
            db.Add(notifier);
            await db.SaveChangesAsync();
        }

        await RunJob();

        Assert.Single(Received(token));
        Assert.Empty(Received(successOnly));
    }

    [Fact]
    public async Task The_expectation_is_saved_checked_and_read_back_with_quiet_since()
    {
        var (id, _, _) = await Watched(null);

        await using (var scope = fixture.CreateScope())
        {
            scope.Superuser();
            var get = await Read(scope, id);
            get.ExpectTraffic = new ExpectTraffic
                { EveryMinutes = 15, Days = ["thu", "Sun", "Mon", "Tue", "Wed"], From = "08:00", To = "22:00", TimeZone = "Asia/Riyadh" };
            await ActivatorUtilities.CreateInstance<Resources.Subscriptions.Update>(scope.ServiceProvider).Handle(id, get);
        }

        await using (var scope = fixture.CreateScope())
        {
            scope.Superuser();
            var read = await Read(scope, id);
            Assert.Equal(["Sun", "Mon", "Tue", "Wed", "Thu"], read.ExpectTraffic!.Days);
            Assert.Equal("Asia/Riyadh", read.ExpectTraffic.TimeZone);
            Assert.Null(read.QuietSince);

            read.ExpectTraffic.TimeZone = "Mars/Olympus";
            var refused = await Assert.ThrowsAsync<SWValidationException>(() => ActivatorUtilities
                .CreateInstance<Resources.Subscriptions.Update>(scope.ServiceProvider).Handle(id, read));
            Assert.Contains("isn't a time zone", refused.Message);
        }

        await using (var scope = fixture.CreateScope())
        {
            scope.Superuser();
            var read = await Read(scope, id);
            read.ExpectTraffic!.EveryMinutes = 4;
            var refused = await Assert.ThrowsAsync<SWValidationException>(() => ActivatorUtilities
                .CreateInstance<Resources.Subscriptions.Update>(scope.ServiceProvider).Handle(id, read));
            Assert.Contains("5 to", refused.Message);
        }
    }

    [Fact]
    public async Task Removing_the_expectation_clears_quiet_since()
    {
        var (id, _, _) = await Watched(Always());
        await RunJob();

        await using (var scope = fixture.CreateScope())
        {
            scope.Superuser();
            var get = await Read(scope, id);
            Assert.NotNull(get.QuietSince);
            get.ExpectTraffic = null;
            await ActivatorUtilities.CreateInstance<Resources.Subscriptions.Update>(scope.ServiceProvider).Handle(id, get);
        }

        Assert.Null(await QuietSince(id));
    }

    static async Task<SubscriptionGet> Read(AsyncServiceScope scope, int id) =>
        (SubscriptionGet)await ActivatorUtilities.CreateInstance<Resources.Subscriptions.Get>(scope.ServiceProvider).Handle(id);

    /// <summary>An active subscription expecting <paramref name="expect"/>, and a notifier on its failures.</summary>
    async Task<(int Id, string Name, string Token)> Watched(ExpectTraffic expect)
    {
        var token = $"quiet-{Guid.NewGuid():N}";
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var document = new Document(null, "Quiet doc " + token, DocumentFormat.Json);
        db.Add(document);
        await db.SaveChangesAsync();
        // A gateway type: a Receiving one would need schedules before an update is accepted.
        var subscription = new Subscription($"Orders {token}", document.Id, SubscriptionType.GatewayApiCall) { Inactive = false };
        subscription.SetExpectTraffic(expect);
        db.Add(subscription);
        await db.SaveChangesAsync();

        var notifier = new Notifier($"Quiet {token}")
        {
            HandlerId = nameof(NativeTestResponder), RunOnFailedResult = true, RunOnSubscriptions = [subscription.Id],
        };
        notifier.SetDictionaries(new Dictionary<string, string> { ["Body"] = token });
        db.Add(notifier);
        await db.SaveChangesAsync();
        scope.ServiceProvider.GetRequiredService<IInfolinkCache>().Revoke();
        return (subscription.Id, subscription.Name, token);
    }

    async Task<DateTime> Exchange(int subscriptionId)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var subscription = await db.Set<Subscription>().SingleAsync(s => s.Id == subscriptionId);
        var xchange = await scope.ServiceProvider.GetRequiredService<XchangeService>()
            .CreateXchange(subscription, new XchangeFile("{}"));
        await db.SaveChangesAsync();
        return xchange.StartedOn;
    }

    async Task RunJob(DateTime? at = null)
    {
        await using var scope = fixture.CreateScope();
        await ActivatorUtilities.CreateInstance<QuietSubscriptionsJob>(scope.ServiceProvider).Run(at ?? DateTime.UtcNow);
    }

    async Task<DateTime?> QuietSince(int id)
    {
        await using var scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<BitweenDbContext>().Set<Subscription>().AsNoTracking()
            .Where(s => s.Id == id).Select(s => s.QuietSince).SingleAsync();
    }

    static List<XchangeResultNotification> Received(string token) =>
        NativeTestResponder.Received.TryGetValue(token, out var sent)
            ? sent.Select(JsonConvert.DeserializeObject<XchangeResultNotification>).ToList()!
            : [];
}
