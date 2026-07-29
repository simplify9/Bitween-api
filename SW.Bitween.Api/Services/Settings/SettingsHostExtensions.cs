using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SW.Bitween.Services;

public static class SettingsHostExtensions
{
    /// <summary>
    /// Hands configuration over to the Settings table and applies what's stored, before the app
    /// starts serving. Any key without a row yet is imported from configuration once; after that
    /// the table is the only source. Runs after <c>MigrateDatabase</c> so the table is guaranteed
    /// to exist; an unreachable database is logged and the app boots on its configured values
    /// rather than failing to start.
    /// </summary>
    public static IHost ApplyStoredSettings(this IHost host)
    {
        using var scope = host.Services.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<SettingsService>();

        try
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            settings.ImportMissing(dbContext).GetAwaiter().GetResult();
            settings.Reload(dbContext).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            scope.ServiceProvider.GetRequiredService<ILogger<SettingsService>>()
                .LogError(ex, "Could not load stored settings; starting on configured values only.");
        }

        return host;
    }
}
