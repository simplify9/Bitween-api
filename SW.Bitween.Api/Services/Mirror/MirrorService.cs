using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Cli;
using SW.Bitween.Cli.Configuration;
using SW.PrimitiveTypes;

namespace SW.Bitween.Services.Mirror;

/// <summary>A prod the mirror can't copy, and why, in words for the page.</summary>
public class MirrorRefusal(string message) : Exception(message);

public record MirrorSide(string Url, string Version, long? Exchanges);

/// <summary>
/// One of prod's items and how staging's compares, with where each is so the page can open it: the
/// id on staging and on prod, and a subscription's type, which decides its page. A SQL statement opens
/// on its data source's page, so its ids are its data source's.
/// </summary>
/// <param name="State">inSync, differs, missing, waiting (for an adapter staging lacks) or extra (only on staging).</param>
/// <param name="Refused">The last copy tried it and Bitween turned it down: copying again won't change that.</param>
public record MirrorItem(string Key, string State, IReadOnlyList<string> Fields, string Reason = null, bool Refused = false,
    string StagingId = null, string ProdId = null, string Type = null);

public record MirrorKind(string Section, string Noun, string Plural, int InSync, int Differs, int Missing, int Extra, string Skipped, IReadOnlyList<MirrorItem> Items);

/// <summary>One of prod's adapters and what staging has of it.</summary>
public record MirrorAdapter(string Id, string Source, string ProdVersion, string StagingVersion, string State, int UsedBy);

/// <summary>A field prod sends on items of one kind that this release doesn't know: from a newer or a changed build. Not copied.</summary>
public record MirrorUnknownField(string Section, string Plural, string Field, int Items, IReadOnlyList<string> Examples);

/// <summary>
/// One line on the page, in one of four groups: needsYou (only a person can do it), copy (copying
/// does it), notCovered (what the test can't include, and why) and onPurpose (how staging differs from
/// prod by design). Section and Key name the item it is about; Action, the page where it is done.
/// </summary>
/// <param name="Field">For a fill-in: the field to fill in, as its page names it.</param>
/// <param name="Noun">For a fill-in: what kind of item it is in, in words: notification channel, say.</param>
public record MirrorNote(string Id, string Group, string Text, string Section = null, string Key = null, string Action = null,
    string StagingId = null, string ProdId = null, string Type = null, string Field = null, string Noun = null);

/// <summary>The page's answer: whether staging is ready to test on, and the counts behind it.</summary>
/// <param name="Matching">Prod's items staging has as prod has them.</param>
/// <param name="Total">Prod's items, of the kinds prod showed.</param>
public record MirrorVerdict(bool Ready, int Matching, int Total, int NeedsYou, int ToCopy, int NotCovered);

/// <summary>How close staging is to prod, and what is left to do: what the page shows.</summary>
public record MirrorParity(
    DateTimeOffset CheckedOn,
    MirrorSide Prod,
    MirrorSide Staging,
    MirrorVerdict Verdict,
    IReadOnlyList<MirrorNote> Notes,
    IReadOnlyList<MirrorKind> Kinds,
    IReadOnlyList<MirrorAdapter> Adapters,
    IReadOnlyList<MirrorFakedKey> FakedKeys,
    IReadOnlyList<MirrorUnknownField> Unknown);

public record MirrorFailure(string Section, string Noun, string Key, string Error);

