using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SW.Bitween.Domain.Accounts;

namespace SW.Bitween.Services;

/// <summary>
/// Makes the built-in partner roles (<see cref="Role.PartnerPresets"/>) where they are missing, so
/// a partner's person can be given one without anybody ticking permissions.
/// </summary>
/// <remarks>
/// <para>
/// On startup rather than in a migration, because the row needs a name and an id nobody else holds:
/// an existing installation may have a custom role by the same name, or holding the next fixed id,
/// and a migration that collided with either would stop Bitween starting. Here the database picks
/// the id, a taken name gets a suffix, and anything that still goes wrong is logged and skipped.
/// </para>
/// <para>
/// Nodes starting together can both find a preset missing; the unique index on its key keeps one
/// row, and the node that loses reads the other's.
/// </para>
/// </remarks>
public static class BuiltInPartnerRoles
{
    public static IHost EnsureBuiltInPartnerRoles(this IHost host)
    {
        using var scope = host.Services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<BitweenDbContext>>();
        try
        {
            EnsureAsync(scope.ServiceProvider.GetRequiredService<BitweenDbContext>(), logger).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            // A role missing is not a reason to stay down: custom partner roles still work.
            logger.LogWarning(ex, "Couldn't make the built-in partner roles; they'll be tried again on the next start.");
        }
        return host;
    }

    public static async Task EnsureAsync(BitweenDbContext db, ILogger logger)
    {
        var present = await db.Set<Role>().AsNoTracking()
            .Where(r => r.PresetKey != null).Select(r => r.PresetKey).ToListAsync();

        foreach (var preset in Role.PartnerPresets.Where(p => !present.Contains(p.Key)))
        {
            var name = preset.Name;
            if (await db.Set<Role>().AnyAsync(r => r.Name == name))
                name = $"{preset.Name} (built-in)";
            if (await db.Set<Role>().AnyAsync(r => r.Name == name))
            {
                logger.LogWarning("Not making the built-in role {Role}: roles named {Name} and {Name} (built-in) exist already.",
                    preset.Key, preset.Name, preset.Name);
                continue;
            }

            var role = Role.ForPreset(preset, name);
            db.Add(role);
            try
            {
                await db.SaveChangesAsync();
                logger.LogInformation("Made the built-in partner role {Name}.", name);
            }
            catch (DbUpdateException)
            {
                // Another node made it a moment ago.
                db.Entry(role).State = EntityState.Detached;
            }
        }
    }
}
