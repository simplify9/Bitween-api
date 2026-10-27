using System.Text.Json;
using System.Text.Json.Nodes;
using SW.Serverless.Installer;
using SW.Serverless.Tooling;

namespace SW.Bitween.Cli.Configuration;

public enum Op { Create, Update, Delete, Publish, Promote, Install }

/// <summary>One thing apply would do.</summary>
public record Change(Op Op, string Section, string Key, JsonObject Want = null, Existing Have = null, List<string> Fields = null)
{
    public string Noun => Section == Kinds.Adapters ? "adapter" : Kinds.Of(Section).Noun;
}

/// <summary>What apply would do, or what stops it.</summary>
public class Plan
{
    public List<Change> Changes { get; } = [];
    public List<string> Problems { get; } = [];
    public List<string> Warnings { get; } = [];
    public int Unchanged { get; set; }

    /// <summary>What this configuration manages after the plan is carried out: for --prune next time.</summary>
    public Dictionary<string, SortedSet<string>> Managed { get; } = new();
}

/// <summary>
/// Compares a configuration file with a Bitween and brings the Bitween in line: creating what's
/// missing, updating what differs, and with --prune removing what the file no longer lists. Only
/// ever removes what an earlier apply of the same configuration put there; it remembers which, in
/// Bitween itself, so a job in a fresh container knows as much as one that ran before.
/// </summary>
public class Planner(Context ctx, ConfigFile config)
{
    string StateId => GlobalValues.StatePrefix + config.Name;

    /// <summary>Reads every section, in an order where whatever a field names has been read first.</summary>
    public async Task ReadAsync()
    {
        var raw = new Dictionary<string, List<(string Id, JsonObject Api)>>();
        foreach (var kind in Kinds.All)
        {
            raw[kind.Section] = await kind.ReadAsync(ctx);
            // Statements' keys name their data source, read just before.
            foreach (var (id, api) in raw[kind.Section]) ctx.Know(kind.Section, kind.KeyOfApi(ctx, api), id);
        }
        foreach (var kind in Kinds.All)
            ctx.Current[kind.Section] = raw[kind.Section].Select(x => kind.ToExisting(ctx, x.Id, x.Api)).ToList();
    }

    public async Task<Plan> PlanAsync(bool prune)
    {
        var plan = new Plan();
        var managedBefore = await ManagedAsync();
        recorded = Serialized(managedBefore.ToDictionary(m => m.Key, m => new SortedSet<string>(m.Value, StringComparer.Ordinal)));

        await PlanAdaptersAsync(plan);

        foreach (var kind in Kinds.All)
        {
            var wanted = config.Sections.GetValueOrDefault(kind.Section) ?? [];
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var managed = plan.Managed[kind.Section] = new SortedSet<string>(managedBefore.GetValueOrDefault(kind.Section) ?? [], StringComparer.Ordinal);

            foreach (var want in Ordered(kind, wanted))
            {
                var key = kind.KeyOf(want);
                if (string.IsNullOrWhiteSpace(key) || key.StartsWith('/') || key.EndsWith('/'))
                {
                    plan.Problems.Add($"{kind.Section}: an item has no {kind.KeyField}.");
                    continue;
                }
                var where = $"{kind.Noun} {Context.Quote(key)}";
                if (!keys.Add(key)) { plan.Problems.Add($"{where} is in the file twice."); continue; }
                if (ctx.Ambiguous(kind.Section, key)) { plan.Problems.Add($"Bitween has more than one {kind.Noun} named {Context.Quote(key)}; rename all but one."); continue; }
                foreach (var problem in kind.Unresolved(want, Known)) plan.Problems.Add($"{where}: {problem}.");
                managed.Add(key);

                var have = ctx.Current[kind.Section].FirstOrDefault(e => e.Key == key);
                if (have == null)
                {
                    plan.Changes.Add(new Change(Op.Create, kind.Section, key, want));
                    continue;
                }

                var fixedChanged = kind.Fixed.Where(f => want.ContainsKey(f) && !Values.Same(want[f], have.File[f])).ToList();
                foreach (var f in fixedChanged)
                    plan.Problems.Add($"{where}: {f} can't change once it exists ({Values.Show(have.File[f], ctx.Secrets)} to {Values.Show(want[f], ctx.Secrets)}). Remove it and add it again, under a new name or after removing the old one.");
                var differ = kind.Differences(ctx, want, have).Except(fixedChanged).ToList();
                if (differ.Count > 0) plan.Changes.Add(new Change(Op.Update, kind.Section, key, want, have, differ));
                else plan.Unchanged++;
            }

            // What an earlier apply put there and the file no longer lists.
            foreach (var key in managed.Where(k => !keys.Contains(k)).ToList())
            {
                var have = ctx.Current[kind.Section].FirstOrDefault(e => e.Key == key);
                if (have == null) managed.Remove(key);
                else if (prune) plan.Changes.Add(new Change(Op.Delete, kind.Section, key, Have: have));
            }
        }

        // An approve-once connection gets its tokens only once someone authorizes it in Bitween.
        foreach (var want in config.Sections.GetValueOrDefault(Kinds.Of("connections").Section) ?? [])
        {
            if (!string.Equals((string)want["grant"], "AuthorizationCode", StringComparison.OrdinalIgnoreCase)) continue;
            var key = Kinds.Of("connections").KeyOf(want);
            var have = ctx.Current["connections"].FirstOrDefault(e => e.Key == key);
            if (have == null || (string)have.Api["status"] != "Connected")
                plan.Warnings.Add($"connection {Context.Quote(key)} is approved once: after applying, open it on Bitween's Connections page and choose Authorize.");
        }

        // Removals last and in reverse, so nothing is removed while something still names it.
        var removals = plan.Changes.Where(c => c.Op == Op.Delete)
            .OrderByDescending(c => Kinds.All.ToList().FindIndex(k => k.Section == c.Section)).ToList();
        plan.Changes.RemoveAll(c => c.Op == Op.Delete);
        plan.Changes.AddRange(removals);
        if (!prune)
        {
            var left = Kinds.All.Sum(k => plan.Managed[k.Section].Count(key => !(config.Sections.GetValueOrDefault(k.Section) ?? []).Any(w => k.KeyOf(w) == key)));
            if (left > 0) plan.Warnings.Add($"{left} item(s) an earlier apply made are no longer in the file; --prune removes them.");
        }
        return plan;
    }

