using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.NativeAdapters.SmtpHandler;
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
    /// Rejects mail settings that would hand the password to an unencrypted connection.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The handler refuses this at send time too, which is the guarantee that matters. Catching it
    /// here is so the person configuring it finds out when they save, rather than from a missing
    /// notification and a line in the log days later.
    /// </para>
    /// <para>
    /// Only the mail handler is named, because only it has a password. A general answer belongs in
    /// the adapter contract — an adapter saying which of its own settings conflict — not here.
    /// </para>
    /// </remarks>
    public static void EnsureTransportIsSecure(string handlerId, IReadOnlyDictionary<string, string> properties)
    {
        if (properties == null || properties.Count == 0) return;
        if (!nameof(NativeSmtpHandler).Equals(handlerId, StringComparison.OrdinalIgnoreCase)) return;

        // A masked password counts as set: the sentinel means one is stored, not that the field is
        // empty. Only an explicit "false" turns encryption off — absent means the adapter's own
        // default, which is on.
        var password = Value(properties, nameof(SmtpHandlerInput.Password));
        var useTls = Value(properties, nameof(SmtpHandlerInput.UseTls));

        if (string.IsNullOrWhiteSpace(password)) return;
        if (!bool.TryParse(useTls, out var encrypted) || encrypted) return;

        throw new SWValidationException("CHANNEL_PASSWORD_WITHOUT_TLS",
            "This channel would send its mail password over an unencrypted connection. " +
            "Turn UseTls on, or clear the password if the relay does not need one.");
    }

    private static string Value(IReadOnlyDictionary<string, string> properties, string key) =>
        properties.FirstOrDefault(kv => kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value;

}
