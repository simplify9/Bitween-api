using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SW.Bitween.Domain;

namespace SW.Bitween.Services;

/// <summary>
/// The bus names its exchange and every queue after ASPNETCORE_ENVIRONMENT. A new name moves Bitween
/// to new queues while the other applications on the same RabbitMQ keep sending to the old ones, and
/// what was waiting in them stays there. So a name Bitween didn't run with before is refused until
/// it is confirmed.
/// </summary>
/// <remarks>
/// Not a hypothetical: the chart's default environment changed from Development to Production, so a
/// deployment that never chose one changed it on upgrade without anyone deciding to.
/// </remarks>
public static class BusEnvironment
{
    /// <summary>
    /// The name Bitween last ran with. A Settings row nobody edits and the settings page doesn't list,
    /// as <see cref="SettingsService.LegacyDocumentPrefixKey"/>.
    /// </summary>
    public const string RecordedKey = "Bitween.BusEnvironment";

    public const string ConfirmSetting = "Bitween:ConfirmBusEnvironment";

    private static readonly string[] UsualNames = ["Development", "Staging", "Production"];

    public static IHost CheckBusEnvironment(this IHost host)
    {
        using var scope = host.Services.CreateScope();
        var services = scope.ServiceProvider;
        var configuration = services.GetRequiredService<IConfiguration>();
        var current = services.GetRequiredService<IHostEnvironment>().EnvironmentName;
        var queuePrefix = services.GetRequiredService<BitweenOptions>().QueuePrefix;

        var problem = Check(services.GetRequiredService<BitweenDbContext>(), current, configuration[ConfirmSetting],
                () => FormerName(configuration.GetConnectionString("RabbitMQ"), queuePrefix, current),
                services.GetRequiredService<ILogger<BitweenDbContext>>())
            .GetAwaiter().GetResult();
        if (problem is not null) host.Services.GetRequiredService<StartupProblems>().Add(problem);
        return host;
    }

    /// <param name="formerName">
    /// For a Bitween that ran before names were recorded: the name its queues were made under.
    /// </param>
    /// <returns>The startup problem, or null when this name may run.</returns>
    public static async Task<string> Check(BitweenDbContext db, string current, string confirmed,
        Func<Task<string>> formerName, ILogger logger)
    {
        var row = await db.Set<Setting>().SingleOrDefaultAsync(s => s.Id == RecordedKey);
        var recorded = row?.Value;

        // A new installation has nothing to strand. One already in use tells by its queues.
        if (recorded is null && await db.Set<Subscription>().AnyAsync())
        {
            try
            {
                recorded = await formerName();
            }
            catch (Exception ex)
            {
                // Not left to a later start: by then the bus has made queues under the new name, and they
                // would be all that start finds. Without the broker the bus can't run anyway.
                logger.LogWarning(ex, "Could not reach RabbitMQ to tell which environment this Bitween ran as before.");
                return "RabbitMQ could not be reached to tell which ASPNETCORE_ENVIRONMENT this Bitween's queues were " +
                       $"made under ({ex.GetBaseException().Message}). Check ConnectionStrings:RabbitMQ and that the " +
                       "broker is up.";
            }
        }

        if (recorded is not null && !Same(recorded, current) && !Same(confirmed, current))
            return $"ASPNETCORE_ENVIRONMENT is {current}, but this Bitween's RabbitMQ queues are named after " +
                   $"{recorded}, which it ran with before. With the new name it would read from new queues, " +
                   "while other applications on this RabbitMQ keep sending to the old ones. If the change is a " +
                   $"mistake, set ASPNETCORE_ENVIRONMENT (the chart's global.environment) back to {recorded}. If " +
                   $"every application on this RabbitMQ moved to {current} on purpose, set " +
                   $"Bitween__ConfirmBusEnvironment={current}.";

        if (Same(row?.Value, current)) return null;

        if (row is null) db.Add(new Setting { Id = RecordedKey, Value = current });
        else row.Value = current;
        try
        {
            await db.SaveChangesAsync();
            logger.LogInformation("Bitween's RabbitMQ queues are named after the environment {Environment}.", current);
        }
        catch (DbUpdateException ex)
        {
            // Another node starting at the same moment recorded it first, with the same name.
            logger.LogWarning(ex, "The bus environment was recorded by another node.");
        }

        return null;
    }

    private static async Task<string> FormerName(string connectionString, string queuePrefix, string current)
    {
        await using var broker = await BrokerQueues.ConnectAsync(connectionString);
        foreach (var name in UsualNames.Prepend(current).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            // The lane of exchanges without a work group, which every Bitween since work groups has;
            // before them, the queue of exchanges from API calls.
            if (await broker.InspectAsync(BrokerQueues.QueueName(name, queuePrefix, nameof(XchangeService),
                    WorkGroup.None.GetBusMessageName())) is not null ||
                await broker.InspectAsync(BrokerQueues.QueueName(name, queuePrefix, nameof(XchangeService),
                    nameof(ApiXchangeCreatedEvent))) is not null)
                return name;
        }

        // Nothing under any of them, so nothing to leave behind.
        return current;
    }

    // The bus lowercases the name in every exchange and queue.
    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
