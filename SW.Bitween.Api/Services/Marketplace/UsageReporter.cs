using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Services.Adapters;
using SW.Bitween.Services.Metrics;
using SW.PrimitiveTypes;
using SW.Serverless.Contract.Catalog;

namespace SW.Bitween.Services.Marketplace;

/// <summary>What one day's report holds, shown on the Settings page before it is turned on, and after each send.</summary>
public record UsageReportPreview(DateOnly Day, IReadOnlyList<RegistryUsageLine> Lines);

/// <summary>
/// The daily usage report: for each connector installed from the registry, yesterday's calls and
/// failures from the adapter metrics Bitween already keeps, and how many subscriptions name it. No
/// payloads, no partner or subscription names, no hostnames. Off unless Settings turns it on.
/// </summary>
public class UsageReporter(BitweenDbContext dbContext, RegistryClient registry, BitweenOptions options, AdapterCatalog catalog, RegistryUpdates updates)
{
    public bool Enabled => options.RegistryUsageReports && registry.Configured && registry.Registered;

    /// <summary>The report for a day, as it would be sent.</summary>
    public async Task<UsageReportPreview> PreviewAsync(DateOnly day)
    {
        var from = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var to = from.AddDays(1);
        var fromRegistry = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in await updates.InstalledFromRegistryAsync()) fromRegistry.Add(entry.Id);
        if (fromRegistry.Count == 0) return new UsageReportPreview(day, []);

        var stats = await MetricsQueries.Adapters(dbContext, from, to)
            .GroupBy(s => new { s.AdapterId, s.Version })
            .Select(g => new { g.Key.AdapterId, g.Key.Version, Calls = g.Sum(x => (long)x.Calls), Failures = g.Sum(x => (long)x.Failures) })
            .ToListAsync();

        // How many subscriptions name each adapter, in any role, by its catalog id.
        var subscriptions = await dbContext.Set<Subscription>().AsNoTracking()
            .Select(s => new { s.HandlerId, s.MapperId, s.ValidatorId, s.ReceiverId })
            .ToListAsync();
        int UsedBy(string id) => subscriptions.Count(s => new[] { s.HandlerId, s.MapperId, s.ValidatorId, s.ReceiverId }
            .Any(r => r != null && string.Equals(AdapterCatalogPaths.Split(r).AdapterId, id, StringComparison.OrdinalIgnoreCase)));

        var lines = stats.Where(s => fromRegistry.Contains(s.AdapterId))
            .Select(s => new RegistryUsageLine(s.AdapterId.ToLowerInvariant(), string.IsNullOrEmpty(s.Version) ? "unknown" : s.Version, UsedBy(s.AdapterId), s.Calls, s.Failures))
            .ToList();
        // A connector installed and used by subscriptions but not called that day still counts as in use.
        foreach (var id in fromRegistry.Where(id => lines.All(l => !string.Equals(l.PackageId, id, StringComparison.OrdinalIgnoreCase))))
        {
            var used = UsedBy(id);
            if (used > 0) lines.Add(new RegistryUsageLine(id.ToLowerInvariant(), (await catalog.GetAsync(id))?.Current ?? "unknown", used, 0, 0));
        }
        return new UsageReportPreview(day, lines);
    }

    /// <summary>Sends yesterday's report. Nothing when off, unregistered, or there is nothing to say.</summary>
    public async Task<UsageReportPreview> SendAsync(DateOnly? day = null)
    {
        if (!Enabled) throw new SWValidationException("USAGE_OFF", "Usage reports are off, or this Bitween isn't registered with the registry.");
        var report = await PreviewAsync(day ?? DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1));
        if (report.Lines.Count > 0) await registry.ReportUsageAsync(report.Day, BitweenInfo.Version(options).ToString(), report.Lines);
        return report;
    }
}
