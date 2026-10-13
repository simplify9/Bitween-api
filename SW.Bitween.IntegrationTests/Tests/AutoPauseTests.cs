using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using SW.Bitween.Domain;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// A subscription set to pause after some number of failed deliveries in a row stops sending to a
/// target that is down, and holds what arrives meanwhile. One not set to never does.
/// </summary>
[Collection("Bitween")]
public class AutoPauseTests(BitweenFixture fixture)
{
    private const string Responder = nameof(Adapters.NativeTestResponder);
    private const string Missing = "native:no-such-handler";

    private static int _seq;
    private static string Unique(string prefix) => $"{prefix}-{Interlocked.Increment(ref _seq)}";

    private async Task<int> NewSubscription(int? autoPauseAfter, int? retryPolicyId = null)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var doc = new Document(null, Unique("Auto-pause type"), DocumentFormat.Json);
        db.Add(doc);
        await db.SaveChangesAsync();

        var sub = new Subscription(Unique("Auto-pause sub"), doc.Id, SubscriptionType.GatewayApiCall)
            { Inactive = false, HandlerId = Missing, AutoPauseAfterFailures = autoPauseAfter };
        if (retryPolicyId != null) sub.SetRetryPolicy(retryPolicyId, null);
        db.Add(sub);
        await db.SaveChangesAsync();
        return sub.Id;
    }

    private async Task SetHandler(int subscriptionId, string handlerId)
    {
        await using var scope = fixture.CreateScope();
        await scope.ServiceProvider.GetRequiredService<BitweenDbContext>().Set<Subscription>()
            .Where(s => s.Id == subscriptionId)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.HandlerId, handlerId));
    }

    /// <summary>Submits one exchange to the subscription and processes it.</summary>
    private async Task<string> Deliver(int subscriptionId)
    {
        string xchangeId;
        await using (var scope = fixture.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<IInfolinkCache>().Revoke();
            xchangeId = await scope.ServiceProvider.GetRequiredService<XchangeService>()
                .SubmitSubscriptionXchange(subscriptionId, new XchangeFile("{}"));
        }

        await using (var scope = fixture.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<XchangeService>()
                .Process("XchangeCreated", JsonConvert.SerializeObject(new { Id = xchangeId }));
        }

        return xchangeId;
    }

    private async Task<Subscription> Reload(int subscriptionId)
    {
        await using var scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<BitweenDbContext>()
            .Set<Subscription>().AsNoTracking().SingleAsync(s => s.Id == subscriptionId);
    }

    private async Task<int> PauseEvents(int subscriptionId)
    {
        await using var scope = fixture.CreateScope();
        var bodies = await scope.ServiceProvider.GetRequiredService<BitweenDbContext>().Set<OutboxMessage>()
            .AsNoTracking().Where(m => m.MessageType == nameof(SubscriptionAutoPausedEvent))
            .Select(m => m.Body).ToListAsync();
        return bodies.Count(b => JsonConvert.DeserializeObject<SubscriptionAutoPausedEvent>(b)!.SubscriptionId == subscriptionId);
    }

    [Fact]
    public async Task A_subscription_not_set_to_pause_never_does()
    {
        var id = await NewSubscription(null);

        for (var i = 0; i < 3; i++) await Deliver(id);

        var sub = await Reload(id);
        Assert.Null(sub.PausedOn);
        Assert.Equal(0, sub.ConsecutiveFailures);
    }

    [Fact]
    public async Task It_pauses_on_the_failure_that_reaches_the_limit_and_says_so_once()
    {
        var id = await NewSubscription(2);

        await Deliver(id);
        var afterOne = await Reload(id);
        Assert.Null(afterOne.PausedOn);
        Assert.Equal(1, afterOne.ConsecutiveFailures);
        Assert.NotNull(afterOne.LastException);

        await Deliver(id);
        var afterTwo = await Reload(id);
        Assert.NotNull(afterTwo.PausedOn);
        Assert.True(afterTwo.PausedAutomatically);

        // Already paused: a later failure doesn't pause it again or send a second alert.
        await Deliver(id);
        Assert.Equal(afterTwo.PausedOn, (await Reload(id)).PausedOn);
        Assert.Equal(1, await PauseEvents(id));
    }

    [Fact]
    public async Task A_delivery_that_goes_through_starts_the_count_again()
    {
        var id = await NewSubscription(2);

        await Deliver(id);
        await SetHandler(id, Responder);
        await Deliver(id);
        Assert.Equal(0, (await Reload(id)).ConsecutiveFailures);

        await SetHandler(id, Missing);
        await Deliver(id);

        var sub = await Reload(id);
        Assert.Null(sub.PausedOn);
        Assert.Equal(1, sub.ConsecutiveFailures);
    }

    [Fact]
    public async Task Resuming_after_an_automatic_pause_starts_the_count_again()
    {
        var id = await NewSubscription(1);
        await Deliver(id);
        Assert.True((await Reload(id)).PausedAutomatically);

        await using (var scope = fixture.CreateScope())
        {
            scope.Superuser();
            await ActivatorUtilities.CreateInstance<Resources.Subscriptions.Pause>(scope.ServiceProvider)
                .Handle(id, new SubscriptionPause());
        }

        var sub = await Reload(id);
        Assert.Null(sub.PausedOn);
        Assert.False(sub.PausedAutomatically);
        Assert.Equal(0, sub.ConsecutiveFailures);
        Assert.Null(sub.LastException);
    }

    /// <summary>
    /// The policy's alert channel is the place already set up to hear that deliveries stopped; the
    /// subscription's own rows ticked for failures want to hear it too. A channel that is both hears
    /// once, and a redelivered event tells nobody twice.
    /// </summary>
    [Fact]
    public async Task The_pause_reaches_the_policys_alert_channel_and_the_channels_ticked_for_failures_once_each()
    {
        var policyToken = Unique("pause-policy");
        var rowToken = Unique("pause-row");
        var successToken = Unique("pause-success-only");
        int policyChannel, rowChannel, successChannel, policyId;
        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            NotificationChannel Channel(string token) => new()
            {
                Name = Unique("Auto-pause channel"), HandlerId = Responder,
                HandlerProperties = new Dictionary<string, string> { ["Body"] = token }
            };
            var channels = new[] { Channel(policyToken), Channel(rowToken), Channel(successToken) };
            db.AddRange(channels);
            await db.SaveChangesAsync();
            (policyChannel, rowChannel, successChannel) = (channels[0].Id, channels[1].Id, channels[2].Id);

            var policy = new RetryPolicy { Name = Unique("Auto-pause policy"), AlertChannelId = policyChannel };
            db.Add(policy);
            await db.SaveChangesAsync();
            policyId = policy.Id;
        }

        var id = await NewSubscription(1, policyId);
        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var sub = await db.Set<Subscription>().SingleAsync(s => s.Id == id);
            sub.SetNotifications([
                new SubscriptionNotification { ChannelId = policyChannel, OnFailure = true },
                new SubscriptionNotification { ChannelId = rowChannel, OnFailure = true },
                new SubscriptionNotification { ChannelId = successChannel, OnSuccess = true }
            ]);
            await db.SaveChangesAsync();
            scope.ServiceProvider.GetRequiredService<IInfolinkCache>().Revoke();
        }
        var xchangeId = await Deliver(id);

        var paused = new SubscriptionAutoPausedEvent
            { SubscriptionId = id, XchangeId = xchangeId, Failures = 1, OccurredOn = DateTime.UtcNow };
        for (var delivery = 0; delivery < 2; delivery++)
        {
            await using var scope = fixture.CreateScope();
            await ActivatorUtilities.CreateInstance<AutoPauseAlertService>(scope.ServiceProvider).Process(paused);
        }

        Assert.Equal(1, Adapters.NativeTestResponder.Deliveries.GetValueOrDefault(policyToken));
        Assert.Equal(1, Adapters.NativeTestResponder.Deliveries.GetValueOrDefault(rowToken));
        Assert.Equal(0, Adapters.NativeTestResponder.Deliveries.GetValueOrDefault(successToken));

        await using (var scope = fixture.CreateScope())
        {
            var sent = await scope.ServiceProvider.GetRequiredService<BitweenDbContext>()
                .Set<XchangeNotification>().AsNoTracking()
                .Where(n => n.XchangeId == xchangeId && n.NotifierName == XchangeNotification.AutoPauseAlertName)
                .ToListAsync();
            Assert.Equal(new int?[] { policyChannel, rowChannel }, sent.Select(n => n.ChannelId).OrderBy(c => c));
            Assert.All(sent, n => Assert.True(n.Success, n.Exception));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    public async Task A_limit_outside_one_to_a_thousand_is_refused(int limit)
    {
        var id = await NewSubscription(null);
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var get = (SubscriptionGet)await ActivatorUtilities
            .CreateInstance<Resources.Subscriptions.Get>(scope.ServiceProvider).Handle(id);
        get.AutoPauseAfterFailures = limit;

        await Assert.ThrowsAsync<SW.PrimitiveTypes.SWValidationException>(() => ActivatorUtilities
            .CreateInstance<Resources.Subscriptions.Update>(scope.ServiceProvider).Handle(id, get));
    }
}
