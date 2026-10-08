using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SW.Bitween.Domain;

namespace SW.Bitween.Services;

/// <summary>
/// Converts partner API keys stored before keys were hashed. Runs at startup after the SYSTEM
/// partner's published key has been dealt with; once every key is hashed it finds nothing to do.
/// </summary>
public static class PartnerKeyHashing
{
    public static IHost HashStoredPartnerKeys(this IHost host)
    {
        using var scope = host.Services.CreateScope();
        HashStoredKeys(
                scope.ServiceProvider.GetRequiredService<BitweenDbContext>(),
                scope.ServiceProvider.GetRequiredService<ILogger<BitweenDbContext>>())
            .GetAwaiter().GetResult();
        return host;
    }

    public static async Task HashStoredKeys(BitweenDbContext dbContext, ILogger logger)
    {
        var partners = await dbContext.Set<Partner>()
            .Where(p => p.ApiCredentials.Any(c => !c.Key.StartsWith(PartnerKeyHash.Prefix)))
            .ToListAsync();
        if (partners.Count == 0) return;

        // All or nothing: a partner left with some keys removed and not yet re-added would turn
        // away the callers that use them.
        await using var transaction = dbContext.Database.CurrentTransaction is null
            ? await dbContext.Database.BeginTransactionAsync()
            : null;

        var converted = 0;
        foreach (var partner in partners)
        {
            var hashed = partner.ApiCredentials.Where(c => PartnerKeyHash.IsHashed(c.Key)).ToList();
            var plain = partner.ApiCredentials.Where(c => !PartnerKeyHash.IsHashed(c.Key)).ToList();

            // Credentials compare by name, so the plain ones go before their hashed copies come in.
            partner.SetApiCredentials(hashed);
            await dbContext.SaveChangesAsync();
            partner.SetApiCredentials(hashed.Concat(plain.Select(c => new ApiCredential(c.Name, c.Key))));
            await dbContext.SaveChangesAsync();
            converted += plain.Count;
        }

        if (transaction is not null) await transaction.CommitAsync();
        logger.LogInformation("Stored {Count} partner API key(s) as hashes.", converted);
    }
}
