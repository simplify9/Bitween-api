using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.Bitween.NativeAdapters.SlackMessageHandler;
using SW.Bitween.NativeAdapters.SmtpHandler;
using SW.Bitween.NativeAdapters.TeamsMessageHandler;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.NotificationChannels;

internal static class ChannelValidation
{
    public static async Task EnsureNameIsFree(BitweenDbContext dbContext, string name, int? exceptId = null)
    {
        if (await dbContext.Set<NotificationChannel>()
                .AnyAsync(c => c.Name == name && (exceptId == null || c.Id != exceptId)))
            throw new SWValidationException("NOTIFICATION_CHANNEL_NAME_TAKEN",
                $"A notification channel called '{name}' already exists.");
    }

    /// <summary>
    /// Refuses mail settings that would send the password over an unencrypted connection.
    /// </summary>
    /// <remarks>
    /// The SMTP adapter refuses this when it sends, which is the guarantee that matters. Checking
    /// here means whoever sets the channel up finds out when they save, rather than from a missing
    /// notification days later.
    /// </remarks>
    public static void EnsureTransportIsSecure(string handlerId, IReadOnlyDictionary<string, string> properties)
    {
        if (properties == null || properties.Count == 0) return;
        if (!nameof(NativeSmtpHandler).Equals(handlerId, StringComparison.OrdinalIgnoreCase)) return;

        // A hidden password counts as set: the sentinel means one is stored. Only an explicit
        // "false" turns encryption off; absent means the adapter's default, which is on.
        var password = Value(properties, nameof(SmtpHandlerInput.Password));
        var useTls = Value(properties, nameof(SmtpHandlerInput.UseTls));

        if (string.IsNullOrWhiteSpace(password)) return;
        if (!bool.TryParse(useTls, out var encrypted) || encrypted) return;

        throw new SWValidationException("CHANNEL_PASSWORD_WITHOUT_TLS",
            "This channel would send its mail password over an unencrypted connection. " +
            "Turn UseTls on, or clear the password if the server does not need one.");
    }

    /// <summary>
    /// Refuses a Teams or Slack webhook that is not a full https address. Both services only hand
    /// out https ones, and anything else would carry the message, and the address that lets anyone
    /// post to the channel, in the clear.
    /// </summary>
    /// <remarks>
    /// A reference to a global value is let through, as is the mask of a stored address: the value
    /// it stands for is checked when it is sent.
    /// </remarks>
    public static void EnsureWebhookIsHttps(string handlerId, IReadOnlyDictionary<string, string> properties)
    {
        if (!nameof(NativeTeamsMessageHandler).Equals(handlerId, StringComparison.OrdinalIgnoreCase) &&
            !nameof(NativeSlackMessageHandler).Equals(handlerId, StringComparison.OrdinalIgnoreCase))
            return;

        var url = Value(properties ?? new Dictionary<string, string>(), nameof(TeamsMessageInput.WebhookUrl));
        if (string.IsNullOrWhiteSpace(url) || url == AdapterSecretProperties.Sentinel || url.Contains("{{")) return;

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new SWValidationException("CHANNEL_WEBHOOK_NOT_HTTPS",
                "The webhook address has to be a full https:// address, copied from Teams or Slack.");
    }

    /// <summary>
    /// Refuses a channel missing a setting its adapter can't send without, so it fails when it is
    /// saved, not when it is needed.
    /// </summary>
    /// <remarks>Checked on what will be stored, so a dropped secret counts as missing.</remarks>
    public static async Task EnsureRequiredPresent(AdapterRequirements requirements, string handlerId,
        IReadOnlyDictionary<string, string> properties)
    {
        var missing = await requirements.MissingFor(handlerId,
            (properties ?? new Dictionary<string, string>())
            .Select(kv => new KeyAndValue { Key = kv.Key, Value = kv.Value }).ToList());

        if (missing.Count > 0)
            throw new SWValidationException("CHANNEL_SETTINGS_MISSING",
                $"This channel can't send without {string.Join(", ", missing)}.");
    }

    /// <summary>
    /// Refuses a subscription's notification rows that could never send: one with no outcome
    /// ticked, a channel listed twice, or a channel that doesn't exist.
    /// </summary>
    public static async Task EnsureNotificationsCanSend(BitweenDbContext dbContext,
        ICollection<SubscriptionNotification> notifications)
    {
        if (notifications.Any(n => !n.OnFailure && !n.OnBadResult && !n.OnSuccess))
            throw new SWValidationException("NOTIFICATION_NO_OUTCOME",
                "A notification has nothing ticked to send on. Tick at least one outcome, or remove it.");

        if (notifications.GroupBy(n => n.ChannelId).Any(g => g.Count() > 1))
            throw new SWValidationException("NOTIFICATION_CHANNEL_REPEATED",
                "The same channel is listed twice. Tick every outcome it should send on in one row.");

        var ids = notifications.Select(n => n.ChannelId).ToList();
        var found = await dbContext.Set<NotificationChannel>().Where(c => ids.Contains(c.Id))
            .Select(c => c.Id).ToListAsync();
        var missing = ids.Except(found).ToList();
        if (missing.Count > 0)
            throw new SWValidationException("NOTIFICATION_CHANNEL_NOT_FOUND",
                $"Notification channel {string.Join(", ", missing)} was not found.");
    }

    private static string Value(IReadOnlyDictionary<string, string> properties, string key) =>
        properties.FirstOrDefault(kv => kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value;
}