    bool Known(string section, string key) =>
        ctx.Has(section, key) || (config.Sections.GetValueOrDefault(section) ?? []).Any(w => Kinds.Of(section).KeyOf(w) == key);

    /// <summary>Its own kind first when one names another: a response subscription before the one that answers to it.</summary>
    static IEnumerable<JsonObject> Ordered(Kind kind, List<JsonObject> wanted)
    {
        var pending = wanted.ToList();
        var placed = new HashSet<string>(StringComparer.Ordinal);
        var inFile = wanted.Select(kind.KeyOf).Where(k => k != null).ToHashSet(StringComparer.Ordinal);
        while (pending.Count > 0)
        {
            var ready = pending.Where(w => kind.NamesOwnKind(w).All(k => !inFile.Contains(k) || placed.Contains(k) || k == kind.KeyOf(w))).ToList();
            if (ready.Count == 0) ready = [pending[0]]; // a loop: Bitween will say what's wrong with it
            foreach (var w in ready)
            {
                pending.Remove(w);
                if (kind.KeyOf(w) is { } k) placed.Add(k);
                yield return w;
            }
        }
    }

    async Task PlanAdaptersAsync(Plan plan)
    {
        foreach (var want in config.Sections.GetValueOrDefault(Kinds.Adapters) ?? [])
        {
            if (want.ContainsKey("install"))
            {
                await PlanInstallAsync(plan, want);
                continue;
            }
            var package = (string)want["package"];
            if (string.IsNullOrWhiteSpace(package) || !File.Exists(package))
            {
                plan.Problems.Add($"adapters: there's no package at {package ?? "(no package given)"}.");
                continue;
            }
            var manifest = AdapterRepository.ReadManifest(package);
            if (manifest?.Id == null || manifest.Version == null)
            {
                plan.Problems.Add($"adapters: {package} has no adapter.json with an id and a version; build it with bitween adapter build.");
                continue;
            }

            JsonNode listing = null;
            try { listing = await ctx.Api.GetAsync($"api/adapters/versions?adapterId={Uri.EscapeDataString(manifest.Id)}"); }
            catch (BitweenApiException ex) when (ex.Status == System.Net.HttpStatusCode.NotFound) { }

            var versions = (listing?["versions"] as JsonArray ?? []).OfType<JsonObject>().ToList();
            var published = versions.FirstOrDefault(v => (string)v["version"] == manifest.Version);
            var key = $"{manifest.Id} {manifest.Version}";
            var item = new JsonObject { ["package"] = package, ["id"] = manifest.Id, ["version"] = manifest.Version, ["current"] = (bool?)want["current"] ?? false };
            if (published == null) plan.Changes.Add(new Change(Op.Publish, Kinds.Adapters, key, item));
            else
            {
                // Published already, and versions don't change. Whether this package is the same one can't be
                // told from the listing: Bitween stamps the manifest and zips the package again when it takes
                // it, so the hash it keeps is never the uploaded file's, and comparing them warned every time.
                if ((bool?)want["current"] == true && (string)listing?["current"] != manifest.Version)
                    plan.Changes.Add(new Change(Op.Promote, Kinds.Adapters, key, item));
                else plan.Unchanged++;
            }
        }
    }

