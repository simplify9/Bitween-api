using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Cli;
using SW.Bitween.Cli.Configuration;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Gateway;
using SW.PrimitiveTypes;
using static SW.Bitween.Services.Mirror.MirrorCompare;

namespace SW.Bitween.Services.Mirror;

public sealed partial class MirrorService
{
    /// <summary>
    /// The field of a list's filter on how its items stand against prod: fromProd, untested, same, check,
    /// different or noTraffic. See <see cref="FilterAsync"/>.
    /// </summary>
    public const string ListFilter = "Mirror";

    /// <summary>Prod's items of each kind, by name, as last read: the lists ask every few seconds while tests run, and prod's setup seldom changes.</summary>
    internal static readonly ConcurrentDictionary<(string Url, string Resource), (DateTimeOffset At, HashSet<string> Names)> ProdNames = new();

    /// <summary>One of staging's items that prod has too, and the subscriptions prod has too it is or leads to: what testing it tests.</summary>
    sealed record OnProd(int Id, string Key, string Type, IReadOnlyList<string> Subscriptions, bool Custom);

    /// <summary>
    /// Staging's items of a kind that prod has too, by name, each with how its test last ran: a
    /// subscription's own, a gateway's the worst of the subscriptions it leads to. Read from prod's list
    /// alone. Why prod can't be read, when it can't.
    /// </summary>
    public async Task<MirrorListed> ListedAsync(string section)
    {
        var (items, error) = await OnProdAsync(section);
        if (error != null) return new MirrorListed([], null, [], error);
        var verdicts = await ReadVerdictsAsync();
        return new MirrorListed(items.Select(i => Listed(i, verdicts)).ToList(), null, []);
    }

    /// <summary>The subscriptions that testing some of staging's items of a kind tests, by name: those prod has too.</summary>
    public async Task<IReadOnlyList<string>> SubscriptionsOfAsync(string section, IReadOnlyCollection<int> ids)
    {
        var (items, error) = await OnProdAsync(section);
        if (error != null) throw new MirrorRefusal(error);
        return items.Where(i => ids.Contains(i.Id)).SelectMany(i => i.Subscriptions).Distinct().ToList();
    }

    async Task<(List<OnProd> Items, string Error)> OnProdAsync(string section)
    {
        var kind = KindToBring(section);
        HashSet<string> onProd, subscriptionsOnProd;
        try
        {
            onProd = await ProdNamesAsync(kind.Resource);
            subscriptionsOnProd = kind.Section == "subscriptions" ? onProd : await ProdNamesAsync(Kinds.Of("subscriptions").Resource);
        }
        catch (MirrorRefusal refusal)
        {
            return ([], refusal.Message);
        }
        catch (Exception ex) when (ex is BitweenApiException or HttpRequestException or TaskCanceledException)
        {
            return ([], $"Prod couldn't be read: {ex.Message}");
        }

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var subscriptions = (await db.Set<Subscription>().AsNoTracking()
                .Select(s => new { s.Id, s.Name, s.Type, Adapters = new[] { s.ReceiverId, s.ValidatorId, s.MapperId, s.HandlerId } }).ToListAsync())
            .ToDictionary(s => s.Id, s => new { s.Id, s.Name, s.Type, Custom = s.Adapters.Any(a => a != null && !NativeAdapterDiscoveryService.IsNative(a)) });
        var leading = kind.Section switch
        {
            "apiGateways" => await db.Set<ApiGateway>().AsNoTracking()
                .Select(g => new { g.Id, g.Name, Type = (string)null, To = g.Partners.Select(p => p.SubscriptionId).ToList() }).ToListAsync(),
            "busGateways" => await db.Set<BusGateway>().AsNoTracking()
                .Select(g => new { g.Id, g.Name, Type = (string)null, To = g.Routes.Select(r => r.SubscriptionId).ToList() }).ToListAsync(),
            _ => subscriptions.Values.Select(s => new { s.Id, s.Name, Type = s.Type.ToString(), To = new List<int> { s.Id } }).ToList(),
        };
        var items = leading.Where(i => onProd.Contains(i.Name))
            .Select(i => new OnProd(i.Id, i.Name, i.Type,
                i.To.Distinct().Select(id => subscriptions.GetValueOrDefault(id)?.Name).Where(n => n != null && subscriptionsOnProd.Contains(n)).ToList(),
                i.To.Any(id => subscriptions.GetValueOrDefault(id)?.Custom == true)))
            .OrderBy(i => i.Key, StringComparer.OrdinalIgnoreCase).ToList();
        return (items, null);
    }

    static MirrorListedItem Listed(OnProd item, Dictionary<string, MirrorTestVerdict> verdicts)
    {
        var tested = item.Subscriptions.Select(verdicts.GetValueOrDefault).Where(v => v != null).ToList();
        var ran = tested.Where(v => v.Verdict != NoTraffic).ToList();
        var verdict = tested.Count == 0 ? null
            : ran.Count == 0 ? NoTraffic
            : ran.Any(v => v.Verdict == Different) ? Different
            : ran.Any(v => v.Verdict == Check) ? Check
            : Same;
        return new MirrorListedItem(item.Id, item.Key, item.Type, verdict, tested.Count == 0 ? null : tested.Max(v => v.RanOn), tested.Count,
            item.Subscriptions.Count, item.Custom);
    }

    /// <summary>The names of prod's items of a kind, from prod's list of them, read again once a minute old.</summary>
    async Task<HashSet<string>> ProdNamesAsync(string resource)
    {
        var key = (options.Mirror.SourceUrl, resource);
        if (ProdNames.TryGetValue(key, out var read) && DateTimeOffset.UtcNow - read.At < TimeSpan.FromMinutes(1)) return read.Names;

        using var prod = Connect();
        await VersionAsync(prod);
        var names = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            for (var page = 0; ; page++)
            {
                var answer = await prod.GetAsync($"api/{resource}?page={page}&size=500");
                var rows = (answer?["result"] as JsonArray ?? []).OfType<JsonObject>().ToList();
                foreach (var row in rows)
                    if ((string)row["name"] is { } name)
                        names.Add(name);
                if (rows.Count < 500 || names.Count >= ((int?)answer?["totalCount"] ?? names.Count)) break;
            }
        }
        // A prod whose release has no such list, or whose login can't read it, has none to show.
        catch (BitweenApiException ex) when (ex.Status is HttpStatusCode.NotFound or HttpStatusCode.Forbidden) { }
        ProdNames[key] = (DateTimeOffset.UtcNow, names);
        return names;
    }

    /// <summary>
    /// Turns a list's <see cref="ListFilter"/> into the ids of the items it keeps, so the list counts and
    /// pages them as usual however many there are. Refused with why when prod can't be read.
    /// </summary>
    public async Task FilterAsync(SearchyRequest request, string section)
    {
        foreach (var condition in request.Conditions ?? [])
        {
            var filters = condition.Filters.Where(f => string.Equals(f.Field, ListFilter, StringComparison.OrdinalIgnoreCase)).ToList();
            if (filters.Count == 0) continue;
            var listed = await ListedAsync(section);
            if (listed.Error != null) throw new SWValidationException("MIRROR_PROD", listed.Error);
            foreach (var filter in filters)
            {
                condition.Filters.Remove(filter);
                var wanted = filter.Value as string;
                var ids = listed.Items
                    .Where(i => wanted switch { "fromProd" => true, "untested" => i.Verdict == null, _ => i.Verdict == wanted })
                    .Select(i => i.Id).ToArray();
                condition.Filters.Add(new SearchyFilter("Id", SearchyRule.EqualsToList, ids));
            }
        }
    }
}
