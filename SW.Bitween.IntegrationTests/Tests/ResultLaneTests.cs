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
/// A finished exchange goes on its work group's result lane only when something will hear about
/// it: a notification of the subscription's, or a legacy notifier watching it, that covers the
/// outcome. The lane used to get every exchange and load it twice to find, mostly, nothing to do.
/// </summary>
[Collection("Bitween")]
public class ResultLaneTests(BitweenFixture fixture)
{
    private const string Missing = "native:no-such-handler";

    [Fact]
    public async Task An_exchange_nothing_notifies_on_is_not_sent_to_the_result_lane()
    {
        var subscriptionId = await NewSubscription(nameof(NativeTestResponder), notifications: []);

        var xchangeId = await DeliverAsync(subscriptionId);

        Assert.True(await HasResultAsync(xchangeId));
        Assert.False(await ResultEventQueuedAsync(xchangeId));
    }

    [Fact]
    public async Task A_notification_on_failure_hears_failures_and_not_successes()
    {
        var channelId = await NewChannel();
        NotifyOn notification = new(channelId, OnFailure: true);
        var working = await NewSubscription(nameof(NativeTestResponder), [notification]);
        var failing = await NewSubscription(Missing, [notification]);

        var succeeded = await DeliverAsync(working);
        var failed = await DeliverAsync(failing);

        Assert.False(await ResultEventQueuedAsync(succeeded));
        Assert.True(await ResultEventQueuedAsync(failed));
    }

    [Fact]
    public async Task A_legacy_notifier_watching_the_subscription_still_hears()
    {
        var subscriptionId = await NewSubscription(nameof(NativeTestResponder), notifications: []);
        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            db.Add(new Notifier($"Result lane notifier {Guid.NewGuid():N}")
            {
                HandlerId = nameof(NativeTestResponder),
                RunOnSuccessfulResult = true,
                RunOnSubscriptions = [subscriptionId],
            });
            await db.SaveChangesAsync();
            scope.ServiceProvider.GetRequiredService<IInfolinkCache>().Revoke();
        }

        var xchangeId = await DeliverAsync(subscriptionId);

        Assert.True(await ResultEventQueuedAsync(xchangeId));
    }

    private sealed record NotifyOn(int ChannelId, bool OnFailure = false, bool OnSuccess = false);

    private async Task<int> NewChannel()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var channel = new NotificationChannel
        {
            Name = $"Result lane channel {Guid.NewGuid():N}", HandlerId = nameof(NativeTestResponder),
            HandlerProperties = new Dictionary<string, string>()
        };
        db.Add(channel);
        await db.SaveChangesAsync();
        return channel.Id;
    }

    private async Task<int> NewSubscription(string handlerId, NotifyOn[] notifications)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var document = new Document(null, $"Result lane doc {Guid.NewGuid():N}", DocumentFormat.Json);
        db.Add(document);
        await db.SaveChangesAsync();

        var subscription = new Subscription($"Result lane sub {Guid.NewGuid():N}", document.Id,
            SubscriptionType.GatewayApiCall) { Inactive = false, HandlerId = handlerId };
        subscription.SetNotifications(notifications.Select(n => new SubscriptionNotification
            { ChannelId = n.ChannelId, OnFailure = n.OnFailure, OnSuccess = n.OnSuccess }).ToList());
        db.Add(subscription);
        await db.SaveChangesAsync();
        scope.ServiceProvider.GetRequiredService<IInfolinkCache>().Revoke();
        return subscription.Id;
    }

    private async Task<string> DeliverAsync(int subscriptionId)
    {
        string xchangeId;
        await using (var scope = fixture.CreateScope())
            xchangeId = await scope.ServiceProvider.GetRequiredService<XchangeService>()
                .SubmitSubscriptionXchange(subscriptionId, new XchangeFile("{}"));

        await using (var scope = fixture.CreateScope())
            await scope.ServiceProvider.GetRequiredService<XchangeService>()
                .Process("ResultLaneTest", JsonConvert.SerializeObject(new { Id = xchangeId }));
        return xchangeId;
    }

    private async Task<bool> HasResultAsync(string xchangeId)
    {
        await using var scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<BitweenDbContext>().Set<XchangeResult>()
            .AsNoTracking().AnyAsync(r => r.Id == xchangeId);
    }

    /// <summary>The fixture leaves published outbox rows in place until a test flushes them.</summary>
    private async Task<bool> ResultEventQueuedAsync(string xchangeId)
    {
        await using var scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<BitweenDbContext>().Set<OutboxMessage>().AsNoTracking()
            .AnyAsync(m => m.MessageType.EndsWith(XchangeService.ResultQueueSuffix) && m.Body.Contains(xchangeId));
    }
}
