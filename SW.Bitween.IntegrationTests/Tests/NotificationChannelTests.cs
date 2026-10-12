using System;
using System.Collections.Generic;
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
        try
        {
            var id = await Create(Unique("Encrypted"), Smtp("hunter2-at-rest"));

            Assert.DoesNotContain("hunter2-at-rest", await RawHandlerProperties(id));
            Assert.Equal("hunter2-at-rest", (await Stored(id)).HandlerProperties["Password"]);
        }
        finally
        {
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
}