/// <summary>
/// Copies prod's setup onto this staging, by name, through prod's own API and read-only: nothing on
/// prod changes and nothing needs installing there. Values are faked the moment they arrive (see
/// <see cref="MirrorSetup"/>), so this Bitween only ever stores stand-ins. Legacy setups are recreated
/// as prod has them (see <see cref="MirrorCopyGate"/>). Works with a prod on r10.0 or later; anything
/// older is refused with the reason.
/// </summary>
public sealed partial class MirrorService(BitweenOptions options, FileLinks links, IHttpContextAccessor httpContextAccessor, IConfiguration configuration,
    NativeAdapterDiscoveryService nativeAdapters, ICloudFilesService cloudFiles, IServiceScopeFactory scopes)
{
    /// <summary>The configuration name staging's copy is recorded under, so a later copy removes what prod no longer has.</summary>
    public const string ConfigurationName = "prod-mirror";

    internal const string NotTarget =
        "This Bitween isn't marked as a mirror, so nothing of prod's is copied onto it. Set Bitween__Mirror__Target=true in its deployment.";

    static readonly string[] AdapterPrefixes = ["receivers", "validators", "mappers", "handlers"];

    /// <summary>
    /// What Bitween said of each item the last copy couldn't make, by kind and name, shown until a copy
    /// makes it. One staging runs one node, and a restart forgets them until the next copy.
    /// </summary>
    static readonly System.Collections.Concurrent.ConcurrentDictionary<(string Section, string Key), string> LastFailures = new();

    /// <summary>
    /// A comparison, prod's setup as staging copies it, and the two connections its planner carries out
    /// changes over until it is done with; with the file the planner plans from and staging as it read it.
    /// </summary>
    sealed record Comparison(MirrorParity Parity, Planner Planner, Plan Plan, MirrorSetupCopy Copy, BitweenApi Prod, BitweenApi Self,
        ConfigFile Config, Context Staging, IReadOnlyList<MirrorBlank> Open) : IDisposable
    {
        public void Dispose()
        {
            Prod.Dispose();
            Self.Dispose();
        }
    }

    async Task<Comparison> CompareAsync(bool removeExtras, string copyKey, string selfToken = null)
    {
        var prod = Connect();
        BitweenApi self = null;
        try
        {
            return await CompareAsync(prod, () => self = SelfApi(copyKey, selfToken), removeExtras);
        }
        catch
        {
            prod.Dispose();
            self?.Dispose();
            throw;
        }
    }

    async Task<Comparison> CompareAsync(BitweenApi prod, Func<BitweenApi> openSelf, bool removeExtras)
    {
        var prodVersion = await VersionAsync(prod);

        // ---- prod, read and faked
        var prodCtx = new Context(prod);
        var renamed = new List<string>();
        var skipped = await ReadAsync(prodCtx, renamed);
        var (copy, _) = CopyOf(prodCtx);
        var used = AdapterUses(copy.Sections);
        var prodAdapters = await AdaptersAsync(prod, used.Keys, isProd: true);

        // ---- staging, as it is
        var self = openSelf();
        var selfCtx = new Context(self);
        var stagingAdapters = await AdaptersAsync(self, used.Keys);
        var adapters = CompareAdapters(prodAdapters, stagingAdapters, used, NativeIds());

        var config = new ConfigFile();
        config.Rename(ConfigurationName);
        var planner = new Planner(selfCtx, config);
        await planner.ReadAsync();
        var open = TakeStagingsOwn(copy, selfCtx, await ReadStandInsAsync());

        // A subscription staging doesn't have yet, whose adapter it lacks and can't get from the
        // marketplace, would only fail: it waits, and says for which adapter. One staging has already
        // is copied over as usual, so a missing adapter never removes it.
        var waiting = Waiting(copy.Sections, adapters, stagingAdapters)
            .Where(w => !selfCtx.Has("subscriptions", w.Key)).ToDictionary(w => w.Key, w => w.Value, StringComparer.Ordinal);
        foreach (var (section, items) in copy.Sections)
            config.Sections[section] = items.Where(i => section != "subscriptions" || !waiting.ContainsKey(Kinds.Of(section).KeyOf(i))).ToList();
        config.Sections[Kinds.Adapters] = adapters.Where(a => a.State == "missing" && a.Source == "marketplace" && a.ProdVersion != null)
            .Select(a => new JsonObject { ["install"] = a.Id, ["version"] = a.ProdVersion, ["current"] = true }).ToList();

        var plan = await planner.PlanAsync(prune: true);

        var extras = Extras(selfCtx, copy.Sections, plan, skipped);
        if (removeExtras)
            plan.Changes.AddRange(extras.OrderByDescending(e => IndexOf(e.Section))
                .Select(e => new Change(Op.Delete, e.Section, e.Item.Key, Have: e.Item)));

        var kinds = Kinds.All.Select(kind => KindRow(kind, copy.Sections.GetValueOrDefault(kind.Section) ?? [], plan, extras, waiting, skipped,
            prodCtx, selfCtx)).ToList();
        var unknown = Unknown(prodCtx, selfCtx, skipped);
        var notes = Notes(kinds, adapters, plan, extras.Count, renamed, unknown, copy);
        var parity = new MirrorParity(
            DateTimeOffset.UtcNow,
            new MirrorSide(prod.Url, prodVersion, await ExchangesAsync(prod)),
            new MirrorSide(links.BaseUrl, BitweenInfo.Version(options).ToString(3), await ExchangesAsync(self)),
            Verdict(kinds, notes),
            notes, kinds, adapters, copy.FakedKeys, unknown);
        return new Comparison(parity, planner, plan, copy, prod, self, config, selfCtx, open);
    }

    /// <summary>
    /// Every address and secret the copy didn't bring takes staging's own where staging has one, so
    /// staging's never reads as a difference from prod's. Those still empty or a placeholder are left to do,
    /// as is a secret a bring saved as a placeholder (<paramref name="standIns"/>, see <see cref="StandInId"/>),
    /// which reads back hidden like a filled one.
    /// </summary>
    internal static List<MirrorBlank> TakeStagingsOwn(MirrorSetupCopy copy, Context staging, ISet<string> standIns)
    {
        var open = new List<MirrorBlank>();
        foreach (var blank in copy.Blanks)
        {
            var mine = staging.Current.GetValueOrDefault(blank.Section)?.FirstOrDefault(e => e.Key == blank.Key)?.File;
            var item = copy.Sections.GetValueOrDefault(blank.Section)?.FirstOrDefault(i => Kinds.Of(blank.Section).KeyOf(i) == blank.Key);
            if (At(mine, blank.Path) is JsonValue v && v.TryGetValue<string>(out var value) && !MirrorFaker.IsPlaceholder(value)
                && At(item, blank.Path.Take(blank.Path.Count - 1)) is JsonObject owner)
            {
                owner[blank.Path[^1]] = value;
                if (standIns.Contains(StandInId(blank))) open.Add(blank with { StoodIn = true });
            }
            else
                open.Add(blank);
        }
        return open;
    }

    static JsonNode At(JsonNode node, IEnumerable<string> path)
    {
        foreach (var name in path) node = (node as JsonObject)?[name];
        return node;
    }

    // ------------------------------------------------------------------ prod

    BitweenApi Connect()
    {
        var m = options.Mirror;
        if (string.IsNullOrWhiteSpace(m.SourceUrl))
            throw new MirrorRefusal("Prod's address isn't set yet.");
        if (!Uri.TryCreate(m.SourceUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
            throw new MirrorRefusal($"{m.SourceUrl} isn't an address Bitween can call. Use one like https://bitween.acme.com.");
        if (SameInstance(uri))
            throw new MirrorRefusal("That address is this Bitween. Set prod's address, not staging's.");

        // Prod is only ever read: a call that would change anything there is refused before it leaves.
        var handler = new ReadOnly(new HttpClientHandler { UseCookies = false });
        if (!string.IsNullOrWhiteSpace(m.SourceToken))
            return new BitweenApi(m.SourceUrl, m.SourceToken.Trim(), insecure: false, handler);
        if (!string.IsNullOrWhiteSpace(m.SourceEmail) && !string.IsNullOrEmpty(m.SourcePassword))
            return new BitweenApi(m.SourceUrl, m.SourceEmail.Trim(), m.SourcePassword, insecure: false, handler);
        throw new MirrorRefusal("Set how staging signs in to prod: a service account token, or a Viewer member's email and password.");
    }

    bool SameInstance(Uri prod)
    {
        foreach (var own in new[] { links.BaseUrl, links.InstanceUrl, options.PublicUrl })
            if (Uri.TryCreate(own, UriKind.Absolute, out var o) &&
                string.Equals(o.Host, prod.Host, StringComparison.OrdinalIgnoreCase) && o.Port == prod.Port)
                return true;
        return false;
    }

    /// <summary>
    /// Prod's release. r10.0 answered 1.0.0.0, as every build before it did, so 1.0.0.0 is told apart
    /// from r8 by what only r10 has; Infolink (r6 and before) names its version differently.
    /// </summary>
    static async Task<string> VersionAsync(BitweenApi prod)
    {
        JsonNode answer;
        try { answer = await prod.GetAsync("api/settings/myversion"); }
        catch (BitweenApiException ex) when (ex.Status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new MirrorRefusal($"Prod refused the sign-in: {ex.Message}");
        }
        catch (Exception ex) when (ex is BitweenApiException or HttpRequestException or TaskCanceledException)
        {
            throw new MirrorRefusal($"Prod didn't answer: {ex.Message}");
        }

        if (answer?["infolinkApiVersion"] != null)
            throw new MirrorRefusal("Prod runs Infolink (r6 or older). The mirror copies prods on r10.0 or later; move this client to r10 first.");
        var version = VersionIn(answer?["bitweenApiVersion"]);
        if (version is { Major: >= 10 }) return version.ToString(3);

        try { await prod.GetAsync("api/permissions"); }
        catch (BitweenApiException ex) when (ex.Status == HttpStatusCode.NotFound)
        {
            throw new MirrorRefusal("Prod runs r8 or older. The mirror copies prods on r10.0 or later; move this client to r10 first.");
        }
        return "r10.0";
    }

    static Version VersionIn(JsonNode node) => node switch
    {
        JsonValue v when v.TryGetValue<string>(out var s) && Version.TryParse(s, out var parsed) => parsed,
        JsonObject o when (int?)o["major"] is { } major => new Version(major, (int?)o["minor"] ?? 0, Math.Max(0, (int?)o["build"] ?? 0)),
        _ => null,
    };

    /// <summary>
    /// What prod's API last answered for each kind, by its address, with the kinds it didn't and why. Its
    /// setup is a request an item - nearly 200 on Traxis's - and the picker, its preview, the bring and each
    /// test's take all want it within minutes, against the 600 a minute prod allows staging's login.
    /// </summary>
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string,
        (DateTimeOffset At, Dictionary<string, List<(string Id, JsonObject Api)>> Raw, Dictionary<string, string> Skipped)> ProdSetups =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Every kind prod has, each read on its own, read again once five minutes old: a kind prod's release
    /// doesn't have, or the login can't read, is left out and says why, rather than stopping the rest. A copy
    /// each time, since a comparison renames in it.
    /// </summary>
    static async Task<(Dictionary<string, List<(string Id, JsonObject Api)>> Raw, Dictionary<string, string> Skipped)> AnsweredAsync(Context ctx)
    {
        if (!ProdSetups.TryGetValue(ctx.Api.Url, out var read) || DateTimeOffset.UtcNow - read.At >= TimeSpan.FromMinutes(5))
        {
            var skipped = new Dictionary<string, string>(StringComparer.Ordinal);
            var raw = new Dictionary<string, List<(string Id, JsonObject Api)>>(StringComparer.Ordinal);
            foreach (var kind in Kinds.All)
            {
                try { raw[kind.Section] = await kind.ReadAsync(ctx); }
                catch (BitweenApiException ex) when (ex.Status == HttpStatusCode.NotFound)
                {
                    skipped[kind.Section] = NotOnProdsRelease;
                    raw[kind.Section] = [];
                }
                catch (BitweenApiException ex) when (ex.Status == HttpStatusCode.Forbidden)
                {
                    skipped[kind.Section] = "Staging's login on prod can't read these.";
                    raw[kind.Section] = [];
                }
            }
            ProdSetups[ctx.Api.Url] = read = (DateTimeOffset.UtcNow, raw, skipped);
        }
        return (read.Raw.ToDictionary(k => k.Key, k => k.Value.Select(x => (x.Id, (JsonObject)x.Api.DeepClone())).ToList(), StringComparer.Ordinal),
            new Dictionary<string, string>(read.Skipped, StringComparer.Ordinal));
    }

    /// <summary>
    /// Every kind prod has, as the copy reads it (see <see cref="AnsweredAsync"/>): several of one name are
    /// told apart, and a kind in a form this release doesn't read is left out and says why.
    /// </summary>
    static async Task<Dictionary<string, string>> ReadAsync(Context ctx, List<string> renamed)
    {
        var (raw, skipped) = await AnsweredAsync(ctx);
        foreach (var kind in Kinds.All.Where(k => !skipped.ContainsKey(k.Section)))
        {
            renamed.AddRange(Distinguish(ctx, kind, raw[kind.Section]));
            foreach (var (id, api) in raw[kind.Section]) ctx.Know(kind.Section, kind.KeyOfApi(ctx, api), id);
        }
        foreach (var kind in Kinds.All)
        {
            try { ctx.Current[kind.Section] = raw[kind.Section].Select(x => kind.ToExisting(ctx, x.Id, x.Api)).ToList(); }
            catch (Exception ex) when (ex is InvalidOperationException or FormatException or ArgumentException)
            {
                // A shape this release didn't expect: the rest still copies.
                skipped[kind.Section] = $"Prod answered in a form this release doesn't read ({ex.Message}).";
                ctx.Current[kind.Section] = [];
            }
        }
        return skipped;
    }

    const string NotOnProdsRelease = "Prod's release doesn't have these.";

    /// <summary>
    /// Prod can hold several of a kind under one name - five subscriptions called "test" - and a copy
    /// goes by name, so each after the oldest is named apart: "test (2)", "test (3)". Every reference
    /// to one still names the right one, since references are read by id. Says what it renamed.
    /// </summary>
    internal static IEnumerable<string> Distinguish(Context ctx, Kind kind, List<(string Id, JsonObject Api)> items)
    {
        var field = kind.Section == "dataSourceStatements" ? "name" : kind.Fields.First(f => f.File == kind.KeyField).Api;
        var taken = items.Select(i => kind.KeyOfApi(ctx, i.Api)).Where(k => k != null).ToHashSet(StringComparer.Ordinal);
        var notes = new List<string>();
        foreach (var group in items.GroupBy(i => kind.KeyOfApi(ctx, i.Api), StringComparer.Ordinal).Where(g => g.Key != null && g.Count() > 1))
        {
            var ordered = group.OrderBy(i => long.TryParse(i.Id, out var n) ? n : long.MaxValue).ThenBy(i => i.Id, StringComparer.Ordinal).ToList();
            var names = new List<string>();
            foreach (var (_, api) in ordered.Skip(1))
            {
                var name = (string)api[field];
                var n = 2;
                string unique;
                do
                {
                    unique = $"{name} ({n++})";
                    api[field] = unique;
                } while (!taken.Add(kind.KeyOfApi(ctx, api)));
                names.Add(unique);
            }
            notes.Add($"Prod has {group.Count()} {Plural(kind.Noun)} named “{group.Key}”. On staging the oldest keeps the name and the others are {string.Join(", ", names.Select(x => $"“{x}”"))}.");
        }
        return notes;
    }

    // ------------------------------------------------------------------ adapters

    sealed record AdapterInfo(string Id, bool Native, string Current, IReadOnlyCollection<string> Versions, bool FromRegistry);

    /// <summary>
    /// Prod's listed adapters as last read, by its address. A prod whose release can't list them without
    /// starting each published one to describe it takes half a minute and more, every picker and
    /// comparison asks, and what prod has installed seldom changes.
    /// </summary>
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTimeOffset At, Dictionary<string, AdapterInfo> Listed)> ProdAdapters =
        new(StringComparer.Ordinal);

    /// <summary>
    /// The adapters a Bitween lists, and for any one its items use that the lists leave out - a
    /// database's, say - what versions it has of it. Prod's lists are read again once ten minutes old;
    /// staging's every time, since a copy installs adapters.
    /// </summary>
    static async Task<Dictionary<string, AdapterInfo>> AdaptersAsync(BitweenApi api, IEnumerable<string> used, bool isProd = false)
    {
        Dictionary<string, AdapterInfo> listed;
        if (isProd && ProdAdapters.TryGetValue(api.Url, out var read) && DateTimeOffset.UtcNow - read.At < TimeSpan.FromMinutes(10))
            listed = read.Listed;
        else
        {
            listed = new Dictionary<string, AdapterInfo>(StringComparer.OrdinalIgnoreCase);
            async Task<JsonNode> ListOf(string prefix)
            {
                // Without their startup properties, which nothing here reads: describing a published
                // adapter starts it. A release from before the option ignores it and describes them.
                try { return await api.GetAsync($"api/adapters/catalog?prefix={prefix}&startupValues=false"); }
                catch (BitweenApiException ex) when (ex.Status is HttpStatusCode.NotFound or HttpStatusCode.Forbidden) { return null; }
            }
            // Staging's kinds side by side, each a read of its storage; prod's one after another, to go easy on it.
            var answers = new List<JsonNode>();
            if (isProd) foreach (var prefix in AdapterPrefixes) answers.Add(await ListOf(prefix));
            else answers.AddRange(await Task.WhenAll(AdapterPrefixes.Select(ListOf)));
            foreach (var answer in answers)
            {
                foreach (var row in (answer as JsonArray ?? []).OfType<JsonObject>())
                {
                    var id = (string)row["key"];
                    if (string.IsNullOrEmpty(id)) continue;
                    var history = (row["versionHistory"] as JsonArray ?? []).OfType<JsonObject>().ToList();
                    listed[id] = new AdapterInfo(id, (bool?)row["native"] ?? false, (string)row["currentVersion"],
                        (row["versions"] as JsonArray ?? []).Select(v => v?.ToString()).Where(v => v != null).ToList(),
                        history.Any(h => ((string)h["publishedBy"])?.StartsWith("registry:", StringComparison.Ordinal) == true));
                }
            }
            if (isProd) ProdAdapters[api.Url] = (DateTimeOffset.UtcNow, listed);
        }
        var found = new Dictionary<string, AdapterInfo>(listed, StringComparer.OrdinalIgnoreCase);
        foreach (var id in used.Where(id => !found.ContainsKey(id) && !NativeAdapterDiscoveryService.IsNative(id)))
        {
            JsonNode listing;
            try { listing = await api.GetAsync($"api/adapters/versions?adapterId={Uri.EscapeDataString(id)}"); }
            catch (BitweenApiException ex) when (ex.Status is HttpStatusCode.NotFound or HttpStatusCode.Forbidden or HttpStatusCode.BadRequest) { continue; }
            var versions = (listing?["versions"] as JsonArray ?? []).OfType<JsonObject>().ToList();
            if (versions.Count == 0 && listing?["current"] == null) continue;
            found[id] = new AdapterInfo(id, false, (string)listing?["current"], versions.Select(v => (string)v["version"]).Where(v => v != null).ToList(),
                versions.Any(v => ((string)v["publishedBy"])?.StartsWith("registry:", StringComparison.Ordinal) == true));
        }
        return found;
    }

    /// <summary>Every adapter built into this release, retiring ones included: listings leave out a retiring one nothing uses yet.</summary>
    HashSet<string> NativeIds() =>
        AdapterPrefixes.SelectMany(p => nativeAdapters.GetNativeAdapters(p)).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Simplify9 publishes its database and broker adapters on the marketplace, so one missing on staging
    /// is installed from there, whatever prod got it from.
    /// </summary>
    static bool PublishedBySimplify9(string id) =>
        id.StartsWith("bitween.db.", StringComparison.OrdinalIgnoreCase) || id.StartsWith("bitween.bus.", StringComparison.OrdinalIgnoreCase);

    static List<MirrorAdapter> CompareAdapters(Dictionary<string, AdapterInfo> prod, Dictionary<string, AdapterInfo> staging,
        Dictionary<string, int> uses, HashSet<string> builtIn)
    {
        var rows = new List<MirrorAdapter>();
        foreach (var id in prod.Keys.Union(uses.Keys, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        {
            prod.TryGetValue(id, out var p);
            staging.TryGetValue(id, out var s);
            var used = uses.GetValueOrDefault(id);
            if (p?.Native == true || NativeAdapterDiscoveryService.IsNative(id))
            {
                // Built in: it comes with the release. One prod uses that this release lacks is a finding.
                if (!builtIn.Contains(id) && used > 0) rows.Add(new MirrorAdapter(id, "builtin", "built in", null, "missing", used));
                continue;
            }
            var source = p?.FromRegistry == true || PublishedBySimplify9(id) ? "marketplace" : "custom";
            var want = p?.Current ?? p?.Versions.LastOrDefault();
            var state = s == null || (want != null && !s.Versions.Contains(want)) ? "missing"
                : want != null && s.Current != want ? "different"
                : "same";
            rows.Add(new MirrorAdapter(id, source, want, s?.Current, state, used));
        }
        return rows;
    }

    /// <summary>How many of prod's items use each adapter: subscriptions by role, channels, notifiers, data sources.</summary>
    static Dictionary<string, int> AdapterUses(Dictionary<string, List<JsonObject>> sections)
    {
        var uses = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        void Add(string id) { if (!string.IsNullOrEmpty(id)) uses[id] = uses.GetValueOrDefault(id) + 1; }
        foreach (var s in sections.GetValueOrDefault("subscriptions") ?? [])
        foreach (var role in new[] { "receiver", "validator", "mapper", "handler" })
            Add((string)s[role]?["adapter"]);
        foreach (var c in (sections.GetValueOrDefault("notificationChannels") ?? []).Concat(sections.GetValueOrDefault("notifiers") ?? []))
            Add((string)c["handler"]);
        foreach (var d in sections.GetValueOrDefault("dataSources") ?? []) Add((string)d["adapter"]);
        return uses;
    }

    /// <summary>
    /// Subscriptions that name an adapter staging lacks, or a version of one it lacks, by name, with what
    /// they wait for. One the copy will install from the marketplace at a known version doesn't wait.
    /// </summary>
    static Dictionary<string, string> Waiting(Dictionary<string, List<JsonObject>> sections, List<MirrorAdapter> adapters,
        Dictionary<string, AdapterInfo> staging)
    {
        var missing = adapters.Where(a => a.State == "missing" && !(a.Source == "marketplace" && a.ProdVersion != null))
            .ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase);
        var waiting = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var s in sections.GetValueOrDefault("subscriptions") ?? [])
        foreach (var role in new[] { "receiver", "validator", "mapper", "handler" })
        {
            if ((string)s[role]?["adapter"] is not { } id) continue;
            var pinned = (string)s[role]?["version"];
            if (missing.TryGetValue(id, out var a))
                waiting[(string)s["name"]] = $"Waits for the adapter {a.Id}{((pinned ?? a.ProdVersion) is { } v ? " " + v : "")} on staging.";
            else if (pinned != null && staging.TryGetValue(id, out var has) && !has.Native && !has.Versions.Contains(pinned))
                waiting[(string)s["name"]] = $"Waits for version {pinned} of the adapter {id} on staging.";
        }
        return waiting;
    }

    // ------------------------------------------------------------------ comparing

    /// <summary>
    /// What staging has that prod doesn't: removed on a copy only when asked. A kind prod didn't show -
    /// its release lacks it, or the login can't read it - says nothing about what prod has, so none of
    /// staging's are counted.
    /// </summary>
    static List<(string Section, Existing Item)> Extras(Context self, Dictionary<string, List<JsonObject>> sections, Plan plan,
        Dictionary<string, string> skipped)
    {
        var pruned = plan.Changes.Where(c => c.Op == Op.Delete).Select(c => (c.Section, c.Key)).ToHashSet();
        var extras = new List<(string, Existing)>();
        foreach (var kind in Kinds.All.Where(k => !skipped.ContainsKey(k.Section)))
        {
            var prodKeys = (sections.GetValueOrDefault(kind.Section) ?? []).Select(kind.KeyOf).ToHashSet(StringComparer.Ordinal);
            extras.AddRange((self.Current.GetValueOrDefault(kind.Section) ?? [])
                .Where(e => e.Key != null && !prodKeys.Contains(e.Key) && !pruned.Contains((kind.Section, e.Key)))
                .Select(e => (kind.Section, e)));
        }
        return extras;
    }

    static MirrorKind KindRow(Kind kind, List<JsonObject> prodItems, Plan plan, List<(string Section, Existing Item)> extras,
        Dictionary<string, string> waiting, Dictionary<string, string> skipped, Context prod, Context self)
    {
        var changes = plan.Changes.Where(c => c.Section == kind.Section).ToList();
        var prodIds = Ids(prod, kind.Section);
        var stagingIds = Ids(self, kind.Section);
        MirrorItem Located(MirrorItem item, string type = null) =>
            item with { ProdId = prodIds.GetValueOrDefault(item.Key), StagingId = stagingIds.GetValueOrDefault(item.Key), Type = type };

        var items = new List<MirrorItem>();
        foreach (var item in prodItems)
        {
            var key = kind.KeyOf(item);
            var type = kind.Section == "subscriptions" ? (string)item["type"] : null;
            var change = changes.FirstOrDefault(c => c.Key == key && c.Op != Op.Delete);
            var refused = change != null && LastFailures.TryGetValue((kind.Section, key), out var error) ? error : null;
            items.Add(Located(waiting.TryGetValue(key, out var reason) && kind.Section == "subscriptions"
                ? new MirrorItem(key, "waiting", [], reason)
                : change == null ? new MirrorItem(key, "inSync", [])
                : change.Op == Op.Create ? new MirrorItem(key, "missing", [], refused == null ? null : $"Staging couldn't create it. Bitween said: {refused}", refused != null)
                : new MirrorItem(key, "differs", change.Fields ?? [], refused == null ? null : $"Staging couldn't update it. Bitween said: {refused}", refused != null),
                type));
        }
        var extraKeys = extras.Where(e => e.Section == kind.Section).Select(e => e.Item.Key).ToHashSet(StringComparer.Ordinal);
        items.AddRange(changes.Where(c => c.Op == Op.Delete && !extraKeys.Contains(c.Key))
            .Select(c => Located(new MirrorItem(c.Key, "extra", [], "Prod no longer has it; copying removes it."))));
        items.AddRange(extraKeys.Order(StringComparer.Ordinal).Select(k => Located(new MirrorItem(k, "extra", [], "Only on staging."))));
        return new MirrorKind(kind.Section, kind.Noun, Plural(kind.Noun),
            items.Count(i => i.State == "inSync"), items.Count(i => i.State == "differs"),
            items.Count(i => i.State is "missing" or "waiting"), items.Count(i => i.State == "extra"),
            skipped.GetValueOrDefault(kind.Section), items);
    }

    /// <summary>Each item's id by key; a SQL statement's is its data source's, the page it is on.</summary>
    static Dictionary<string, string> Ids(Context ctx, string section) =>
        (ctx.Current.GetValueOrDefault(section) ?? []).Where(e => e.Key != null)
        .GroupBy(e => e.Key, StringComparer.Ordinal)
        .ToDictionary(g => g.Key, g => section == "dataSourceStatements" ? g.First().Api["dataSourceId"]?.ToString() : g.First().Id, StringComparer.Ordinal);

    /// <summary>
    /// Fields prod sends on a kind's items, set on at least one, that this release never sends on the
    /// same kind: what a changed or newer build added. The copy can't carry them, so the page says so
    /// rather than dropping them unseen. Only kinds staging has items of can be compared.
    /// </summary>
    internal static List<MirrorUnknownField> Unknown(Context prod, Context self, Dictionary<string, string> skipped)
    {
        var found = new List<MirrorUnknownField>();
        foreach (var kind in Kinds.All.Where(k => !skipped.ContainsKey(k.Section)))
        {
            var known = (self.Current.GetValueOrDefault(kind.Section) ?? []).SelectMany(e => e.Api.Select(p => p.Key)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (known.Count == 0) continue;
            var set = (prod.Current.GetValueOrDefault(kind.Section) ?? [])
                .SelectMany(e => e.Api.Where(p => !known.Contains(p.Key) && IsSet(p.Value)).Select(p => (Field: p.Key, e.Key)))
                .GroupBy(x => x.Field, StringComparer.Ordinal);
            found.AddRange(set.Select(g => new MirrorUnknownField(kind.Section, Plural(kind.Noun), g.Key, g.Count(),
                g.Select(x => x.Key).Where(k => k != null).Order(StringComparer.Ordinal).Take(3).ToList())));
        }
        return found;

        static bool IsSet(JsonNode value) => !Values.Empty(value) && !(value is JsonValue v && v.TryGetValue<bool>(out var b) && !b);
    }

    /// <summary>"retry policy" as "retry policies", "address" as "addresses", "partner" as "partners".</summary>
    internal static string Plural(string noun) =>
        noun.Length > 1 && noun[^1] == 'y' && !"aeiou".Contains(noun[^2]) ? noun[..^1] + "ies"
        : noun.EndsWith('s') || noun.EndsWith('x') || noun.EndsWith("ch") || noun.EndsWith("sh") ? noun + "es"
        : noun + "s";

    /// <summary>"1 of prod's items uses", "3 of prod's items use".</summary>
    static string Used(int n) => $"{n} of prod's items use{(n == 1 ? "s" : "")}";

    static string Count(int n, string noun) => $"{n} {(n == 1 ? noun : Plural(noun))}";

    static int IndexOf(string section) => Kinds.All.ToList().FindIndex(k => k.Section == section);

    // ------------------------------------------------------------------ what the page says

    static readonly Regex NamesWhatFailed = new("^There's no (?<noun>[a-zA-Z ]+) \"(?<name>.+)\"\\.$", RegexOptions.Compiled);

    /// <summary>
    /// Every line the page shows, in its group. Each says only what is known: a refusal is what
    /// Bitween said when staging tried to create the item, never a forecast of what prod will do.
    /// </summary>
    static List<MirrorNote> Notes(List<MirrorKind> kinds, List<MirrorAdapter> adapters, Plan plan, int extras, List<string> renamed,
        List<MirrorUnknownField> unknown, MirrorSetupCopy copy)
    {
        var notes = AdapterNotes(adapters);

        // ---- needs you
        foreach (var k in kinds.Where(k => k.Skipped != null && k.Skipped != NotOnProdsRelease))
            notes.Add(new MirrorNote($"skipped:{k.Section}", "needsYou",
                $"{Capital(k.Plural)}: {k.Skipped} On prod, give its role permission to view {k.Plural}."));
        foreach (var problem in plan.Problems.Distinct())
            notes.Add(new MirrorNote($"problem:{problem}", "needsYou", $"On staging: {problem}"));

        // ---- copy does it
        var creates = kinds.Sum(k => k.Items.Count(i => i.State == "missing" && !i.Refused));
        var updates = kinds.Sum(k => k.Items.Count(i => i.State == "differs" && !i.Refused));
        var installs = plan.Changes.Count(c => c.Op is Op.Install or Op.Promote);
        var what = new[]
        {
            creates > 0 ? $"creates {Count(creates, "item")} staging doesn't have" : null,
            updates > 0 ? $"updates {Count(updates, "item")} that {(updates == 1 ? "differs" : "differ")}" : null,
            installs > 0 ? $"installs {Count(installs, "adapter")} from the marketplace" : null,
        }.Where(x => x != null).ToList();
        if (what.Count > 0) notes.Add(new MirrorNote("copy", "copy", $"Copying {string.Join(", ", what)}."));
        if (extras > 0)
            notes.Add(new MirrorNote("extras", "copy",
                $"Staging has {Count(extras, "item")} prod doesn't. Copying removes {(extras == 1 ? "it" : "them")} only when you tick Also remove."));

        // ---- not covered
        foreach (var (k, i) in kinds.SelectMany(k => k.Items.Select(i => (k, i))).Where(x => x.i.Refused))
        {
            var said = LastFailures.GetValueOrDefault((k.Section, i.Key)) ?? "";
            var named = NamesWhatFailed.Match(said);
            notes.Add(new MirrorNote($"refused:{k.Section}:{i.Key}", "notCovered", named.Success
                ? $"Staging couldn't create prod's {k.Noun} “{i.Key}”, because it uses the {named.Groups["noun"].Value} “{named.Groups["name"].Value}”, which staging couldn't create either."
                : $"Staging couldn't {(i.State == "missing" ? "create" : "update")} prod's {k.Noun} “{i.Key}”, so the test won't cover it. Bitween said: {said}",
                k.Section, i.Key));
        }
        foreach (var (k, i) in kinds.SelectMany(k => k.Items.Select(i => (k, i))).Where(x => x.i.State == "waiting"))
            notes.Add(new MirrorNote($"waiting:{k.Section}:{i.Key}", "notCovered",
                $"Prod's {k.Noun} “{i.Key}” isn't on staging yet. {i.Reason}", k.Section, i.Key));
        foreach (var u in unknown)
            notes.Add(new MirrorNote($"unknown:{u.Section}:{u.Field}", "notCovered",
                $"Prod's {u.Plural} carry “{u.Field}”, which this release doesn't know, so it isn't copied. It is set on {u.Items} of them" +
                $"{(u.Examples.Count > 0 ? ", such as " + string.Join(", ", u.Examples.Select(e => $"“{e}”")) : "")}."));

        // ---- different on purpose
        var subscriptions = copy.Sections.GetValueOrDefault("subscriptions")?.Count ?? 0;
        if (subscriptions > 0)
            notes.Add(new MirrorNote("paused", "onPurpose", $"{Capital(Count(subscriptions, "subscription"))} {(subscriptions == 1 ? "is" : "are")} paused on staging, so nothing runs on its own."));
        var dataSources = copy.Sections.GetValueOrDefault("dataSources")?.Count ?? 0;
        var busGateways = copy.Sections.GetValueOrDefault("busGateways")?.Count ?? 0;
        if (dataSources + busGateways > 0)
            notes.Add(new MirrorNote("switchedOff", "onPurpose",
                $"{Capital(string.Join(" and ", new[] { dataSources > 0 ? Count(dataSources, "data source") : null, busGateways > 0 ? Count(busGateways, "bus gateway") : null }.Where(x => x != null)))} {(dataSources + busGateways == 1 ? "is" : "are")} switched off on staging."));
        var fakedPartner = copy.FakedKeys.Count(f => !f.Kept && f.Id.StartsWith("partner:", StringComparison.Ordinal));
        var fakedGlobal = copy.FakedKeys.Count(f => !f.Kept && f.Id.StartsWith("global:", StringComparison.Ordinal));
        if (fakedPartner + fakedGlobal > 0)
            notes.Add(new MirrorNote("faked", "onPurpose",
                $"{Capital(string.Join(" and ", new[] { fakedPartner > 0 ? Count(fakedPartner, "partner property") : null, fakedGlobal > 0 ? Count(fakedGlobal, "global value") : null }.Where(x => x != null)))} " +
                $"hold{(fakedPartner + fakedGlobal == 1 ? "s" : "")} a fake of the same shape. Keep a real one only if it's a setting, such as a time zone.",
                Action: "faked"));
        if (copy.Blanks.Count > 0)
            notes.Add(new MirrorNote("blanks", "onPurpose",
                "Prod's addresses and secrets are never copied: staging uses its own, your test systems and logins."));
        if ((copy.Sections.GetValueOrDefault("partners")?.Count ?? 0) > 0)
            notes.Add(new MirrorNote("keys", "onPurpose", "Partners' API keys are staging's own."));
        notes.AddRange(renamed.Select(r => new MirrorNote($"renamed:{r}", "onPurpose", r)));
        return notes;
    }

    /// <summary>What only a person can do about prod's adapters on staging: upload one, make a version current, or know a built-in one is gone.</summary>
    static List<MirrorNote> AdapterNotes(IEnumerable<MirrorAdapter> adapters)
    {
        var notes = new List<MirrorNote>();
        foreach (var a in adapters.Where(a => a.State == "missing" && a.Source != "builtin" && !(a.Source == "marketplace" && a.ProdVersion != null)))
        {
            var where = a.Source == "marketplace"
                ? $"Install the adapter {a.Id} from the Marketplace tab of the Adapters page"
                : $"Upload the adapter {a.Id}{(a.ProdVersion == null ? "" : $", version {a.ProdVersion},")} on the Adapters page";
            notes.Add(new MirrorNote($"adapter:{a.Id}", "needsYou",
                $"{where}. {Used(a.UsedBy)} it, and {(a.UsedBy == 1 ? "it comes" : "they come")} to staging once it's there.",
                Action: a.Source == "marketplace" ? "adapters-marketplace" : "adapters-installed"));
        }
        foreach (var a in adapters.Where(a => a.State == "different"))
            notes.Add(new MirrorNote($"current:{a.Id}", "needsYou",
                $"Make version {a.ProdVersion} of the adapter {a.Id} the current one on staging, as on prod. Staging runs {a.StagingVersion}.",
                Action: "adapters-installed"));
        foreach (var a in adapters.Where(a => a.State == "missing" && a.Source == "builtin"))
            notes.Add(new MirrorNote($"builtin:{a.Id}", "needsYou",
                $"This release no longer has the built-in adapter {a.Id}. {Used(a.UsedBy)} it, and would stop working after the upgrade."));
        return notes;
    }

    static string Capital(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    static MirrorVerdict Verdict(List<MirrorKind> kinds, List<MirrorNote> notes)
    {
        var compared = kinds.Where(k => k.Skipped == null).ToList();
        var toCopy = compared.Sum(k => k.Items.Count(i => i.State is "missing" or "differs" && !i.Refused));
        var needsYou = notes.Count(n => n.Group == "needsYou");
        return new MirrorVerdict(needsYou == 0 && toCopy == 0,
            compared.Sum(k => k.InSync), compared.Sum(k => k.InSync + k.Differs + k.Missing),
            needsYou, toCopy, notes.Count(n => n.Group == "notCovered"));
    }

    static async Task<long?> ExchangesAsync(BitweenApi api)
    {
        try { return (long?)(await api.GetAsync("api/xchanges?page=0&size=1"))?["totalCount"]; }
        catch (BitweenApiException) { return null; }
    }

    // ------------------------------------------------------------------ this Bitween

    MirrorFaker Faker(IEnumerable<string> keep = null) => new(HKDF.DeriveKey(HashAlgorithmName.SHA256,
        Encoding.UTF8.GetBytes(configuration["Token:Key"] ?? string.Empty), 32, info: Encoding.UTF8.GetBytes("bitween-mirror-fakes")), keep);

    /// <summary>
    /// Prod's setup as staging gets it, and the faker that made it, in two passes: the first fakes it to
    /// find the words it names (see <see cref="MirrorSetup.Words"/>), the second keeps those words
    /// wherever prod's data has them. Exchanges' files are faked by the same faker, so they keep them too.
    /// </summary>
    (MirrorSetupCopy Copy, MirrorFaker Faker) CopyOf(Context prod)
    {
        var keep = KeepList();
        var faker = Faker(MirrorSetup.Words(MirrorSetup.Copy(prod.Current, Faker(), keep).Sections));
        return (MirrorSetup.Copy(prod.Current, faker, keep), faker);
    }

    HashSet<string> KeepList() =>
        (options.Mirror.KeepRealValues ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// This Bitween's own API, as the caller, the way the kit installer calls itself: where it listens.
    /// During a copy each call carries the copy's key, so saves know they recreate prod.
    /// </summary>
    BitweenApi SelfApi(string copyKey, string token = null)
    {
        var profile = new Profile { Url = SelfUrl, AccessToken = token ?? CallerToken() };
        return copyKey == null
            ? new BitweenApi(null, null, profile)
            : new BitweenApi(null, null, profile, new WithCopyKey(copyKey, new HttpClientHandler { UseCookies = false }));
    }

    string SelfUrl => string.IsNullOrWhiteSpace(links.InstanceUrl) ? links.BaseUrl : links.InstanceUrl;

    /// <summary>The token of whoever asked: staging reads its own setup as them.</summary>
    internal string CallerToken()
    {
        var header = httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();
        return header?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true
            ? header[7..].Trim()
            : throw new SWUnauthorizedException();
    }

    sealed class WithCopyKey(string key, HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.Add(MirrorCopyGate.Header, key);
            return base.SendAsync(request, cancellationToken);
        }
    }

    /// <summary>
    /// Lets through only what reads: GETs, and the two sign-ins a read needs. Anything else is
    /// refused here, before it reaches prod, whatever asked for it.
    /// </summary>
    sealed class ReadOnly(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath.TrimEnd('/').ToLowerInvariant() ?? "";
            var signIn = request.Method == HttpMethod.Post && (path.EndsWith("/api/accounts/login") || path.EndsWith("/api/accounts/servicetoken"));
            if (request.Method != HttpMethod.Get && !signIn)
                throw new InvalidOperationException($"The mirror only reads prod; it refused {request.Method} {path}.");
            return base.SendAsync(request, cancellationToken);
        }
    }
}
