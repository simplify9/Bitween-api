using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
/// Notification channels: where notifications and alerts go, set up once in Settings and picked by
/// each place that notifies.
/// </summary>
[Collection("Bitween")]
public class NotificationChannelTests(BitweenFixture fixture)
{
    private const string Sentinel = AdapterSecretProperties.Sentinel;

    private static int _seq;
    private static string Unique(string prefix) => $"{prefix}-{Interlocked.Increment(ref _seq)}";

    /// <summary>The legacy SMTP adapter's settings: enough to save, nothing is sent here.</summary>
    private static Dictionary<string, string> Smtp(string password = "", bool useTls = true,
        string host = "smtp.example.com") => new()
    {
        ["Host"] = host,
        ["Port"] = "587",
        ["UseTls"] = useTls ? "true" : "false",
        ["Password"] = password,
        ["From"] = "bitween@example.com",
        ["To"] = "ops@example.com",
        ["Subject"] = "{{ SubscriptionName }} finished",
        ["Body"] = "Exchange {{ Id }}"
    };

    private async Task<int> Create(string name, Dictionary<string, string> properties,
        string handlerId = "NativeSmtpHandler")
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        return (int)await ActivatorUtilities.CreateInstance<Resources.NotificationChannels.Create>(scope.ServiceProvider)
            .Handle(new NotificationChannelCreate { Name = name, HandlerId = handlerId, HandlerProperties = properties });
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