    /// <summary>
    /// A connector from the marketplace, at a version: installed unless this Bitween has that version.
    /// Not made current unless asked, so subscriptions already on another version keep running it.
    /// </summary>
    async Task PlanInstallAsync(Plan plan, JsonObject want)
    {
        var id = (string)want["install"];
        var version = (string)want["version"];
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(version))
        {
            plan.Problems.Add("adapters: an install names the connector and its version, as install: s9.openapi and version: 1.0.0.");
            return;
        }

        JsonNode listing = null;
        try { listing = await ctx.Api.GetAsync($"api/adapters/versions?adapterId={Uri.EscapeDataString(id)}"); }
        catch (BitweenApiException ex) when (ex.Status == System.Net.HttpStatusCode.NotFound) { }

        var key = $"{id} {version}";
        var current = (bool?)want["current"] ?? false;
        var item = new JsonObject { ["install"] = id, ["id"] = id, ["version"] = version, ["current"] = current };
        var has = (listing?["versions"] as JsonArray ?? []).OfType<JsonObject>().Any(v => (string)v["version"] == version);
        if (!has) plan.Changes.Add(new Change(Op.Install, Kinds.Adapters, key, item));
        else if (current && (string)listing?["current"] != version) plan.Changes.Add(new Change(Op.Promote, Kinds.Adapters, key, item));
        else plan.Unchanged++;
    }

    // ---------------------------------------------------------------------- carrying it out

    public async Task ApplyAsync(Plan plan, TextWriter output)
    {
        try
        {
            foreach (var change in plan.Changes)
            {
                output.Write("  " + Describe(change) + " ... ");
                try
                {
                    await CarryOutAsync(change);
                }
                catch (Exception ex) when (ex is BitweenApiException or ConfigException)
                {
                    output.WriteLine("failed");
                    throw new ConfigException($"{Sentence(change)} failed: {ex.Message}");
                }
                output.WriteLine("done");
            }
        }
        finally
        {
            await SaveManagedAsync(plan, output);
        }
    }

    /// <summary>
    /// Every change it can make, carrying on past one that fails, and why each that failed did: for a
    /// copy of a whole Bitween, where one subscription waiting for its adapter shouldn't hold up the
    /// rest. A change that names what failed before it fails too, and says so.
    /// </summary>
    public async Task<List<(Change Change, string Error)>> ApplyAllAsync(Plan plan)
    {
        var failed = new List<(Change, string)>();
        try
        {
            foreach (var change in plan.Changes)
            {
                try { await CarryOutAsync(change); }
                // A timed-out call surfaces as TaskCanceledException: one slow item, not a reason to stop.
                catch (Exception ex) when (ex is BitweenApiException or ConfigException or HttpRequestException or TaskCanceledException)
                {
                    failed.Add((change, ex.Message));
                }
            }
        }
        finally
        {
            await SaveManagedAsync(plan, TextWriter.Null);
        }
        return failed;
    }

    async Task CarryOutAsync(Change change)
    {
        switch (change.Op)
        {
            case Op.Publish:
                var query = (bool)change.Want["current"]! ? "?current=true" : "";
                await ctx.Api.PostFileAsync("api/adapters/packages" + query, (string)change.Want["package"], "application/zip");
                break;
            case Op.Promote:
                await ctx.Api.PostAsync("api/adapters/promote", new { adapterId = (string)change.Want["id"], version = (string)change.Want["version"] });
                break;
            case Op.Install:
                await ctx.Api.PostAsync("api/marketplace/install",
                    new { id = (string)change.Want["id"], version = (string)change.Want["version"], current = (bool)change.Want["current"]! });
                break;
            case Op.Create:
                var kind = Kinds.Of(change.Section);
                ctx.Know(change.Section, change.Key, await kind.CreateAsync(ctx, change.Want));
                break;
            case Op.Update:
                await Kinds.Of(change.Section).UpdateAsync(ctx, change.Have, change.Want);
                break;
            case Op.Delete:
                await Kinds.Of(change.Section).DeleteAsync(ctx, change.Have);
                ctx.Forget(change.Section, change.Key);
                removed.Add(change);
                break;
        }
    }

    readonly List<Change> removed = [];

    /// <summary>What was recorded before this run, to write it again only when it changed.</summary>
    string recorded;

    static string Serialized(Dictionary<string, SortedSet<string>> managed) =>
        JsonSerializer.Serialize(managed.Where(m => m.Value.Count > 0).OrderBy(m => m.Key, StringComparer.Ordinal)
            .ToDictionary(m => m.Key, m => m.Value.ToList()));

    // ---------------------------------------------------------------------- what it manages

    async Task<Dictionary<string, List<string>>> ManagedAsync()
    {
        JsonNode state;
        try { state = await ctx.Api.GetAsync($"api/globaladaptervaluessets/{Uri.EscapeDataString(StateId)}"); }
        catch (BitweenApiException ex) when (ex.Status is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Forbidden) { return new(); }

        var managed = new Dictionary<string, List<string>>();
        foreach (var (section, value) in state?["values"] as JsonObject ?? [])
        {
            try { managed[section] = JsonSerializer.Deserialize<List<string>>(value?.ToString() ?? "[]") ?? []; }
            catch (JsonException) { }
        }
        return managed;
    }

    async Task SaveManagedAsync(Plan plan, TextWriter output)
    {
        foreach (var change in removed) plan.Managed.GetValueOrDefault(change.Section)?.Remove(change.Key);
        // Unchanged on most runs of a job that applies every half hour; no need to say so in the audit trail.
        if (Serialized(plan.Managed) == recorded) return;
        var values = new JsonObject(plan.Managed.Where(m => m.Value.Count > 0)
            .Select(m => KeyValuePair.Create(m.Key, (JsonNode)JsonSerializer.Serialize(m.Value))));
        var body = new JsonObject
        {
            ["id"] = StateId,
            ["name"] = $"Managed by bitween apply: {config.Name}",
            ["values"] = values,
            ["secretProperties"] = new JsonArray(),
        };
        try
        {
            try
            {
                await ctx.Api.GetAsync($"api/globaladaptervaluessets/{Uri.EscapeDataString(StateId)}");
                await ctx.Api.PostAsync($"api/globaladaptervaluessets/{Uri.EscapeDataString(StateId)}", body);
            }
            catch (BitweenApiException ex) when (ex.Status == System.Net.HttpStatusCode.NotFound)
            {
                await ctx.Api.PostAsync("api/globaladaptervaluessets", body);
            }
        }
        catch (BitweenApiException ex)
        {
            output.WriteLine($"Warning: couldn't record what this configuration manages ({ex.Message}); --prune won't know about it.");
        }
    }

    // ---------------------------------------------------------------------- saying it

    static string Sign(Op op) => op switch { Op.Create or Op.Publish or Op.Install => "+", Op.Delete => "-", _ => "~" };

    public static string Describe(Change change) => change.Op switch
    {
        Op.Publish => $"+ publish adapter {change.Key}{((bool?)change.Want["current"] == true ? ", current" : "")}",
        Op.Promote => $"~ make adapter {change.Key} current",
        Op.Install => $"+ install adapter {change.Key} from the marketplace{((bool?)change.Want["current"] == true ? ", current" : "")}",
        _ => $"{Sign(change.Op)} {change.Noun} {Context.Quote(change.Key)}",
    };

    static string Sentence(Change change) => change.Op switch
    {
        Op.Create => $"Creating {change.Noun} {Context.Quote(change.Key)}",
        Op.Update => $"Updating {change.Noun} {Context.Quote(change.Key)}",
        Op.Delete => $"Removing {change.Noun} {Context.Quote(change.Key)}",
        Op.Publish => $"Publishing adapter {change.Key}",
        Op.Install => $"Installing adapter {change.Key} from the marketplace",
        _ => $"Making adapter {change.Key} current",
    };

    public void Print(Plan plan, TextWriter output)
    {
        foreach (var warning in plan.Warnings) output.WriteLine("Warning: " + warning);
        if (plan.Problems.Count > 0)
        {
            output.WriteLine("The configuration can't be applied:");
            foreach (var problem in plan.Problems) output.WriteLine("  ! " + problem);
            return;
        }
        foreach (var change in plan.Changes)
        {
            output.WriteLine("  " + Describe(change));
            foreach (var field in change.Fields ?? [])
                output.WriteLine($"      {field}: {Values.Show(change.Have.File[field], ctx.Secrets)} -> {Values.Show(change.Want[field], ctx.Secrets)}");
        }
        int Count(params Op[] ops) => plan.Changes.Count(c => ops.Contains(c.Op));
        output.WriteLine(plan.Changes.Count == 0
            ? $"No changes. {plan.Unchanged} item(s) already as the file says."
            : $"{Count(Op.Create)} to create, {Count(Op.Update)} to update, {Count(Op.Delete)} to remove, " +
              $"{Count(Op.Publish, Op.Promote, Op.Install)} adapter step(s); {plan.Unchanged} unchanged.");
    }
}
