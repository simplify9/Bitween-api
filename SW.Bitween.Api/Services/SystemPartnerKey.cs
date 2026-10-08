using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Accounts;

namespace SW.Bitween.Services;

/// <summary>
/// The SYSTEM partner is seeded with an API key that is published in this repository. Whoever
/// holds it can post any document type into content routing without signing in, so it must not
/// stay in place — but deployments that have been running for years may have systems that post
/// with it, so it cannot simply be changed under them.
/// </summary>
public static class SystemPartnerKey
{
    // The key the migrations give the SYSTEM partner, published in this repository.
    public const string PublishedKey = "7facc758283844b49cc4ffd26a75b1de";

    /// <summary>
    /// Replaces the published key on the SYSTEM partner. Runs after <c>MigrateDatabase</c>; once
    /// the key is anything else this does nothing, so changing the configured value later has no
    /// effect — rotate it from the partner's page.
    /// </summary>
    private static bool IsPublished(ApiCredential credential) =>
        credential.Key == PublishedKey || credential.Key == PartnerKeyHash.Of(PublishedKey);

    public static IHost SecureSystemPartnerKey(this IHost host)
    {
        using var scope = host.Services.CreateScope();
        Secure(
                scope.ServiceProvider.GetRequiredService<BitweenDbContext>(),
                scope.ServiceProvider.GetRequiredService<IConfiguration>()["Bitween:SystemPartnerKey"],
                scope.ServiceProvider.GetRequiredService<ILogger<BitweenDbContext>>())
            .GetAwaiter().GetResult();
        return host;
    }

    /// <remarks>
    /// With <c>Bitween:SystemPartnerKey</c> configured, the published key is replaced by it, so an
    /// operator can rotate the key and hand the new one to whatever posts with it in one step.
    /// Without it, a new installation — the seeded administrator is the only account, as
    /// <see cref="SeededAdministrator"/> judges it — gets a random key, since nothing can be
    /// using the old one yet. An existing installation keeps the published key, so nothing that
    /// posts with it breaks on upgrade, and every start logs that it is still in place.
    /// </remarks>
    public static async Task Secure(BitweenDbContext dbContext, string configuredKey, ILogger logger)
    {
        var partner = await dbContext.Set<Partner>()
            .SingleOrDefaultAsync(p => p.Id == Partner.SystemId);

        var published = partner?.ApiCredentials.FirstOrDefault(IsPublished);
        if (published is null)
            return;

        string replacement;
        if (!string.IsNullOrWhiteSpace(configuredKey))
        {
            replacement = configuredKey.Trim();
            if (replacement == PublishedKey)
                throw new InvalidOperationException(
                    "Bitween:SystemPartnerKey is the default key, which is published in a public repository. " +
                    "Choose a different one.");
            if (replacement.Length < 32)
                throw new InvalidOperationException(
                    "Bitween:SystemPartnerKey must be at least 32 characters.");
            var replacementHash = PartnerKeyHash.Of(replacement);
            if (await dbContext.Set<Partner>().AnyAsync(p =>
                    p.ApiCredentials.Any(c => c.Key == replacement || c.Key == replacementHash)))
                throw new InvalidOperationException(
                    "Bitween:SystemPartnerKey is already in use by a partner. Choose a different one.");
        }
        else if (await dbContext.Set<Account>().AnyAsync(a => a.Id != SeededAdministrator.Id && !a.Deleted))
        {
            logger.LogCritical(
                "The SYSTEM partner still accepts the API key published in Bitween's public repository " +
                "('{KeyName}'). Anyone who knows it can post documents into routing without signing in. " +
                "Rotate it from the partner's page, or set Bitween:SystemPartnerKey " +
                "(env: Bitween__SystemPartnerKey) and restart; update anything that posts with the old key first.",
                published.Name);
            return;
        }
        else
        {
            replacement = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        }

        // Credentials compare by name, so the old one has to go before one of the same name can be
        // added. Together or not at all: a partner left with neither would lock out the caller.
        await using var transaction = dbContext.Database.CurrentTransaction is null
            ? await dbContext.Database.BeginTransactionAsync()
            : null;

        var kept = partner.ApiCredentials.Where(c => !IsPublished(c)).ToList();
        partner.SetApiCredentials(kept);
        await dbContext.SaveChangesAsync();

        partner.SetApiCredentials(kept.Append(new ApiCredential(published.Name, replacement)));
        await dbContext.SaveChangesAsync();

        if (transaction is not null)
            await transaction.CommitAsync();

        logger.LogWarning(
            string.IsNullOrWhiteSpace(configuredKey)
                ? "Replaced the SYSTEM partner's published API key '{KeyName}' with a random one on this new installation."
                : "Replaced the SYSTEM partner's published API key '{KeyName}' with Bitween:SystemPartnerKey.",
            published.Name);
    }
}
