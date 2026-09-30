using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SW.Bitween.Domain;

namespace SW.Bitween;

/// <summary>
/// Moves retry alerts that still carry their own handler onto notification channels, so they keep
/// sending after alerts started going through channels only.
/// </summary>
/// <remarks>
/// <para>
/// Runs on every boot and does nothing once there is nothing left to move: each row it touches has
/// its handler cleared. Done in code rather than in a migration because most of what it moves sits
/// inside a JSON column — a policy's groups, a subscription's inline policy — that SQL cannot
/// rewrite alike on PostgreSQL, SQL Server and MySQL.
/// </para>
/// <para>
/// Every distinct handler setup becomes one channel, reused by every level that had the same one, so
/// a relay configured identically on ten groups is one channel to maintain rather than ten.
/// </para>
/// </remarks>
public static class RetryAlertChannelConversion
{
    public static IHost MoveRetryAlertsToChannels(this IHost host)
    {
        using var scope = host.Services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<NotificationChannel>>();
        try
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var moved = Run(dbContext).GetAwaiter().GetResult();
            if (moved > 0)
                logger.LogInformation("Moved {Count} retry alert settings onto notification channels.", moved);
        }
        catch (Exception ex)
        {
            // Not fatal: until it succeeds, the alerts it would have moved stay silent, and the next
            // boot tries again. Stopping the whole service over them would be the worse outage.
            logger.LogError(ex, "Could not move retry alerts onto notification channels; they will not send until this succeeds.");
        }

        return host;
    }

    /// <summary>Returns how many alert settings it moved.</summary>
    public static async Task<int> Run(BitweenDbContext dbContext)
    {
        var policies = await dbContext.Set<RetryPolicy>().ToListAsync();
        var inlinePolicies = await dbContext.Set<Subscription>()
            .Where(s => s.CustomRetryPolicy != null)
            .ToListAsync();
        var overrides = await dbContext.Set<RetryAlertOverride>()
            .Where(o => o.AlertHandlerId != null)
            .ToListAsync();

        var hasWork = overrides.Count > 0
                      || policies.Any(p => !string.IsNullOrWhiteSpace(p.AlertHandlerId)
                                           || p.Groups.Any(g => !string.IsNullOrWhiteSpace(g.AlertHandlerId)))
                      || inlinePolicies.Any(s =>
                          s.CustomRetryPolicy.Groups.Any(g => !string.IsNullOrWhiteSpace(g.AlertHandlerId)));
        if (!hasWork) return 0;

        await using var transaction = await dbContext.Database.BeginTransactionAsync();

        var channels = await dbContext.Set<NotificationChannel>().ToListAsync();
        var moved = 0;

        async Task<int> ChannelFor(string handlerId, IReadOnlyDictionary<string, string> properties, string name)
        {
            moved++;
            var key = Fingerprint(handlerId, properties);
            var existing = channels.FirstOrDefault(c => Fingerprint(c.HandlerId, c.HandlerProperties) == key);
            if (existing != null) return existing.Id;

            var channel = new NotificationChannel
            {
                Name = UniqueName(name, channels),
                HandlerId = handlerId,
                HandlerProperties = properties?.ToDictionary(kv => kv.Key, kv => kv.Value) ?? []
            };
            dbContext.Add(channel);
            // Saved on its own so it has an id to point at; the transaction still makes the whole
            // move land at once or not at all.
            await dbContext.SaveChangesAsync();
            channels.Add(channel);
            return channel.Id;
        }

        foreach (var policy in policies)
        {
            if (!string.IsNullOrWhiteSpace(policy.AlertHandlerId))
            {
                policy.AlertChannelId ??= await ChannelFor(policy.AlertHandlerId, policy.AlertHandlerProperties,
                    $"Retry alert: {policy.Name}");
                policy.AlertHandlerId = null;
                policy.AlertHandlerProperties = null;
            }

            if (await MoveGroups(policy.Groups, policy.Name, ChannelFor))
                dbContext.Entry(policy).Property(p => p.Groups).IsModified = true;
        }

        foreach (var subscription in inlinePolicies)
            if (await MoveGroups(subscription.CustomRetryPolicy.Groups, subscription.Name, ChannelFor))
                dbContext.Entry(subscription).Property(s => s.CustomRetryPolicy).IsModified = true;

        if (overrides.Count > 0)
        {
            var subscriptionIds = overrides.Select(o => o.SubscriptionId).Distinct().ToList();
            var names = await dbContext.Set<Subscription>().AsNoTracking()
                .Where(s => subscriptionIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => s.Name);

            foreach (var alertOverride in overrides)
            {
                var group = policies.SelectMany(p => p.Groups).FirstOrDefault(g => g.Id == alertOverride.GroupId);
                var subscriptionName = names.GetValueOrDefault(alertOverride.SubscriptionId)
                                       ?? $"Subscription {alertOverride.SubscriptionId}";
                alertOverride.AlertChannelId ??= await ChannelFor(alertOverride.AlertHandlerId,
                    alertOverride.AlertHandlerProperties,
                    $"Retry alert: {subscriptionName} › {group?.Name ?? "group"}");
                alertOverride.AlertHandlerId = null;
                alertOverride.AlertHandlerProperties = null;
            }
        }

        await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();
        return moved;
    }

    private static async Task<bool> MoveGroups(IEnumerable<Model.RetryGroup> groups, string ownerName,
        Func<string, IReadOnlyDictionary<string, string>, string, Task<int>> channelFor)
    {
        var changed = false;
        foreach (var group in groups ?? [])
        {
            if (string.IsNullOrWhiteSpace(group.AlertHandlerId)) continue;

            group.AlertChannelId ??= await channelFor(group.AlertHandlerId, group.AlertHandlerProperties,
                $"Retry alert: {ownerName} › {group.Name}");
            group.AlertHandlerId = null;
            group.AlertHandlerProperties = null;
            changed = true;
        }

        return changed;
    }

    /// <summary>The same handler with the same settings, whatever order the keys were saved in.</summary>
    private static string Fingerprint(string handlerId, IReadOnlyDictionary<string, string> properties) =>
        handlerId + "|" + JsonConvert.SerializeObject(
            (properties ?? new Dictionary<string, string>()).OrderBy(kv => kv.Key, StringComparer.Ordinal));

    private static string UniqueName(string wanted, List<NotificationChannel> channels)
    {
        var name = wanted.Length > 90 ? wanted[..90] : wanted;
        var candidate = name;
        for (var n = 2; channels.Any(c => c.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)); n++)
            candidate = $"{name} ({n})";
        return candidate;
    }
}
