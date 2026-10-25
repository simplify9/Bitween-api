using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using SW.Bitween.Cli;
using SW.Bitween.Cli.Configuration;
using SW.PrimitiveTypes;

namespace SW.Bitween.Services.Mirror;

public sealed partial class MirrorService
{
    /// <summary>How far back the traffic a page shows goes.</summary>
    const int TrafficDays = 7;

    /// <summary>The kinds a page offers to bring from prod.</summary>
    static readonly string[] Bringable = ["subscriptions", "apiGateways", "busGateways"];

    /// <summary>A global values set, as {{globals.set.key}} and a mapper's __globals__.set name it.</summary>
    static readonly Regex GlobalsNamed = new(@"(?:\bglobals|__globals__)\??\.([A-Za-z0-9_\-]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>A connection, as {{connections.name.accessToken}} names it.</summary>
    static readonly Regex ConnectionsNamed = new(@"\{\{\s*connections\.([A-Za-z0-9_\-]+)\.", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // ------------------------------------------------------------------ what prod has

    /// <summary>
    /// How many of prod's items there are of each kind a page lists, read from prod's lists alone: cheap
    /// enough for every page that offers to bring them. Why prod can't be read, when it can't.
    /// </summary>
    public async Task<MirrorProdKinds> ProdKindsAsync()
    {
        try
        {
            using var prod = Connect();
            await VersionAsync(prod);
            var types = new Dictionary<string, int>(StringComparer.Ordinal);
            var all = 0;
            for (var page = 0; ; page++)
            {
                var answer = await prod.GetAsync($"api/subscriptions?page={page}&size=500");
                var rows = (answer?["result"] as JsonArray ?? []).OfType<JsonObject>().ToList();
                foreach (var type in rows.Select(r => (string)r["type"]))
                {
                    all++;
                    if (type != null) types[type] = types.GetValueOrDefault(type) + 1;
                }
                if (rows.Count < 500 || all >= ((int?)answer?["totalCount"] ?? all)) break;
            }
            types["all"] = all;
            return new MirrorProdKinds(types, await TotalAsync(prod, "apigateways"), await TotalAsync(prod, "busgateways"));
        }
        catch (MirrorRefusal refusal)
        {
            return new MirrorProdKinds(new Dictionary<string, int>(), 0, 0, refusal.Message);
        }
        catch (Exception ex) when (ex is BitweenApiException or HttpRequestException or TaskCanceledException)
        {
            return new MirrorProdKinds(new Dictionary<string, int>(), 0, 0, $"Prod couldn't be read: {ex.Message}");
        }
    }

    static async Task<int> TotalAsync(BitweenApi prod, string resource)
    {
        try { return (int?)(await prod.GetAsync($"api/{resource}?page=0&size=1"))?["totalCount"] ?? 0; }
        catch (BitweenApiException ex) when (ex.Status is HttpStatusCode.NotFound or HttpStatusCode.Forbidden) { return 0; }
    }

    /// <summary>
    /// Prod's items of one kind - subscriptions, of one type if asked, or API or bus gateways - each with
    /// its traffic over the last week and whether staging has it, busiest first. A gateway's traffic is
    /// that of the subscriptions it leads to.
    /// </summary>
    public async Task<MirrorProdList> ProdListAsync(string section, string type)
    {
        var kind = KindToBring(section);
        using var comparison = await CompareAsync(removeExtras: false, copyKey: null);
        var (days, note, traffic) = await TrafficAsync(comparison.Prod);
        var subscriptionIds = comparison.Parity.Kinds.First(k => k.Section == "subscriptions").Items
            .Where(i => i.ProdId != null).GroupBy(i => i.Key).ToDictionary(g => g.Key, g => g.First().ProdId, StringComparer.Ordinal);
        MirrorProdCounts Of(string subscription) =>
            subscription != null && subscriptionIds.TryGetValue(subscription, out var id) ? traffic.GetValueOrDefault(id) : null;

        var index = Index(comparison.Copy.Sections);
        var items = new List<MirrorProdItem>();
        foreach (var row in comparison.Parity.Kinds.First(k => k.Section == kind.Section).Items)
        {
            if (row.State == "extra" || (type != null && row.Type != type)) continue;
            var item = index.GetValueOrDefault((kind.Section, row.Key));
            var counts = kind.Section == "subscriptions" ? Of(row.Key)
                : Sum(LinksOf(kind.Section, item).Select(l => l.Subscription).Distinct().Select(Of).Where(c => c != null).ToList());
            items.Add(new MirrorProdItem(row.Key, row.ProdId, row.Type, DetailOf(kind.Section, item), row.State, row.StagingId, row.Reason, counts));
        }
        items = (days == null ? items.OrderBy(i => i.Key, StringComparer.OrdinalIgnoreCase)
            : items.OrderByDescending(i => i.Traffic?.Total ?? 0).ThenBy(i => i.Key, StringComparer.OrdinalIgnoreCase)).ToList();
        return new MirrorProdList(kind.Section, kind.Noun, Plural(kind.Noun), days, note, items);
    }

    static Kind KindToBring(string section) =>
        Bringable.Contains(section) ? Kinds.Of(section)
        : throw new SWValidationException("MIRROR_SECTION", $"Bring subscriptions, API gateways or bus gateways from prod, not “{section}”.");

    /// <summary>
    /// Each of prod's subscriptions' traffic over the last week, by id, from the counts prod keeps by the
    /// hour. Without them prod could only say by reading through its exchanges, a heavy read on a busy
    /// prod, so a prod whose release doesn't keep them shows none, and why.
    /// </summary>
    static async Task<(int? Days, string Note, Dictionary<string, MirrorProdCounts> Counts)> TrafficAsync(BitweenApi prod)
    {
        JsonNode answer;
        try { answer = await prod.GetAsync($"api/metrics/subscriptions?days={TrafficDays}"); }
        catch (BitweenApiException ex) when (ex.Status == HttpStatusCode.NotFound)
        {
            return (null, "Prod's release doesn't count traffic yet, so there are no numbers until prod runs one that does.", []);
        }
        catch (BitweenApiException ex) when (ex.Status == HttpStatusCode.Forbidden)
        {
            return (null, "Staging's login on prod can't read prod's traffic. On prod, give its role permission to view subscriptions.", []);
        }
        var counts = new Dictionary<string, MirrorProdCounts>(StringComparer.Ordinal);
        foreach (var row in (answer?["subscriptions"] as JsonArray ?? []).OfType<JsonObject>())
            if (row["subscriptionId"]?.ToString() is { } id)
                counts[id] = new MirrorProdCounts((long?)row["total"] ?? 0, (long?)row["failed"] ?? 0, (DateTimeOffset?)row["lastExchangeHour"],
                    (row["daily"] as JsonArray ?? []).Select(d => (long?)d ?? 0).ToList());
        return ((int?)answer?["days"] ?? TrafficDays, null, counts);
    }

    static MirrorProdCounts Sum(IReadOnlyList<MirrorProdCounts> counts) =>
        counts.Count == 0 ? null
        : new MirrorProdCounts(counts.Sum(c => c.Total), counts.Sum(c => c.Failed), counts.Max(c => c.LastOn),
            Enumerable.Range(0, counts.Max(c => c.Daily.Count)).Select(i => counts.Sum(c => i < c.Daily.Count ? c.Daily[i] : 0)).ToList());

    /// <summary>What an item is for, in a few words: a subscription's partner and information type, a gateway's partners or routes.</summary>
    static string DetailOf(string section, JsonObject item)
    {
        var links = LinksOf(section, item).Count();
        string[] parts = section switch
        {
            "subscriptions" => [(string)item?["partner"], (string)item?["informationType"],
                (string)item?["aggregationFor"] is { } of ? $"rolls up “{of}”" : null],
            "apiGateways" => [Count(links, "partner")],
            _ => [(string)item?["informationType"], Count(links, "route")],
        };
        return string.Join(" · ", parts.Where(p => !string.IsNullOrEmpty(p)));
    }

    /// <summary>What a gateway leads to: each attached partner with its subscription, or each bus route.</summary>
    static IEnumerable<(string Subscription, string Partner)> LinksOf(string section, JsonObject item) =>
        section is not ("apiGateways" or "busGateways") ? []
        : (item?[section == "apiGateways" ? "partners" : "routes"] as JsonArray ?? []).OfType<JsonObject>()
            .Select(l => ((string)l["subscription"], (string)l["partner"]))
            .Where(l => l.Item1 != null);

    // ------------------------------------------------------------------ bringing it

    /// <summary>
    /// Brings prod's picked items onto staging with what they need to run as on prod (see <see cref="Needs"/>).
    /// What staging doesn't have comes as prod has it, faked as a copy fakes it; what staging has stays as
    /// staging has it, so nobody's edits are overwritten, except that a gateway staging has gets the partner
    /// or route that leads to what is brought. Nothing is removed. Without <paramref name="apply"/> it only
    /// says what it would do.
    /// </summary>
    public async Task<MirrorBring> BringAsync(string section, IReadOnlyCollection<string> keys, bool apply)
    {
        var kind = KindToBring(section);
        if (keys.Count == 0) throw new SWValidationException("MIRROR_NOTHING", $"Pick the {Plural(kind.Noun)} to bring from prod.");
        var copyKey = apply ? MirrorCopyGate.OpenKey() : null;
        try
        {
            using var comparison = await CompareAsync(removeExtras: false, copyKey);
            var bringing = await PlanBringingAsync(comparison, kind, keys);
            if (!apply) return new MirrorBring(false, bringing.Pieces, bringing.NeedsYou, []);

            var failed = await comparison.Planner.ApplyAllAsync(bringing.Plan);
            foreach (var change in bringing.Plan.Changes) LastFailures.TryRemove((change.Section, change.Key), out _);
            foreach (var (change, error) in failed) LastFailures[(change.Section, change.Key)] = error;
            var made = bringing.Pieces.Select(p => p.State == "new" ? p with { StagingId = IdOn(comparison.Staging, p.Section, p.Key, bringing.Index) } : p).ToList();
            return new MirrorBring(true, made, bringing.NeedsYou, failed.Select(f => new MirrorFailure(f.Change.Section, f.Change.Noun, f.Change.Key, f.Error)).ToList());
        }
        finally
        {
            MirrorCopyGate.CloseKey(copyKey);
        }
    }

    /// <summary>What bringing some of prod's items takes, worked out on a comparison, with the plan that brings them.</summary>
    /// <param name="Needs">The picked items and everything that comes with them, each with why.</param>
    /// <param name="Index">Prod's items, as the copy has them, by kind and key.</param>
    /// <param name="Adapters">The adapters what comes runs, prod's beside staging's.</param>
    sealed record Bringing(List<MirrorPiece> Pieces, List<MirrorNote> NeedsYou, Plan Plan, Dictionary<(string Section, string Key), string> Needs,
        Dictionary<(string Section, string Key), JsonObject> Index, List<MirrorAdapter> Adapters);

    async Task<Bringing> PlanBringingAsync(Comparison comparison, Kind kind, IReadOnlyCollection<string> keys)
    {
        var index = Index(comparison.Copy.Sections);
        if (keys.FirstOrDefault(k => !index.ContainsKey((kind.Section, k))) is { } gone)
            throw new MirrorRefusal($"Prod doesn't have the {kind.Noun} “{gone}” any more.");

        var needs = Needs(comparison.Copy.Sections, keys.Select(k => (kind.Section, k)));
        var (pieces, wants) = Pieces(comparison, index, needs);
        var uses = AdapterUses(needs.Keys.GroupBy(n => n.Section)
            .ToDictionary(g => g.Key, g => g.Select(n => index[n]).ToList()));
        var adapters = comparison.Parity.Adapters.Where(a => uses.ContainsKey(a.Id)).Select(a => a with { UsedBy = uses[a.Id] }).ToList();
        foreach (var a in adapters.Where(a => a.State == "missing" && a.Source == "marketplace" && a.ProdVersion != null))
        {
            pieces.Add(new MirrorPiece(Kinds.Adapters, "adapter", $"{a.Id} {a.ProdVersion}", "install",
                $"{Capital(Count(a.UsedBy, "item"))} brought here use{(a.UsedBy == 1 ? "s" : "")} it."));
            Add(wants, Kinds.Adapters, new JsonObject { ["install"] = a.Id, ["version"] = a.ProdVersion, ["current"] = true });
        }

        var config = comparison.Config;
        config.Sections.Clear();
        foreach (var (s, items) in wants) config.Sections[s] = items;
        var plan = await comparison.Planner.PlanAsync(prune: false);
        var needsYou = AdapterNotes(adapters);
        needsYou.AddRange(plan.Problems.Distinct().Select(p => new MirrorNote($"problem:{p}", "needsYou", $"On staging: {p}")));
        return new Bringing(pieces, needsYou, plan, needs, index, adapters);
    }

    /// <summary>Where staging has an item now, as its page opens it: a SQL statement's is its data source's.</summary>
    static string IdOn(Context staging, string section, string key, Dictionary<(string Section, string Key), JsonObject> index)
    {
        var (s, k) = section == "dataSourceStatements" ? ("dataSources", (string)index[(section, key)]["dataSource"]) : (section, key);
        try { return staging.Has(s, k) ? staging.IdOf(s, k) : null; }
        catch (ConfigException) { return null; }
    }

    /// <summary>Prod's items, as the copy has them, by kind and key.</summary>
    static Dictionary<(string Section, string Key), JsonObject> Index(Dictionary<string, List<JsonObject>> sections)
    {
        var index = new Dictionary<(string, string), JsonObject>();
        foreach (var (section, items) in sections.Where(s => s.Key != Kinds.Adapters))
        foreach (var item in items)
            if (Kinds.Of(section).KeyOf(item) is { } key) index.TryAdd((section, key), item);
        return index;
    }

    /// <summary>
    /// The picked items and what each needs on staging to run as on prod, each with why it comes: what it
    /// names (see <see cref="Names"/>), and for a subscription, how prod's traffic reaches it - the API
    /// gateway a partner calls it through, the bus gateway that routes to it, the subscription whose
    /// responses it runs on - with what those need in turn. A picked gateway brings what it leads to, and a
    /// data source its SQL statements.
    /// </summary>
    internal static Dictionary<(string Section, string Key), string> Needs(Dictionary<string, List<JsonObject>> sections,
        IEnumerable<(string Section, string Key)> picked)
    {
        var index = Index(sections);
        var chosen = picked.ToHashSet();
        var needs = new Dictionary<(string, string), string>();
        var queue = new Queue<(string Section, string Key, string Why)>(chosen.Select(p => (p.Section, p.Key, "Picked.")));
        while (queue.Count > 0)
        {
            var (section, key, why) = queue.Dequeue();
            if (needs.ContainsKey((section, key)) || !index.TryGetValue((section, key), out var item)) continue;
            needs[(section, key)] = why;
            var noun = Kinds.Of(section).Noun;
            foreach (var (s, k, how) in Names(sections, section, item)) queue.Enqueue((s, k, $"The {noun} “{key}” {how ?? "uses it"}."));
            switch (section)
            {
                case "dataSources":
                    foreach (var statement in (sections.GetValueOrDefault("dataSourceStatements") ?? []).Where(st => (string)st["dataSource"] == key))
                        queue.Enqueue(("dataSourceStatements", Kinds.Of("dataSourceStatements").KeyOf(statement), $"One of the data source “{key}”'s SQL statements."));
                    break;
                case "apiGateways" or "busGateways" when chosen.Contains((section, key)):
                    foreach (var (subscription, partner) in LinksOf(section, item))
                    {
                        queue.Enqueue(("subscriptions", subscription, $"The {noun} “{key}” leads to it."));
                        if (partner != null) queue.Enqueue(("partners", partner, $"It reaches “{subscription}” through the {noun} “{key}”."));
                    }
                    break;
                case "subscriptions":
                    foreach (var gateway in sections.GetValueOrDefault("apiGateways") ?? [])
                    foreach (var (_, partner) in LinksOf("apiGateways", gateway).Where(l => l.Subscription == key))
                    {
                        queue.Enqueue(("apiGateways", (string)gateway["urlName"], $"Prod's partners reach “{key}” through it."));
                        if (partner != null) queue.Enqueue(("partners", partner, $"It calls “{key}” through the API gateway “{gateway["urlName"]}”."));
                    }
                    foreach (var gateway in sections.GetValueOrDefault("busGateways") ?? [])
                    foreach (var (_, partner) in LinksOf("busGateways", gateway).Where(l => l.Subscription == key))
                    {
                        queue.Enqueue(("busGateways", (string)gateway["name"], $"It routes prod's bus messages to “{key}”."));
                        if (partner != null) queue.Enqueue(("partners", partner, $"“{key}” runs as it on what the bus gateway “{gateway["name"]}” routes."));
                    }
                    foreach (var source in (sections.GetValueOrDefault("subscriptions") ?? []).Where(s => (string)s["responseSubscription"] == key))
                        queue.Enqueue(("subscriptions", (string)source["name"], $"“{key}” runs on its responses."));
                    break;
            }
        }
        return needs;
    }

    /// <summary>
    /// What one of prod's items names, which staging needs before it can have it: what its fields refer to,
    /// the notification channels a subscription notifies and its retry alerts go to, and the global values
    /// sets and connections its adapters name in {{globals.…}} and {{connections.…}}. A gateway's partners
    /// and routes aren't among them: a gateway comes with those that can come (see <see cref="Pieces"/>).
    /// </summary>
    /// <returns>Each with how the item uses it, when there's more to say than that it does.</returns>
    static IEnumerable<(string Section, string Key, string How)> Names(Dictionary<string, List<JsonObject>> sections, string section, JsonObject item)
    {
        foreach (var f in Kinds.Of(section).Fields.Where(f => f.Ref != null && item.ContainsKey(f.File)))
        foreach (var name in f.Mode == FieldMode.RefList ? (item[f.File] as JsonArray ?? []).Select(x => x?.ToString()) : [item[f.File]?.ToString()])
            if (!string.IsNullOrEmpty(name))
                yield return (f.Ref, name, f.File switch
                {
                    "responseSubscription" => "hands its responses to it",
                    "aggregationFor" => "rolls up its exchanges",
                    _ => null,
                });

        if (section == "retryPolicies")
            foreach (var channel in AlertChannels(item["groups"])) yield return ("notificationChannels", channel, "sends its alerts there");
        if (section != "subscriptions") yield break;

        foreach (var row in (item["notifications"] as JsonArray ?? []).OfType<JsonObject>())
            if ((string)row["channel"] is { Length: > 0 } channel) yield return ("notificationChannels", channel, "notifies there");
        foreach (var channel in AlertChannels(item["customRetryPolicy"]?["groups"])) yield return ("notificationChannels", channel, "sends its alerts there");

        var texts = new[] { "receiver", "validator", "mapper", "handler" }
            .SelectMany(role => item[role]?["properties"] as JsonObject ?? [])
            .Select(p => p.Value is JsonValue v && v.TryGetValue<string>(out var s) ? s : null).Where(t => t != null).ToList();
        foreach (var set in Named(texts, GlobalsNamed, sections, "globalValues", "id")) yield return ("globalValues", set, "reads its values");
        foreach (var connection in Named(texts, ConnectionsNamed, sections, "connections", "name")) yield return ("connections", connection, "signs in with it");
    }

    /// <summary>The items of a kind that <paramref name="texts"/> name the way <paramref name="pattern"/> finds, as prod's setup calls them.</summary>
    static IEnumerable<string> Named(List<string> texts, Regex pattern, Dictionary<string, List<JsonObject>> sections, string section, string field)
    {
        var known = (sections.GetValueOrDefault(section) ?? []).Select(x => (string)x[field]).Where(x => x != null).ToList();
        return texts.SelectMany(t => pattern.Matches(t)).Select(m => m.Groups[1].Value).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(found => known.FirstOrDefault(k => string.Equals(k, found, StringComparison.OrdinalIgnoreCase))).Where(k => k != null);
    }

    static IEnumerable<string> AlertChannels(JsonNode groups) =>
        (groups as JsonArray ?? []).OfType<JsonObject>().Select(g => (string)g["alertChannel"]).Where(c => !string.IsNullOrEmpty(c));

    /// <summary>
    /// Each item a bring touches, and what the planner is to make of it: prod's, as the copy fakes it, for
    /// what staging doesn't have; staging's own with the partners and routes it gets, for a gateway it has.
    /// A gateway leads only to what staging will have. A subscription that waits for an adapter waits, and
    /// so does what needs something that can't come, saying for what.
    /// </summary>
    static (List<MirrorPiece> Pieces, Dictionary<string, List<JsonObject>> Wants) Pieces(Comparison comparison,
        Dictionary<(string Section, string Key), JsonObject> index, Dictionary<(string Section, string Key), string> needs)
    {
        var staging = comparison.Staging;
        var sections = comparison.Copy.Sections;
        var rows = comparison.Parity.Kinds.SelectMany(k => k.Items.Select(i => (k.Section, Item: i)))
            .GroupBy(x => (x.Section, x.Item.Key)).ToDictionary(g => g.Key, g => g.First().Item);

        var waits = needs.Keys.Where(n => !staging.Has(n.Section, n.Key) && rows.GetValueOrDefault(n)?.State == "waiting")
            .ToDictionary(n => n, n => rows[n].Reason);
        bool WillHave((string Section, string Key) n) => staging.Has(n.Section, n.Key) || (needs.ContainsKey(n) && !waits.ContainsKey(n));
        for (var changed = true; changed;)
        {
            changed = false;
            foreach (var n in needs.Keys.Where(n => !staging.Has(n.Section, n.Key) && !waits.ContainsKey(n)).ToList())
            {
                var blocked = Names(sections, n.Section, index[n]).Select(m => (m.Section, m.Key)).Where(m => !WillHave(m)).ToList();
                if (blocked.Count == 0) continue;
                waits[n] = $"It needs the {Kinds.Of(blocked[0].Section).Noun} “{blocked[0].Key}”, which can't come yet.";
                changed = true;
            }
        }

        var pieces = new List<MirrorPiece>();
        var wants = new Dictionary<string, List<JsonObject>>(StringComparer.Ordinal);
        var order = Kinds.All.Select(k => k.Section).ToList();
        foreach (var ((section, key), why) in needs.OrderBy(n => order.IndexOf(n.Key.Section)).ThenBy(n => n.Key.Key, StringComparer.Ordinal))
        {
            var noun = Kinds.Of(section).Noun;
            var row = rows.GetValueOrDefault((section, key));
            var item = index[(section, key)];
            var links = LinksOf(section, item).Where(l => WillHave(("subscriptions", l.Subscription)) && (l.Partner == null || WillHave(("partners", l.Partner)))).ToList();
            if (!staging.Has(section, key))
            {
                if (waits.TryGetValue((section, key), out var reason))
                {
                    pieces.Add(new MirrorPiece(section, noun, key, "waits", why, reason, Type: row?.Type));
                    continue;
                }
                var want = (JsonObject)item.DeepClone();
                if (section is "apiGateways" or "busGateways")
                    want[section == "apiGateways" ? "partners" : "routes"] = new JsonArray(LinksIn(section, item, links).ToArray());
                Add(wants, section, want);
                pieces.Add(new MirrorPiece(section, noun, key, "new", why, Type: row?.Type));
                continue;
            }

            var have = staging.Current[section].First(e => e.Key == key);
            var added = section switch
            {
                "apiGateways" => links.Where(l => needs.ContainsKey(("subscriptions", l.Subscription)) &&
                    !LinksOf(section, have.File).Any(h => h.Partner == l.Partner)).ToList(),
                "busGateways" => links.Where(l => needs.ContainsKey(("subscriptions", l.Subscription)) && !LinksOf(section, have.File).Contains(l)).ToList(),
                _ => [],
            };
            if (added.Count > 0)
            {
                var want = (JsonObject)have.File.DeepClone();
                var field = section == "apiGateways" ? "partners" : "routes";
                want[field] = new JsonArray([.. (have.File[field] as JsonArray ?? []).Select(x => x?.DeepClone()), .. LinksIn(section, item, added)]);
                Add(wants, section, want);
                pieces.Add(new MirrorPiece(section, noun, key, "adds", why, section == "apiGateways"
                    ? $"Staging has it, and gets {string.Join(", ", added.Select(l => $"the partner “{l.Partner}” for “{l.Subscription}”"))}."
                    : $"Staging has it, and gets the route{(added.Count == 1 ? "" : "s")} to {string.Join(", ", added.Select(l => $"“{l.Subscription}”"))}.",
                    row?.StagingId, row?.Type));
                continue;
            }
            pieces.Add(new MirrorPiece(section, noun, key, "kept", why,
                row?.State == "differs" ? "Staging's differs from prod's, and stays as it is." : null, row?.StagingId, row?.Type));
        }
        return (pieces, wants);
    }

    static void Add(Dictionary<string, List<JsonObject>> wants, string section, JsonObject want)
    {
        if (!wants.TryGetValue(section, out var list)) wants[section] = list = [];
        list.Add(want);
    }

    /// <summary>A gateway's partners or routes as prod has them, those of <paramref name="links"/> only.</summary>
    static IEnumerable<JsonNode> LinksIn(string section, JsonObject item, IReadOnlyCollection<(string Subscription, string Partner)> links) =>
        (item[section == "apiGateways" ? "partners" : "routes"] as JsonArray ?? []).OfType<JsonObject>()
        .Where(l => links.Contains(((string)l["subscription"], (string)l["partner"])))
        .Select(l => l.DeepClone());
}
