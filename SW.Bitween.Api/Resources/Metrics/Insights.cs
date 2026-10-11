using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Gateway;
using SW.Bitween.Services.Adapters;
using SW.Bitween.Services.Metrics;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Metrics;

/// <summary>
/// Housekeeping: configuration that is switched off, unused or out of date, each with a link to
/// it. Nothing here is wrong as such; it is what to look at when tidying up an instance.
/// </summary>
[HandlerName("insights")]
public class Insights(BitweenDbContext db, RequestContext requestContext, IMemoryCache cache, AdapterCatalog catalog)
    : IQueryHandler<object>
{
    const int Listed = 10;

    public record Item(string Label, string Href);

    public record Insight(string Kind, string Severity, string Title, string Detail, int Count, IEnumerable<Item> Items);

    public async Task<object> Handle()
    {
        await requestContext.EnsurePermission(db, Model.Permissions.Dashboard.View);
        return await cache.Cached("insights", Compute);
    }

    async Task<object> Compute()
    {
        var to = MetricsRecorder.HourOf(DateTime.UtcNow).AddHours(1);
        var since = await MetricsQueries.SinceAsync(db);
        var insights = new List<Insight>();

        var subscriptions = await db.Set<Subscription>().AsNoTracking()
            .Select(s => new
            {
                s.Id, s.Name, s.Inactive, s.PausedOn, s.PausedAutomatically, s.RetryPolicyId, s.WorkGroupId, s.PartnerId,
                s.HandlerId, s.HandlerVersion, s.MapperId, s.MapperVersion,
            }).ToListAsync();
        Item Link(int id, string name) => new(name, $"/subscriptions/{id}");

        var paused = subscriptions.Where(s => !s.Inactive && s.PausedOn != null).ToList();
        if (paused.Count > 0)
            insights.Add(new("paused", "warn", "Paused subscriptions",
                $"{paused.Count(s => s.PausedAutomatically)} paused themselves after repeated failures.",
                paused.Count, paused.Take(Listed).Select(s => Link(s.Id, s.Name))));

        var inactive = subscriptions.Where(s => s.Inactive).ToList();
        if (inactive.Count > 0)
            insights.Add(new("inactive", "info", "Switched-off subscriptions", "Kept, but not running.",
                inactive.Count, inactive.Take(Listed).Select(s => Link(s.Id, s.Name))));

        // Quiet for 30 days - only once the metrics reach that far back.
        if (since != null && since <= to.AddDays(-30))
        {
            var active = (await MetricsQueries.Exchanges(db, to.AddDays(-30), to).Where(s => s.Total > 0)
                .Select(s => s.SubscriptionId).Distinct().ToListAsync()).ToHashSet();
            var unused = subscriptions.Where(s => !s.Inactive && s.PausedOn == null && !active.Contains(s.Id)).ToList();
            if (unused.Count > 0)
                insights.Add(new("unused-subscriptions", "info", "Subscriptions without traffic for 30 days",
                    "Running, but nothing has gone through them.", unused.Count, unused.Take(Listed).Select(s => Link(s.Id, s.Name))));
        }

        // Pinned to a version that has since been withdrawn: it keeps running, but nobody can pin it again.
        var withdrawn = new List<Item>();
        foreach (var s in subscriptions.Where(s => s.HandlerVersion != null || s.MapperVersion != null))
            foreach (var (id, version) in new[] { (s.HandlerId, s.HandlerVersion), (s.MapperId, s.MapperVersion) })
                if (id != null && version != null && (await catalog.GetAsync(id))?.Find(version)?.Withdrawn == true)
                    withdrawn.Add(new($"{s.Name} (pins {id} v{version})", $"/subscriptions/{s.Id}"));
        if (withdrawn.Count > 0)
            insights.Add(new("withdrawn-pins", "warn", "Pinned to a withdrawn version",
                "Still runs it, but the version is withdrawn. Move it to a current version.", withdrawn.Count, withdrawn.Take(Listed)));

        var usedPolicies = subscriptions.Where(s => s.RetryPolicyId != null).Select(s => s.RetryPolicyId.Value).ToHashSet();
        var policies = await db.Set<RetryPolicy>().AsNoTracking().Select(p => new { p.Id, p.Name }).ToListAsync();
        var unusedPolicies = policies.Where(p => !usedPolicies.Contains(p.Id)).ToList();
        if (unusedPolicies.Count > 0)
            insights.Add(new("unused-retry-policies", "info", "Retry policies nothing uses", "No subscription is assigned them.",
                unusedPolicies.Count, unusedPolicies.Take(Listed).Select(p => new Item(p.Name, $"/retry-policies/{p.Id}"))));

        var usedGroups = subscriptions.Where(s => s.WorkGroupId != null).Select(s => s.WorkGroupId.Value).ToHashSet();
        var groups = await db.Set<WorkGroup>().AsNoTracking().Select(g => new { g.Id, g.Name }).ToListAsync();
        var unusedGroups = groups.Where(g => !usedGroups.Contains(g.Id)).ToList();
        if (unusedGroups.Count > 0)
            insights.Add(new("unused-work-groups", "info", "Work groups nothing uses", "Their queues are kept for no subscription.",
                unusedGroups.Count, unusedGroups.Take(Listed).Select(g => new Item(g.Name, $"/work-groups/{g.Id}"))));

        var linked = subscriptions.Where(s => s.PartnerId != null).Select(s => s.PartnerId.Value)
            .Concat(await db.Set<ApiGatewayPartner>().AsNoTracking().Select(a => a.PartnerId).ToListAsync())
            .Concat(await db.Set<BusGatewayRoute>().AsNoTracking().Where(r => r.PartnerId != null).Select(r => r.PartnerId.Value).ToListAsync())
            .ToHashSet();
        var partners = await db.Set<Partner>().AsNoTracking().Where(p => p.Id != Partner.SystemId)
            .Select(p => new { p.Id, p.Name }).ToListAsync();
        var loose = partners.Where(p => !linked.Contains(p.Id)).ToList();
        if (loose.Count > 0)
            insights.Add(new("unlinked-partners", "info", "Partners not connected to anything",
                "Not attached to a gateway, a route or a subscription.", loose.Count,
                loose.Take(Listed).Select(p => new Item(p.Name, $"/partners/{p.Id}"))));

        var changedSettings = await db.Set<Setting>().AsNoTracking().CountAsync();
        if (changedSettings > 0)
            insights.Add(new("changed-settings", "info", "Settings changed from their defaults",
                "What is particular about this instance.", changedSettings, [new Item("Settings", "/settings")]));

        return new { Since = since, Insights = insights };
    }
}