    private async Task Delete(int id)
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        await ActivatorUtilities.CreateInstance<Resources.NotificationChannels.Delete>(scope.ServiceProvider)
            .Handle(id);
    }

    private async Task<NotificationChannel> Stored(int id)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        return await db.Set<NotificationChannel>().AsNoTracking().SingleOrDefaultAsync(c => c.Id == id);
    }

    private async Task<string> RawHandlerProperties(int id)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        return await db.Database
            .SqlQueryRaw<string>(
                "SELECT handler_properties::text AS \"Value\" FROM infolink.notification_channel WHERE id = {0}", id)
            .SingleAsync();
    }

    [Fact]
    public async Task A_channel_password_is_hidden_on_read_and_kept_when_saved_back()
    {
        var name = Unique("Hidden password");
        var id = await Create(name, Smtp("hunter2"));

        var loaded = await Get(id);
        Assert.Equal(Sentinel, loaded.HandlerProperties["Password"]);
        Assert.Equal("smtp.example.com", loaded.HandlerProperties["Host"]);

        // What the page does when someone edits the subject: the password comes back as the
        // sentinel, and must not be stored as one.
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
    public async Task Switching_the_adapter_drops_a_hidden_password_instead_of_handing_it_over()
    {
        var name = Unique("Switched adapter");
        var id = await Create(name, Smtp("hunter2"));

        // Keeping the sentinel while switching to an adapter that posts wherever the caller likes
        // would send the stored password there, without the caller ever having seen it.
        await Update(id, new NotificationChannelUpdate
        {
            Name = name,
            HandlerId = "NativeHttpHandler",
            HandlerProperties = new() { ["LoginPassword"] = Sentinel, ["Password"] = Sentinel, ["Url"] = "https://example.com" }
        });

        var stored = await Stored(id);
        Assert.False(stored.HandlerProperties.ContainsKey("Password"));
        Assert.False(stored.HandlerProperties.ContainsKey("LoginPassword"));
        Assert.Equal("https://example.com", stored.HandlerProperties["Url"]);
    }

    [Fact]
    public async Task Pointing_the_mail_server_elsewhere_drops_a_hidden_password()
    {
        var name = Unique("Moved server");
        var id = await Create(name, Smtp("hunter2"));

        var moved = (await Get(id)).HandlerProperties;
        moved["Host"] = "mail.attacker.example";

        // Same adapter, but a new destination: the stored password is not carried over to it.
        await Update(id, new NotificationChannelUpdate
        {
            Name = name, HandlerId = "NativeSmtpHandler", HandlerProperties = moved
        });
        Assert.False((await Stored(id)).HandlerProperties.ContainsKey("Password"));

        // It has to be typed again, which proves whoever moved it knows it.
        moved["Password"] = "typed-again";
        await Update(id, new NotificationChannelUpdate
        {
            Name = name, HandlerId = "NativeSmtpHandler", HandlerProperties = moved
        });
        var stored = await Stored(id);
        Assert.Equal("mail.attacker.example", stored.HandlerProperties["Host"]);
        Assert.Equal("typed-again", stored.HandlerProperties["Password"]);
    }

    [Fact]
    public async Task A_mail_channel_with_a_password_and_no_encryption_is_refused()
    {
        var ex = await Assert.ThrowsAsync<SWValidationException>(() =>
            Create(Unique("Cleartext"), Smtp("hunter2", useTls: false)));
        Assert.Contains("CHANNEL_PASSWORD_WITHOUT_TLS", ex.Message);

        // Encryption off is fine on its own: only the password must not travel in clear.
        await Create(Unique("No password"), Smtp(useTls: false));
    }

    [Fact]
    public async Task A_channel_missing_a_setting_its_adapter_needs_is_refused()
    {
        var properties = Smtp();
        properties.Remove("To");

        var ex = await Assert.ThrowsAsync<SWValidationException>(() => Create(Unique("Nobody to tell"), properties));
        Assert.Contains("CHANNEL_SETTINGS_MISSING", ex.Message);
        Assert.Contains("To", ex.Message);
    }

    [Fact]
    public async Task Two_channels_cannot_share_a_name()
    {
        var name = Unique("Taken");
        await Create(name, Smtp());

        var ex = await Assert.ThrowsAsync<SWValidationException>(() => Create(name, Smtp()));
        Assert.Contains("NOTIFICATION_CHANNEL_NAME_TAKEN", ex.Message);
    }

    [Fact]
    public async Task A_channel_that_does_not_exist_is_not_found_rather_than_a_server_error()
    {
        const int missing = 999_999;

        await Assert.ThrowsAsync<SWNotFoundException>(() => Get(missing));
        await Assert.ThrowsAsync<SWNotFoundException>(() => Update(missing, new NotificationChannelUpdate
        {
            Name = Unique("Nowhere"), HandlerId = "NativeSmtpHandler", HandlerProperties = Smtp()
        }));
        await Assert.ThrowsAsync<SWNotFoundException>(() => Delete(missing));
    }

    [Fact]
    public async Task A_deleted_channel_is_gone()
    {
        var id = await Create(Unique("Short-lived"), Smtp());

        await Delete(id);

        Assert.Null(await Stored(id));
    }

    [Fact]
    public async Task A_channel_password_is_stored_encrypted_and_read_back_plain()
    {
        SecretColumnCipher.Configure("integration test passphrase");
        int? id = null;
        try
        {
            id = await Create(Unique("Encrypted"), Smtp("hunter2-at-rest"));

            Assert.DoesNotContain("hunter2-at-rest", await RawHandlerProperties(id.Value));
            Assert.Equal("hunter2-at-rest", (await Stored(id.Value)).HandlerProperties["Password"]);
        }
        finally
        {
            // Unreadable once the key is gone, and the cache loads every channel, so it can't stay.
            if (id != null)
            {
                await using var scope = fixture.CreateScope();
                await scope.ServiceProvider.GetRequiredService<BitweenDbContext>().Set<NotificationChannel>()
                    .Where(c => c.Id == id).ExecuteDeleteAsync();
            }
            SecretColumnCipher.Configure(null);
        }
    }

    [Fact]
    public async Task A_channel_saved_before_a_key_was_set_is_encrypted_by_the_pass()
    {
        var id = await Create(Unique("Saved in clear"), Smtp("hunter2-before-key"));
        Assert.Contains("hunter2-before-key", await RawHandlerProperties(id));

        SecretColumnCipher.Configure("integration test passphrase");
        try
        {
            await ActivatorUtilities.CreateInstance<SecretColumnEncryptionPass>(fixture.App.Services)
                .RunAsync(CancellationToken.None);

            Assert.DoesNotContain("hunter2-before-key", await RawHandlerProperties(id));
            Assert.Equal("hunter2-before-key", (await Stored(id)).HandlerProperties["Password"]);
        }
        finally
        {
            // Everything the pass encrypted would be unreadable to the rest of the collection, so it
            // is written back in clear, as SecretColumnEncryptionTests does.
            var decrypt = ActivatorUtilities.CreateInstance<SecretColumnEncryptionPass>(fixture.App.Services);
            SecretColumnCipher.Configure(null, previousPassphrase: "integration test passphrase");
            await decrypt.RunAsync(CancellationToken.None);
            SecretColumnCipher.Configure(null);
        }
    }

    [Fact]
    public async Task A_channel_in_use_names_its_users_and_cannot_be_deleted()
    {
        var channelId = await Create(Unique("In use"), Smtp());
        var subscriptionId = await NotifyingSubscription(
            new SubscriptionNotification { ChannelId = channelId, OnFailure = true });

        var use = Assert.Single((await Get(channelId)).UsedBy);
        Assert.Equal(subscriptionId, use.SubscriptionId);

        var ex = await Assert.ThrowsAsync<SWValidationException>(() => Delete(channelId));
        Assert.Contains("NOTIFICATION_CHANNEL_IN_USE", ex.Message);
        Assert.NotNull(await Stored(channelId));
    }

    [Fact]
    public async Task Notifications_are_saved_and_a_save_that_leaves_them_out_keeps_them()
    {
        var channelId = await RecordingChannel(Token("saved"));
        var subscriptionId = await NotifyingSubscription();

        await SaveNotifications(subscriptionId, [new SubscriptionNotification { ChannelId = channelId, OnFailure = true }]);
        var row = Assert.Single((await ReadSubscription(subscriptionId)).Notifications!);
        Assert.Equal(channelId, row.ChannelId);
        Assert.True(row.OnFailure);

        // An older client, or a script, that has never heard of them.
        await SaveNotifications(subscriptionId, null);
        Assert.Single((await ReadSubscription(subscriptionId)).Notifications!);

        await SaveNotifications(subscriptionId, []);
        Assert.Empty((await ReadSubscription(subscriptionId)).Notifications!);
    }

    [Fact]
    public async Task A_subscription_refuses_notifications_that_could_never_send()
    {
        var channelId = await RecordingChannel(Token("checks"));
        var subscriptionId = await NotifyingSubscription();

        async Task<string> Refused(params SubscriptionNotification[] rows) =>
            (await Assert.ThrowsAsync<SWValidationException>(() => SaveNotifications(subscriptionId, rows))).Message;

        Assert.Contains("NOTIFICATION_NO_OUTCOME", await Refused(new SubscriptionNotification { ChannelId = channelId }));
        Assert.Contains("NOTIFICATION_CHANNEL_REPEATED", await Refused(
            new SubscriptionNotification { ChannelId = channelId, OnFailure = true },
            new SubscriptionNotification { ChannelId = channelId, OnSuccess = true }));
        Assert.Contains("NOTIFICATION_CHANNEL_NOT_FOUND", await Refused(
            new SubscriptionNotification { ChannelId = 999_999, OnFailure = true }));
    }

    [Fact]
    public async Task A_failure_goes_to_the_channel_ticked_for_failures_and_a_success_to_the_one_ticked_for_successes()
    {
        var failures = Token("failures");
        var successes = Token("successes");
        var subscriptionId = await NotifyingSubscription(
            new SubscriptionNotification { ChannelId = await RecordingChannel(failures), OnFailure = true },
            new SubscriptionNotification { ChannelId = await RecordingChannel(successes), OnSuccess = true });

        await ProcessResult(await FinishedExchange(subscriptionId, failed: true));
        await ProcessResult(await FinishedExchange(subscriptionId, failed: false));

        Assert.Equal(1, NativeTestResponder.Deliveries.GetValueOrDefault(failures));
        Assert.Equal(1, NativeTestResponder.Deliveries.GetValueOrDefault(successes));

        var sent = JsonConvert.DeserializeObject<XchangeResultNotification>(
            NativeTestResponder.Received[failures].Single());
        Assert.False(sent.Success);
        Assert.Contains("the partner is down", sent.Exception);
    }

    /// <summary>An outage is one message through a channel, as it is through a notifier.</summary>
    [Fact]
    public async Task A_burst_of_failures_sends_one_message_through_a_channel_and_counts_the_rest()
    {
        var token = Token("burst");
        var channelId = await RecordingChannel(token);
        var subscriptionId = await NotifyingSubscription(
            new SubscriptionNotification { ChannelId = channelId, OnFailure = true });

        for (var i = 0; i < 4; i++) await ProcessResult(await FinishedExchange(subscriptionId, failed: true));

        Assert.Equal(1, NativeTestResponder.Deliveries.GetValueOrDefault(token));
        var rows = await Sends(channelId);
        Assert.Equal(1, rows.Count(n => n.Success));
        Assert.Equal(3, rows.Count(n => n.Suppressed));
    }

    [Fact]
    public async Task A_repeat_of_the_result_event_sends_once_through_a_channel()
    {
        var token = Token("repeat");
        var subscriptionId = await NotifyingSubscription(
            new SubscriptionNotification { ChannelId = await RecordingChannel(token), OnFailure = true });
        var id = await FinishedExchange(subscriptionId, failed: true);

        await ProcessResult(id);
        await ProcessResult(id);

        Assert.Equal(1, NativeTestResponder.Deliveries.GetValueOrDefault(token));
    }

    [Fact]
    public async Task A_send_the_adapter_keeps_refusing_is_tried_three_times_then_recorded_as_failed()
    {
        var token = Token("refused");
        // The responder counts the message, then fails on a "Bad" that isn't true or false.
        var channelId = await RecordingChannel(token, new Dictionary<string, string> { ["Bad"] = "neither" });
        var subscriptionId = await NotifyingSubscription(
            new SubscriptionNotification { ChannelId = channelId, OnFailure = true });

        await ProcessResult(await FinishedExchange(subscriptionId, failed: true));

        Assert.Equal(3, NativeTestResponder.Deliveries.GetValueOrDefault(token));
        var row = Assert.Single(await Sends(channelId));
        Assert.False(row.Success);
        Assert.Contains("FormatException", row.Exception);
    }

    [Fact]
    public async Task The_test_button_sends_through_the_channel_and_says_whether_it_went()
    {
        var subject = "{{ SubscriptionName }} from " + Unique("test button");
        var working = await Create(Unique("Testable"), MailHog(subject));
        var broken = MailHog(subject);
        // Nothing listens on port 1, so the connection is refused at once.
        broken["Port"] = "1";
        var refused = await Create(Unique("Untestable"), broken);

        var sent = await TestChannel(working);
        Assert.True(sent.Sent);
        Assert.Null(sent.Error);
        // A template written against the notification's fields is part of what is tested.
        Assert.Single(await Mailed(subject.Replace("{{ SubscriptionName }}", "Test notification")));

        var failed = await TestChannel(refused);
        Assert.False(failed.Sent);
        Assert.False(string.IsNullOrWhiteSpace(failed.Error));
        // The reason alone, not a stack trace, because it is shown to whoever pressed the button.
        Assert.DoesNotContain("   at ", failed.Error);

        // Nothing to record it against, so the history stays clean.
        Assert.Empty(await Sends(working));
        Assert.Empty(await Sends(refused));
    }

    [Fact]
    public async Task A_failure_reaches_an_email_channel_as_a_message_bitween_writes()
    {
        var channelId = await Create(Unique("Email channel"),
            new Dictionary<string, string> { ["To"] = "ops@example.com" }, "NativeEmailMessageHandler");
        var subscriptionId = await NotifyingSubscription(
            new SubscriptionNotification { ChannelId = channelId, OnFailure = true });
        var subscriptionName = (await ReadSubscription(subscriptionId)).Name;
        var xchangeId = await FinishedExchange(subscriptionId, failed: true, exception: "<b>Timed out</b> after 30s");

        await ProcessResult(xchangeId);

        var mail = Assert.Single(await Mailed($"{subscriptionName} failed"));
        Assert.Equal("bitween@example.com", mail.From.Mailboxes.Single().Address);
        Assert.Equal("ops@example.com", mail.To.Mailboxes.Single().Address);
        // The error is text from somewhere else, so it is shown, not obeyed.
        Assert.Contains("&lt;b&gt;Timed out&lt;/b&gt;", mail.HtmlBody);
        Assert.Contains($"https://bitween.test/exchanges?ids={xchangeId}", mail.HtmlBody);
        Assert.Contains("Timed out", mail.TextBody);
    }

    [Fact]
    public async Task Global_values_and_the_partners_own_properties_are_filled_into_a_channel()
    {
        var setId = Unique("mail-values");
        var subject = Unique("Filled subject");
        var partnerContact = $"{Unique("contact")}@example.com";
        Partner partner;
        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            db.Add(new GlobalAdapterValuesSet { Id = setId, Name = setId, Values = new() { ["team"] = "team@example.com" } });
            partner = new Partner(Unique("Notified partner"))
            {
                AdapterProperties = new Dictionary<string, string> { ["CONTACT"] = partnerContact }
            };
            db.Add(partner);
            await db.SaveChangesAsync();
            scope.ServiceProvider.GetRequiredService<IInfolinkCache>().Revoke();
        }

        // One channel, written once: the shared address from a global value set, and whoever the
        // reported exchange's partner names as its contact.
        var properties = MailHog(subject);
        properties["To"] = "{{partner.CONTACT}}";
        properties["Cc"] = $"{{{{globals.{setId}.team}}}}";
        var channel = await Stored(await Create(Unique("Filled channel"), properties));

        await using (var scope = fixture.CreateScope())
        {
            var error = await scope.ServiceProvider.GetRequiredService<NotificationChannelSender>()
                .Send(channel, new XchangeResultNotification { Id = "x", SubscriptionName = "Orders" }, "x", null, partner);
            Assert.Null(error);
        }

        var mail = Assert.Single(await Mailed(subject));
        Assert.Equal(partnerContact, mail.To.Mailboxes.Single().Address);
        Assert.Equal("team@example.com", mail.Cc.Mailboxes.Single().Address);
    }

    [Fact]
    public async Task An_email_channel_without_anyone_to_send_to_is_refused()
    {
        var ex = await Assert.ThrowsAsync<SWValidationException>(() =>
            Create(Unique("Nobody"), [], "NativeEmailMessageHandler"));
        Assert.Contains("CHANNEL_SETTINGS_MISSING", ex.Message);
        Assert.Contains("To", ex.Message);
    }

    [Fact]
    public async Task A_chat_webhook_has_to_be_https()
    {
        var ex = await Assert.ThrowsAsync<SWValidationException>(() => Create(Unique("Plain webhook"),
            new Dictionary<string, string> { ["WebhookUrl"] = "http://hooks.slack.test/x" }, "NativeSlackMessageHandler"));
        Assert.Contains("CHANNEL_WEBHOOK_NOT_HTTPS", ex.Message);
    }

    private static string Token(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    /// <summary>The SMTP adapter pointed at MailHog, with a subject of its own so a test finds its mail.</summary>
    private Dictionary<string, string> MailHog(string subject) => new()
    {
        ["Host"] = "localhost",
        ["Port"] = fixture.MailHogSmtpPort.ToString(),
        ["UseTls"] = "false",
        ["From"] = "bitween@example.com",
        ["To"] = "ops@example.com",
        ["Subject"] = subject,
        ["Body"] = "Exchange {{ Id }}"
    };

    private async Task<List<MimeKit.MimeMessage>> Mailed(string subject)
    {
        using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        using var messages = System.Text.Json.JsonDocument.Parse(
            await http.GetStringAsync($"{fixture.MailHogApi}/api/v2/messages?limit=250"));
        var found = new List<MimeKit.MimeMessage>();
        foreach (var item in messages.RootElement.GetProperty("items").EnumerateArray())
        {
            var raw = item.GetProperty("Raw").GetProperty("Data").GetString()!;
            var message = await MimeKit.MimeMessage.LoadAsync(
                new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(raw)));
            if (message.Subject == subject) found.Add(message);
        }
        return found;
    }

    private async Task<NotificationChannelTestResult> TestChannel(int id)
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        return (NotificationChannelTestResult)await ActivatorUtilities
            .CreateInstance<Resources.NotificationChannels.Test>(scope.ServiceProvider)
            .Handle(id, new NotificationChannelTest());
    }

    /// <summary>A channel whose messages the test responder counts under <paramref name="token"/>.</summary>
    private async Task<int> RecordingChannel(string token, Dictionary<string, string> extra = null)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var properties = new Dictionary<string, string>(extra ?? []) { ["Body"] = token };
        var channel = new NotificationChannel
        {
            Name = Unique("Recording"), HandlerId = nameof(NativeTestResponder), HandlerProperties = properties
        };
        db.Add(channel);
        await db.SaveChangesAsync();
        scope.ServiceProvider.GetRequiredService<IInfolinkCache>().Revoke();
        return channel.Id;
    }

    private async Task<int> NotifyingSubscription(params SubscriptionNotification[] notifications)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var document = new Document(null, $"Channels {Guid.NewGuid():N}", DocumentFormat.Json);
        db.Add(document);
        await db.SaveChangesAsync();
        // A gateway type: a Receiving one would need schedules before an update is accepted.
        var subscription = new Subscription($"Channels {Guid.NewGuid():N}", document.Id,
            SubscriptionType.GatewayApiCall) { Inactive = false };
        subscription.SetNotifications(notifications);
        db.Add(subscription);
        await db.SaveChangesAsync();
        scope.ServiceProvider.GetRequiredService<IInfolinkCache>().Revoke();
        return subscription.Id;
    }

    private async Task<SubscriptionGet> ReadSubscription(int id)
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        return (SubscriptionGet)await ActivatorUtilities
            .CreateInstance<Resources.Subscriptions.Get>(scope.ServiceProvider).Handle(id);
    }

    private async Task SaveNotifications(int subscriptionId, ICollection<SubscriptionNotification> notifications)
    {
        var model = await ReadSubscription(subscriptionId);
        model.Notifications = notifications;
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        await ActivatorUtilities.CreateInstance<Resources.Subscriptions.Update>(scope.ServiceProvider)
            .Handle(subscriptionId, model);
    }

    private async Task<string> FinishedExchange(int subscriptionId, bool failed,
        string exception = "System.Exception: the partner is down")
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var subscription = await db.Set<Subscription>().SingleAsync(s => s.Id == subscriptionId);
        var xchange = await scope.ServiceProvider.GetRequiredService<XchangeService>()
            .CreateXchange(subscription, new XchangeFile("{}"));
        await db.SaveChangesAsync();
        db.Add(new XchangeResult(xchange.Id, null, null,
            exception: failed ? exception : null));
        await db.SaveChangesAsync();
        return xchange.Id;
    }

    private async Task ProcessResult(string xchangeId)
    {
        await using var scope = fixture.CreateScope();
        await scope.ServiceProvider.GetRequiredService<XchangeService>()
            .Process("Channels" + XchangeService.ResultQueueSuffix, JsonConvert.SerializeObject(new { Id = xchangeId }));
    }

    private async Task<List<XchangeNotification>> Sends(int channelId)
    {
        await using var scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<BitweenDbContext>()
            .Set<XchangeNotification>().AsNoTracking().Where(n => n.ChannelId == channelId).ToListAsync();
    }
}
