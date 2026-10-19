using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SW.Bitween.Domain;
using SW.Bitween.Domain.DataSources;

namespace SW.Bitween.Services;

/// <summary>
/// Whether the credentials this database holds can be read with the configured
/// <c>Bitween:SettingsEncryptionKey</c>. A missing or wrong key used to start without complaint and
/// then fail every exchange that needed a partner's or a subscription's settings.
/// </summary>
public static class StoredSecretsKey
{
    public const string KeyLostSetting = "Bitween:SettingsEncryptionKeyLost";

    // Every row, not a sample: a key rotation stopped half way leaves older rows under the old key.
    // These are configuration tables, which the encryption pass reads whole on every start too.
    private const int BatchSize = 100;

    /// <summary>
    /// Records a startup problem when they can't be read. Runs after the migrations, before anything
    /// that reads those tables.
    /// </summary>
    /// <returns>Whether they can be read, so the startup steps that read them can run.</returns>
    public static bool CanReadStoredSecrets(this IHost host)
    {
        using var scope = host.Services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<BitweenDbContext>>();

        if (SecretColumnCipher.KeyLost)
        {
            logger.LogWarning("{Setting} is on: credentials that can't be read with the current key show as " +
                              "empty until they are entered again. Remove it once they are.", KeyLostSetting);
            return true;
        }

        if (AllReadable(scope.ServiceProvider.GetRequiredService<BitweenDbContext>()).GetAwaiter().GetResult())
            return true;

        host.Services.GetRequiredService<StartupProblems>().Add(
            (SecretColumnCipher.Enabled
                ? "Bitween:SettingsEncryptionKey is not the key this database's credentials were encrypted with. "
                : "Bitween:SettingsEncryptionKey is not set, but this database holds credentials encrypted with one. ") +
            "Set it (env: Bitween__SettingsEncryptionKey) to the key used before: a Helm deployment still has " +
            "it in an earlier release, shown by `helm history <release>` and `helm get values <release> " +
            "--revision <n>`. Only if it is lost for good, set Bitween__SettingsEncryptionKeyLost=true: " +
            "partner, subscription and data source settings it encrypted then show as empty until " +
            "they are entered again.");
        return false;
    }

    public static async Task<bool> AllReadable(BitweenDbContext db) =>
        await Readable<Partner>(db) && await Readable<GlobalAdapterValuesSet>(db) &&
        await Readable<DataSource>(db) && await Readable<Subscription>(db) &&
        await Readable<Notifier>(db) && await Readable<NotificationChannel>(db) &&
        await Readable<RetryPolicy>(db) && await Readable<RetryAlertOverride>(db) &&
        await Readable<Domain.Connections.Connection>(db);

    private static async Task<bool> Readable<T>(BitweenDbContext db) where T : class
    {
        var key = db.Model.FindEntityType(typeof(T))!.FindPrimaryKey()!.Properties[0].Name;
        try
        {
            for (var skip = 0; ; skip += BatchSize)
            {
                var batch = await db.Set<T>().AsNoTracking()
                    .OrderBy(e => EF.Property<object>(e, key))
                    .Skip(skip).Take(BatchSize).ToListAsync();
                if (batch.Count < BatchSize) return true;
            }
        }
        catch (Exception ex) when (IsUnreadable(ex))
        {
            return false;
        }
    }

    // EF wraps what a value converter throws.
    private static bool IsUnreadable(Exception ex) =>
        ex is UnreadableSecretException || ex.InnerException is not null && IsUnreadable(ex.InnerException);
}
