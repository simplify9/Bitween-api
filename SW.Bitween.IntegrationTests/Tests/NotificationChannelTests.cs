using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Domain;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// Notification channels — where notifications and retry alerts go, set up once in Settings and
/// picked by each place that notifies.
/// </summary>
[Collection("Bitween")]
public class NotificationChannelTests(BitweenFixture fixture)
{
    private const string Sentinel = "__private__";

    private static int _seq;
    private static string Unique(string prefix) => $"{prefix}-{Interlocked.Increment(ref _seq)}";

    private Dictionary<string, string> Smtp(string password = "", bool useTls = false) => new()
    {
        ["Host"] = "localhost",
        ["Port"] = fixture.MailHogSmtpPort.ToString(),
        ["UseTls"] = useTls ? "true" : "false",
        ["Password"] = password,
        ["From"] = "bitween@example.com",
        ["To"] = "ops@example.com",
        ["Subject"] = "{{ SubscriptionName }} finished",
        ["Body"] = "Exchange {{ Id }}"
    };

    private async Task<int> Create(NotificationChannelCreate model)
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        return (int)await ActivatorUtilities.CreateInstance<Resources.NotificationChannels.Create>(scope.ServiceProvider)
            .Handle(model);
    }

    private async Task Update(int id, NotificationChannelUpdate model)
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        await ActivatorUtilities.CreateInstance<Resources.NotificationChannels.Update>(scope.ServiceProvider)
            .Handle(id, model);
    }

    private async Task<NotificationChannelGet> Get(int id)
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        return (NotificationChannelGet)await ActivatorUtilities
            .CreateInstance<Resources.NotificationChannels.Get>(scope.ServiceProvider).Handle(id);
    }

    private async Task<NotificationChannel> Stored(int id)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        return await db.Set<NotificationChannel>().AsNoTracking().SingleAsync(c => c.Id == id);
    }

    private async Task<Subscription> SubscriptionNotifying(int channelId, bool onFailure = true, bool onSuccess = false)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var document = new Document(null, Unique("Channel doc"), DocumentFormat.Json);
        db.Add(document);
        await db.SaveChangesAsync();

        var subscription = new Subscription(Unique("Notifying sub"), document.Id);
        subscription.SetNotifications([
            new SubscriptionNotification { ChannelId = channelId, OnFailure = onFailure, OnSuccess = onSuccess }
        ]);
        db.Add(subscription);
        await db.SaveChangesAsync();
        // Written straight to the tables, so the cache result handling reads has not heard of them.
        scope.ServiceProvider.GetRequiredService<IInfolinkCache>().Revoke();
        return subscription;
    }

    // ─── Secrets ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_channel_password_is_masked_on_read_and_survives_being_saved_back()
    {
        var name = Unique("Masked channel");
        var id = await Create(new NotificationChannelCreate
        {
            Name = name, HandlerId = "NativeSmtpHandler", HandlerProperties = Smtp("hunter2", useTls: true)
        });

        var loaded = await Get(id);
        Assert.Equal(Sentinel, loaded.HandlerProperties["Password"]);
        Assert.Equal("localhost", loaded.HandlerProperties["Host"]);

        // What the settings dialog does when someone edits the subject: the password comes back as
        // the mask and must not be stored as one.
        loaded.HandlerProperties["Subject"] = "Edited";
        await Update(id, new NotificationChannelUpdate
        {
            Name = name, HandlerId = loaded.HandlerId, HandlerProperties = loaded.HandlerProperties
        });

        var stored = await Stored(id);
        Assert.Equal("hunter2", stored.HandlerProperties["Password"]);
        Assert.Equal("Edited", stored.HandlerProperties["Subject"]);
    }

    [Fact]
    public async Task Changing_the_handler_drops_a_masked_password_instead_of_handing_it_over()
    {
        var name = Unique("Swapped channel");
        var id = await Create(new NotificationChannelCreate
        {
            Name = name, HandlerId = "NativeSmtpHandler", HandlerProperties = Smtp("hunter2", useTls: true)
        });

        // Keeping the dots while pointing the channel somewhere else would otherwise send the stored
        // password to a destination the caller chose without ever seeing it.
        await Update(id, new NotificationChannelUpdate
        {
            Name = name,
            HandlerId = "NativeHttpHandler",
            HandlerProperties = new Dictionary<string, string> { ["Password"] = Sentinel, ["Url"] = "https://example.com" }
        });

        var stored = await Stored(id);
        Assert.False(stored.HandlerProperties.ContainsKey("Password"));
        Assert.Equal("https://example.com", stored.HandlerProperties["Url"]);
    }

    [Fact]
    public async Task A_mail_channel_with_a_password_and_no_encryption_is_refused()
    {
        var ex = await Assert.ThrowsAsync<SWValidationException>(() => Create(new NotificationChannelCreate
        {
            Name = Unique("Cleartext"), HandlerId = "NativeSmtpHandler", HandlerProperties = Smtp("hunter2")
        }));
        Assert.Contains("CHANNEL_PASSWORD_WITHOUT_TLS", ex.Message);

        // Encryption off is fine on its own; only the password must not travel in clear.
        await Create(new NotificationChannelCreate
        {
            Name = Unique("No password"), HandlerId = "NativeSmtpHandler", HandlerProperties = Smtp()
        });
    }

    [Fact]
    public async Task Two_channels_cannot_share_a_name()
    {
        var name = Unique("Taken");
        await Create(new NotificationChannelCreate { Name = name, HandlerId = "NativeSmtpHandler" });

        var ex = await Assert.ThrowsAsync<SWValidationException>(() =>
            Create(new NotificationChannelCreate { Name = name, HandlerId = "NativeSmtpHandler" }));
        Assert.Contains("NOTIFICATION_CHANNEL_NAME_TAKEN", ex.Message);
    }

    // ─── Using a channel ─────────────────────────────────────────────────────────

    [Fact]
    public async Task A_channel_in_use_cannot_be_deleted_and_names_who_uses_it()
    {
        var id = await Create(new NotificationChannelCreate { Name = Unique("In use"), HandlerId = "NativeSmtpHandler" });
        var subscription = await SubscriptionNotifying(id);

        Assert.Contains((await Get(id)).UsedBy, u =>
            u.Kind == NotificationChannelUseKind.Subscription && u.SubscriptionId == subscription.Id);

        await using (var scope = fixture.CreateScope())
        {
            scope.Superuser();
            var delete = ActivatorUtilities.CreateInstance<Resources.NotificationChannels.Delete>(scope.ServiceProvider);
            var ex = await Assert.ThrowsAsync<SWValidationException>(() => delete.Handle(id));
            Assert.Contains("NOTIFICATION_CHANNEL_IN_USE", ex.Message);
            Assert.Contains(subscription.Name, ex.Message);
        }

        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var stored = await db.Set<Subscription>().SingleAsync(s => s.Id == subscription.Id);
            stored.SetNotifications([]);
            await db.SaveChangesAsync();
        }

        await using (var scope = fixture.CreateScope())
        {
            scope.Superuser();
            await ActivatorUtilities.CreateInstance<Resources.NotificationChannels.Delete>(scope.ServiceProvider)
                .Handle(id);
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            Assert.False(await db.Set<NotificationChannel>().AnyAsync(c => c.Id == id));
        }
    }

    [Fact]
    public async Task A_subscription_refuses_a_notification_with_nothing_to_send_on()
    {
        var id = await Create(new NotificationChannelCreate { Name = Unique("Picky"), HandlerId = "NativeSmtpHandler" });
        var subscription = await SubscriptionNotifying(id);

        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var get = ActivatorUtilities.CreateInstance<Resources.Subscriptions.Get>(scope.ServiceProvider);
        var model = (SubscriptionUpdate)await get.Handle(subscription.Id);
        Assert.Single(model.Notifications!);

        model.Notifications = [new SubscriptionNotification { ChannelId = id }];
        // A receiving subscription with no schedules is refused on its own; null leaves them alone.
        model.Schedules = null;
        var update = ActivatorUtilities.CreateInstance<Resources.Subscriptions.Update>(scope.ServiceProvider);
        var ex = await Assert.ThrowsAsync<SWValidationException>(() => update.Handle(subscription.Id, model));
        Assert.Contains("NOTIFICATION_NO_OUTCOME", ex.Message);
    }

    [Fact]
    public async Task A_failed_exchange_is_mailed_through_the_subscription_channel()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        (await http.DeleteAsync($"{fixture.MailHogApi}/api/v1/messages")).EnsureSuccessStatusCode();

        var channelId = await Create(new NotificationChannelCreate
        {
            Name = Unique("MailHog channel"), HandlerId = "NativeSmtpHandler", HandlerProperties = Smtp()
        });
        var subscription = await SubscriptionNotifying(channelId, onFailure: true, onSuccess: false);

        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var xchanges = scope.ServiceProvider.GetRequiredService<XchangeService>();

        var failed = await xchanges.CreateXchange(subscription, new XchangeFile("{}"));
        var succeeded = await xchanges.CreateXchange(subscription, new XchangeFile("{}"));
        await db.SaveChangesAsync();
        db.Add(new XchangeResult(failed.Id, null, null, exception: "timeout"));
        db.Add(new XchangeResult(succeeded.Id, null, null));
        await db.SaveChangesAsync();

        await xchanges.Process("test-Result", JsonSerializer.Serialize(new { Id = failed.Id }));
        await xchanges.Process("test-Result", JsonSerializer.Serialize(new { Id = succeeded.Id }));

        // Only the failure: this row asked for failures, not successes.
        var json = await http.GetStringAsync($"{fixture.MailHogApi}/api/v2/messages");
        using var messages = JsonDocument.Parse(json);
        Assert.Equal(1, messages.RootElement.GetProperty("total").GetInt32());
        var subject = messages.RootElement.GetProperty("items")[0]
            .GetProperty("Content").GetProperty("Headers").GetProperty("Subject")[0].GetString();
        Assert.Equal($"{subscription.Name} finished", subject);

        var logged = await db.Set<XchangeNotification>().AsNoTracking()
            .SingleAsync(n => n.XchangeId == failed.Id);
        Assert.True(logged.Success);
        Assert.Equal(channelId, logged.ChannelId);
        Assert.False(await db.Set<XchangeNotification>().AnyAsync(n => n.XchangeId == succeeded.Id));
    }

    // ─── Moving retry alerts onto channels ───────────────────────────────────────

    [Fact]
    public async Task Retry_alerts_with_their_own_handler_are_moved_onto_one_shared_channel()
    {
        var recipient = $"{Unique("moved")}@example.com";
        var props = new Dictionary<string, string> { ["To"] = recipient, ["Host"] = "relay" };

        int policyId;
        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            // Written the way rows saved before channels look: a handler on the policy and the same
            // one on a group, in a different key order.
            var policy = new RetryPolicy
            {
                Name = Unique("Legacy alert policy"),
                AlertHandlerId = "NativeSmtpHandler",
                AlertHandlerProperties = props,
                Groups =
                [
                    new RetryGroup
                    {
                        Name = "Timeout",
                        AppliesTo = [XchangeResultType.Error],
                        Matchers = [new ContainsMatcher { Value = "timeout" }],
                        AlertMode = RetryAlertMode.Send,
                        AlertHandlerId = "NativeSmtpHandler",
                        AlertHandlerProperties = new Dictionary<string, string> { ["Host"] = "relay", ["To"] = recipient }
                    }
                ]
            };
            db.Add(policy);
            await db.SaveChangesAsync();
            policyId = policy.Id;

            Assert.True(await RetryAlertChannelConversion.Run(db) >= 2);
        }

        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var policy = await db.Set<RetryPolicy>().AsNoTracking().SingleAsync(p => p.Id == policyId);

            // Nothing left on the old fields, so nothing is moved twice.
            Assert.Null(policy.AlertHandlerId);
            Assert.Null(policy.Groups[0].AlertHandlerId);

            // The same handler with the same settings is one channel, whatever the key order.
            Assert.NotNull(policy.AlertChannelId);
            Assert.Equal(policy.AlertChannelId, policy.Groups[0].AlertChannelId);

            var channel = await db.Set<NotificationChannel>().AsNoTracking()
                .SingleAsync(c => c.Id == policy.AlertChannelId);
            Assert.Equal("NativeSmtpHandler", channel.HandlerId);
            Assert.Equal(recipient, channel.HandlerProperties["To"]);

            // And the alert still goes somewhere.
            var target = RetryAlertResolver.Resolve(null, policy.Groups[0], policy);
            Assert.Equal(channel.Id, target!.ChannelId);

            Assert.Equal(0, await RetryAlertChannelConversion.Run(db));
        }
    }
}
