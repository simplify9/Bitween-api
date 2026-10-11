using System.Text.Json.Nodes;
using static SW.Bitween.Cli.Configuration.Field;

namespace SW.Bitween.Cli.Configuration;

/// <summary>Every section a configuration file can have, in the order apply works through them.</summary>
public static class Kinds
{
    /// <summary>Not a resource like the others: packages to publish, which are never read back or removed.</summary>
    public const string Adapters = "adapters";

    public static readonly IReadOnlyList<Kind> All =
    [
        new SubscriptionCategories(),
        new WorkGroups(),
        new InformationTypes(),
        new Partners(),
        new GlobalValues(),
        new RetryPolicies(),
        new DataSources(),
        new DataSourceStatements(),
        new Subscriptions(),
        new ApiGateways(),
        new BusGateways(),
        new Notifiers(),
    ];

    public static readonly IReadOnlyList<string> Sections = [.. All.Select(k => k.Section).Take(8), Adapters, .. All.Select(k => k.Section).Skip(8)];

    public static Kind Of(string section) => All.First(k => k.Section == section);
}

public class SubscriptionCategories : Kind
{
    public override string Section => "subscriptionCategories";
    public override string Noun => "subscription category";
    public override string Resource => "subscriptioncategories";
    public override string KeyField => "code";
    public override IReadOnlyList<Field> Fields { get; } = [Same("code"), Same("description")];

    protected override Task<List<JsonObject>> ListAsync(Context ctx) => Listed(ctx, "api/subscriptioncategories?limit=100000");
    protected override Task<(string, JsonObject)> DetailOf(Context ctx, JsonObject row) => Task.FromResult((row["id"]!.ToString(), row));
    public override Task DeleteAsync(Context ctx, Existing have) => ctx.Api.PostAsync($"api/{Resource}/{have.Id}/delete", new { });
}

public class WorkGroups : Kind
{
    public override string Section => "workGroups";
    public override string Noun => "work group";
    public override string Resource => "workgroups";
    public override IReadOnlyList<Field> Fields { get; } = [Same("name"), Same("busMessageName"), Same("options")];

    protected override Task<List<JsonObject>> ListAsync(Context ctx) => Listed(ctx, "api/workgroups?limit=1000000");
    protected override Task<(string, JsonObject)> DetailOf(Context ctx, JsonObject row) => Task.FromResult((row["id"]!.ToString(), row));
    public override Task DeleteAsync(Context ctx, Existing have) => ctx.Api.PostAsync($"api/{Resource}/{have.Id}/delete", new { });
}

public class InformationTypes : Kind
{
    /// <summary>Built in: what aggregations produce. Not configuration anyone writes.</summary>
    const string AggregationDocumentId = "10001";

    public override string Section => "informationTypes";
    public override string Noun => "information type";
    public override string Resource => "documents";

    public override IReadOnlyList<Field> Fields { get; } =
    [
        Same("name"), Same("code"), new("format", "documentFormat"), Same("busEnabled"), Same("busMessageTypeName"),
        Same("duplicateInterval"), Same("disregardsUnfilteredMessages"),
        new("promotedProperties", "promotedProperties", FieldMode.KeyValues), Same("validationSchema"),
    ];

    protected override bool Include(string id, JsonObject api) => id != AggregationDocumentId;

    protected override JsonObject UpdateBody(Context ctx, Existing have, JsonObject want)
    {
        var body = base.UpdateBody(ctx, have, want);
        body["id"] = Id(have.Id);
        return body;
    }
}

/// <summary>
/// Partners, with their API keys under keys: { name: value }. Bitween keeps only a hash of a key and
/// shows its first five characters, so a key is the same if it starts with those; one that doesn't is
/// replaced, which takes two calls, since a key keeps its stored value for as long as its name stays.
/// </summary>
public class Partners : Kind
{
    const string SystemPartnerId = "1";

    public override string Section => "partners";
    public override string Noun => "partner";
    public override string Resource => "partners";

    public override IReadOnlyList<Field> Fields { get; } =
    [
        Same("name"), new("properties", "adapterProperties", FieldMode.Dictionary),
        new("secretProperties", "secretProperties", FieldMode.Set), Same("loginIdentity"),
    ];

    protected override bool Include(string id, JsonObject api) => id != SystemPartnerId;

    public override JsonObject ToFile(Context ctx, JsonObject api)
    {
        var file = base.ToFile(ctx, api);
        file["keys"] = Values.ToMap(api["apiCredentials"]);
        return file;
    }

    /// <summary>"abcde...(hidden)" says the key starts abcde; anything else says nothing.</summary>
    static string Shown(JsonNode masked)
    {
        var s = masked?.ToString() ?? "";
        var end = s.IndexOf("...", StringComparison.Ordinal);
        return end > 0 ? s[..end] : null;
    }

    static bool SameKey(JsonNode want, JsonNode have) => Shown(have) is not { } start || (want?.ToString() ?? "").StartsWith(start, StringComparison.Ordinal);

    public override List<string> Differences(Context ctx, JsonObject want, Existing have)
    {
        var differ = base.Differences(ctx, want, have);
        if (want["keys"] is JsonObject keys)
        {
            var current = have.File["keys"] as JsonObject ?? [];
            if (keys.Count != current.Count || keys.Any(k => !current.ContainsKey(k.Key) || !SameKey(k.Value, current[k.Key])))
                differ.Add("keys");
        }
        return differ;
    }

    protected override JsonObject CreateBody(Context ctx, JsonObject want)
    {
        var body = ToApi(ctx, want);
        body["adapterProperties"] ??= new JsonObject();
        body["secretProperties"] ??= new JsonArray();
        return body;
    }

    public override async Task<string> CreateAsync(Context ctx, JsonObject want)
    {
        var id = await base.CreateAsync(ctx, want);
        if (want["keys"] is JsonObject { Count: > 0 })
        {
            var api = (JsonObject)await ctx.Api.GetAsync($"api/partners/{id}");
            await UpdateAsync(ctx, ToExisting(ctx, id, api), want);
        }
        return id;
    }

    public override async Task UpdateAsync(Context ctx, Existing have, JsonObject want)
    {
        var body = UpdateBody(ctx, have, want);
        if (want["keys"] is JsonObject keys)
        {
            var current = have.File["keys"] as JsonObject ?? [];
            var replaced = keys.Where(k => current.ContainsKey(k.Key) && !SameKey(k.Value, current[k.Key])).Select(k => k.Key).ToHashSet();
            if (replaced.Count > 0)
            {
                // First without them, so their stored hashes go; then with them, hashed anew.
                body["apiCredentials"] = Values.ToKeyValues(new JsonObject(keys.Where(k => !replaced.Contains(k.Key))
                    .Select(k => KeyValuePair.Create(k.Key, k.Value?.DeepClone()))));
                await ctx.Api.PostAsync($"api/partners/{have.Id}", body);
            }
            body["apiCredentials"] = Values.ToKeyValues(keys);
        }
        await ctx.Api.PostAsync($"api/partners/{have.Id}", body);
    }
}

public class GlobalValues : Kind
{
    /// <summary>Where apply remembers what it manages; not configuration of anyone's.</summary>
    public const string StatePrefix = "bitween-apply.";

    public override string Section => "globalValues";
    public override string Noun => "global values set";
    public override string Resource => "globaladaptervaluessets";
    public override string KeyField => "id";

    public override IReadOnlyList<Field> Fields { get; } =
        [Same("id"), Same("name"), new("values", "values", FieldMode.Dictionary), new("secretProperties", "secretProperties", FieldMode.Set)];

    protected override Task<(string, JsonObject)> DetailOf(Context ctx, JsonObject row) => Task.FromResult((row["id"]!.ToString(), row));
    protected override bool Include(string id, JsonObject api) => !id.StartsWith(StatePrefix, StringComparison.Ordinal);

    protected override JsonObject CreateBody(Context ctx, JsonObject want)
    {
        var body = ToApi(ctx, want);
        body["values"] ??= new JsonObject();
        body["secretProperties"] ??= new JsonArray();
        return body;
    }

    public override Task DeleteAsync(Context ctx, Existing have) => ctx.Api.PostAsync($"api/{Resource}/{Uri.EscapeDataString(have.Id)}/delete", new { });
}

/// <summary>
/// Retry policies. Bitween keys each group's usage and alert overrides by the group's id, and drops
/// them for a group missing from an update, so groups keep their ids by name; the file never says them.
/// </summary>
public class RetryPolicies : Kind
{
    public override string Section => "retryPolicies";
    public override string Noun => "retry policy";
    public override string Resource => "retrypolicies";

    public override IReadOnlyList<Field> Fields { get; } =
    [
        Same("name"), Same("groups"), new("alertHandler", "alertHandlerId"),
        new("alertHandlerProperties", "alertHandlerProperties", FieldMode.Dictionary),
    ];

    public override JsonObject ToFile(Context ctx, JsonObject api)
    {
        var file = base.ToFile(ctx, api);
        foreach (var group in (file["groups"] as JsonArray ?? []).OfType<JsonObject>()) group.Remove("id");
        return file;
    }

    protected override JsonObject UpdateBody(Context ctx, Existing have, JsonObject want)
    {
        var body = base.UpdateBody(ctx, have, want);
        var ids = (have.Api["groups"] as JsonArray ?? []).OfType<JsonObject>()
            .Where(g => g["name"] != null).GroupBy(g => g["name"]!.ToString()).ToDictionary(g => g.Key, g => g.First()["id"]?.DeepClone());
        foreach (var group in (body["groups"] as JsonArray ?? []).OfType<JsonObject>())
            if (group["id"] == null && group["name"] is { } name && ids.TryGetValue(name.ToString(), out var id))
                group["id"] = id;
        return body;
    }
}

public class DataSources : Kind
{
    public override string Section => "dataSources";
    public override string Noun => "data source";
    public override string Resource => "datasources";

    public override IReadOnlyList<Field> Fields { get; } =
    [
        Same("name"), new("adapter", "adapterId"), Same("kind"), Same("placement"),
        new("properties", "properties", FieldMode.Dictionary), new("secretProperties", "secretProperties", FieldMode.Set),
        Same("inactive"), Same("softMemoryLimitMb"), Same("hardMemoryLimitMb"), Same("cpuPercentLimit"), Same("cpuLimitSamples"),
        Same("deduplicationWindowDays"),
    ];

    public override List<string> Differences(Context ctx, JsonObject want, Existing have)
    {
        var differ = base.Differences(ctx, want, have);
        // Bitween adds names that look secret on its own, so it can hold more than the file says.
        if (differ.Remove("secretProperties"))
        {
            var held = (have.File["secretProperties"] as JsonArray ?? []).Select(x => x?.ToString()).ToHashSet();
            if ((want["secretProperties"] as JsonArray ?? []).Any(x => !held.Contains(x?.ToString()))) differ.Add("secretProperties");
        }
        return differ;
    }
}

/// <summary>A data source's named SQL, keyed by data source and name: "warehouse/new orders".</summary>
public class DataSourceStatements : Kind
{
    public override string Section => "dataSourceStatements";
    public override string Noun => "SQL statement";
    public override string Resource => "datasourcestatements";

    public override IReadOnlyList<Field> Fields { get; } =
    [
        Reference("dataSource", "dataSourceId", "dataSources"), Same("name"), Same("sql"), Same("description"),
        Reference("workGroup", "workGroupId", "workGroups"), Same("inactive"), Same("cursorColumn"), Same("keyColumn"),
    ];

    public override IReadOnlyList<string> Fixed => ["dataSource"];

    public override string KeyOf(JsonObject file) => $"{file["dataSource"]}/{file["name"]}";

    public override string KeyOfApi(Context ctx, JsonObject api) => $"{ctx.KeyOf("dataSources", api["dataSourceId"])}/{api["name"]}";

    protected override Task<(string, JsonObject)> DetailOf(Context ctx, JsonObject row) => Task.FromResult((row["id"]!.ToString(), row));
}

/// <summary>
/// Subscriptions. Each adapter is a block: handler: { adapter, version, properties }; no version
/// follows whatever is current. paused: true pauses one and false resumes it. What kind it is, its
/// information type, partner and what it aggregates are fixed once it exists.
/// </summary>
public class Subscriptions : Kind
{
    static readonly string[] Roles = ["receiver", "validator", "mapper", "handler"];

    public override string Section => "subscriptions";
    public override string Noun => "subscription";
    public override string Resource => "subscriptions";

    public override IReadOnlyList<Field> Fields { get; } =
    [
        Same("name"), Same("type"),
        Reference("informationType", "documentId", "informationTypes"),
        Reference("partner", "partnerId", "partners"),
        Reference("aggregationFor", "aggregationForId", "subscriptions"),
        Same("inactive"),
        Reference("dataSource", "dataSourceId", "dataSources"),
        Reference("category", "categoryId", "subscriptionCategories"),
        Reference("workGroup", "workGroupId", "workGroups"),
        Reference("retryPolicy", "retryPolicyId", "retryPolicies"),
        Same("customRetryPolicy"), Same("matchExpression"),
        new("documentFilter", "documentFilter", FieldMode.KeyValues),
        Same("schedules"),
        Reference("responseSubscription", "responseSubscriptionId", "subscriptions"),
        Same("responseMessageTypeName"), Same("runOnBadResponses"), Same("autoPauseAfterFailures"), Same("aggregationTarget"),
    ];

    public override IReadOnlyList<string> Fixed => ["type", "informationType", "partner", "aggregationFor"];

    public override JsonObject ToFile(Context ctx, JsonObject api)
    {
        var file = base.ToFile(ctx, api);
        foreach (var role in Roles)
        {
            if (string.IsNullOrEmpty((string)api[role + "Id"])) continue;
            var block = new JsonObject { ["adapter"] = (string)api[role + "Id"] };
            if (!string.IsNullOrWhiteSpace((string)api[role + "Version"])) block["version"] = (string)api[role + "Version"];
            block["properties"] = Values.ToMap(api[role + "Properties"]);
            file[role] = block;
        }
        file["paused"] = api["pausedOn"] != null;
        // Every aggregation is of the one built-in type; the file doesn't say it.
        if ((string)api["type"] == "Aggregation") file.Remove("informationType");
        return file;
    }

    public override List<string> Differences(Context ctx, JsonObject want, Existing have)
    {
        var differ = base.Differences(ctx, want, have);
        foreach (var role in Roles.Where(want.ContainsKey))
        {
            var w = want[role] as JsonObject;
            var h = have.File[role] as JsonObject;
            var same = w == null
                ? h == null
                : h != null && (string)w["adapter"] == (string)h["adapter"] && Values.Same(w["version"], h["version"]) &&
                  (!w.ContainsKey("properties") || Values.Same(w["properties"], h["properties"], exactKeys: true));
            if (!same) differ.Add(role);
        }
        if (want["paused"] is JsonValue paused && (bool)paused != ((bool?)have.File["paused"] ?? false)) differ.Add("paused");
        return differ;
    }

    public override JsonObject ToApi(Context ctx, JsonObject want)
    {
        var api = base.ToApi(ctx, want);
        foreach (var role in Roles.Where(want.ContainsKey))
        {
            var block = want[role] as JsonObject;
            api[role + "Id"] = (string)block?["adapter"];
            api[role + "Version"] = (string)block?["version"];
            if (block == null || block.ContainsKey("properties"))
                api[role + "Properties"] = Values.ToKeyValues(block?["properties"]);
        }
        // One or the other: a named policy, or one of its own.
        if (want["retryPolicy"] != null) api["customRetryPolicy"] = null;
        if (want["customRetryPolicy"] != null) api["retryPolicyId"] = null;
        return api;
    }

    protected override JsonObject CreateBody(Context ctx, JsonObject want)
    {
        var body = ToApi(ctx, want);
        // Bitween makes a subscription switched off unless told otherwise; a file means what it says.
        body["inactive"] = (bool?)want["inactive"] ?? false;
        foreach (var role in Roles) body[role + "Properties"] ??= new JsonArray();
        body["documentFilter"] ??= new JsonArray();
        return body;
    }

    public override async Task<string> CreateAsync(Context ctx, JsonObject want)
    {
        var id = await base.CreateAsync(ctx, want);
        if ((bool?)want["paused"] == true) await ctx.Api.PostAsync($"api/subscriptions/{id}/pause", new { });
        return id;
    }

    public override async Task UpdateAsync(Context ctx, Existing have, JsonObject want)
    {
        await base.UpdateAsync(ctx, have, want);
        // A toggle: only when it's the other way round.
        if (want["paused"] is JsonValue paused && (bool)paused != ((bool?)have.File["paused"] ?? false))
            await ctx.Api.PostAsync($"api/subscriptions/{have.Id}/pause", new { });
    }
}

/// <summary>API gateways, keyed by their address, with the partners attached: partners: [{ partner, subscription }].</summary>
public class ApiGateways : Kind
{
    public override string Section => "apiGateways";
    public override string Noun => "API gateway";
    public override string Resource => "apigateways";
    public override string KeyField => "urlName";

    public override IReadOnlyList<Field> Fields { get; } = [Same("name"), Same("urlName"), Same("inactive"), Same("authentication")];

    public override IEnumerable<string> DependsOn => ["partners", "subscriptions"];

    protected override Task<List<JsonObject>> ListAsync(Context ctx) => Searchy(ctx, Resource);

    public override JsonObject ToFile(Context ctx, JsonObject api)
    {
        var file = base.ToFile(ctx, api);
        file["partners"] = new JsonArray((api["partners"] as JsonArray ?? []).OfType<JsonObject>()
            .Select(p => new JsonObject { ["partner"] = ctx.KeyOf("partners", p["partnerId"]), ["subscription"] = ctx.KeyOf("subscriptions", p["subscriptionId"]) })
            .OrderBy(p => (string)p["partner"], StringComparer.Ordinal).Select(p => (JsonNode)p).ToArray());
        return file;
    }

    static Dictionary<string, string> Attachments(JsonNode list) =>
        (list as JsonArray ?? []).OfType<JsonObject>().Where(p => p["partner"] != null)
        .GroupBy(p => p["partner"]!.ToString()).ToDictionary(g => g.Key, g => (string)g.First()["subscription"]);

    public override List<string> Differences(Context ctx, JsonObject want, Existing have)
    {
        var differ = base.Differences(ctx, want, have);
        if (want.ContainsKey("partners"))
        {
            var w = Attachments(want["partners"]);
            var h = Attachments(have.File["partners"]);
            if (w.Count != h.Count || w.Any(p => !h.TryGetValue(p.Key, out var s) || s != p.Value)) differ.Add("partners");
        }
        return differ;
    }

    public override IEnumerable<string> Unresolved(JsonObject want, Func<string, string, bool> known)
    {
        foreach (var problem in base.Unresolved(want, known)) yield return problem;
        foreach (var (partner, subscription) in Attachments(want["partners"]))
        {
            if (!known("partners", partner)) yield return $"partners: there's no partner {Context.Quote(partner)}";
            if (subscription != null && !known("subscriptions", subscription)) yield return $"partners: there's no subscription {Context.Quote(subscription)}";
        }
    }

    public override JsonObject ToApi(Context ctx, JsonObject want)
    {
        var api = base.ToApi(ctx, want);
        api.Remove("partners");
        return api;
    }

    public override async Task<string> CreateAsync(Context ctx, JsonObject want)
    {
        var id = await base.CreateAsync(ctx, want);
        await AttachAsync(ctx, id, [], want);
        return id;
    }

    public override async Task UpdateAsync(Context ctx, Existing have, JsonObject want)
    {
        await base.UpdateAsync(ctx, have, want);
        await AttachAsync(ctx, have.Id, Attachments(have.File["partners"]), want);
    }

    static async Task AttachAsync(Context ctx, string id, Dictionary<string, string> current, JsonObject want)
    {
        if (!want.ContainsKey("partners")) return;
        var wanted = Attachments(want["partners"]);
        foreach (var (partner, subscription) in wanted)
        {
            var body = new { partnerId = int.Parse(ctx.IdOf("partners", partner)), subscriptionId = int.Parse(ctx.IdOf("subscriptions", subscription)) };
            if (!current.TryGetValue(partner, out var had)) await ctx.Api.PostAsync($"api/apigateways/{id}/addpartner", body);
            else if (had != subscription) await ctx.Api.PostAsync($"api/apigateways/{id}/updatepartner", body);
        }
        foreach (var partner in current.Keys.Where(p => !wanted.ContainsKey(p)))
            await ctx.Api.PostAsync($"api/apigateways/{id}/removepartner", new { partnerId = int.Parse(ctx.IdOf("partners", partner)) });
    }
}

/// <summary>Bus gateways, with their routes: routes: [{ subscription, partner, matchExpression }].</summary>
public class BusGateways : Kind
{
    public override string Section => "busGateways";
    public override string Noun => "bus gateway";
    public override string Resource => "busgateways";

    public override IReadOnlyList<Field> Fields { get; } =
    [
        Same("name"), Reference("informationType", "documentId", "informationTypes"), Same("inactive"),
        Reference("dataSource", "dataSourceId", "dataSources"), Same("endpoint"),
        new("endpointProperties", "endpointProperties", FieldMode.Dictionary),
    ];

    public override IReadOnlyList<string> Fixed => ["informationType"];
    public override IEnumerable<string> DependsOn => [.. base.DependsOn, "partners", "subscriptions"];

    public override JsonObject ToFile(Context ctx, JsonObject api)
    {
        var file = base.ToFile(ctx, api);
        file["routes"] = new JsonArray((api["routes"] as JsonArray ?? []).OfType<JsonObject>().Select(r => (JsonNode)Values.Tidy(new JsonObject
        {
            ["subscription"] = ctx.KeyOf("subscriptions", r["subscriptionId"]),
            ["partner"] = ctx.KeyOf("partners", r["partnerId"]),
            ["matchExpression"] = r["matchExpression"]?.DeepClone(),
        })).ToArray());
        return file;
    }

    static string RouteKey(JsonObject route) => $"{route["subscription"]}|{route["partner"]}";

    static Dictionary<string, JsonObject> Routes(JsonNode list) =>
        (list as JsonArray ?? []).OfType<JsonObject>().GroupBy(RouteKey).ToDictionary(g => g.Key, g => g.First());

    public override List<string> Differences(Context ctx, JsonObject want, Existing have)
    {
        var differ = base.Differences(ctx, want, have);
        if (want.ContainsKey("routes"))
        {
            var w = Routes(want["routes"]);
            var h = Routes(have.File["routes"]);
            if (w.Count != h.Count || w.Any(r => !h.TryGetValue(r.Key, out var had) || !Values.Same(r.Value["matchExpression"], had["matchExpression"])))
                differ.Add("routes");
        }
        return differ;
    }

    public override IEnumerable<string> Unresolved(JsonObject want, Func<string, string, bool> known)
    {
        foreach (var problem in base.Unresolved(want, known)) yield return problem;
        foreach (var route in Routes(want["routes"]).Values)
        {
            if ((string)route["subscription"] is { } s && !known("subscriptions", s)) yield return $"routes: there's no subscription {Context.Quote(s)}";
            if ((string)route["partner"] is { } p && !known("partners", p)) yield return $"routes: there's no partner {Context.Quote(p)}";
        }
    }

    public override JsonObject ToApi(Context ctx, JsonObject want)
    {
        var api = base.ToApi(ctx, want);
        api.Remove("routes");
        return api;
    }

    protected override JsonObject CreateBody(Context ctx, JsonObject want)
    {
        var body = ToApi(ctx, want);
        body["endpointProperties"] ??= new JsonObject();
        return body;
    }

    public override async Task<string> CreateAsync(Context ctx, JsonObject want)
    {
        var id = await base.CreateAsync(ctx, want);
        await RouteAsync(ctx, id, [], [], want);
        return id;
    }

    public override async Task UpdateAsync(Context ctx, Existing have, JsonObject want)
    {
        await base.UpdateAsync(ctx, have, want);
        var ids = (have.Api["routes"] as JsonArray ?? []).OfType<JsonObject>().ToDictionary(
            r => $"{ctx.KeyOf("subscriptions", r["subscriptionId"])}|{ctx.KeyOf("partners", r["partnerId"])}", r => (int)r["id"]!);
        await RouteAsync(ctx, have.Id, Routes(have.File["routes"]), ids, want);
    }

    static async Task RouteAsync(Context ctx, string id, Dictionary<string, JsonObject> current, Dictionary<string, int> routeIds, JsonObject want)
    {
        if (!want.ContainsKey("routes")) return;
        var wanted = Routes(want["routes"]);
        foreach (var (key, route) in wanted)
        {
            var partner = (string)route["partner"];
            var body = new JsonObject
            {
                ["subscriptionId"] = int.Parse(ctx.IdOf("subscriptions", (string)route["subscription"])),
                ["partnerId"] = partner == null ? null : int.Parse(ctx.IdOf("partners", partner)),
                ["matchExpression"] = route["matchExpression"]?.DeepClone(),
            };
            if (!current.TryGetValue(key, out var had)) await ctx.Api.PostAsync($"api/busgateways/{id}/addroute", body);
            else if (!Values.Same(route["matchExpression"], had["matchExpression"]))
            {
                body["routeId"] = routeIds[key];
                await ctx.Api.PostAsync($"api/busgateways/{id}/updateroute", body);
            }
        }
        foreach (var key in current.Keys.Where(k => !wanted.ContainsKey(k)))
            await ctx.Api.PostAsync($"api/busgateways/{id}/removeroute", new { routeId = routeIds[key] });
    }
}

public class Notifiers : Kind
{
    public override string Section => "notifiers";
    public override string Noun => "notifier";
    public override string Resource => "notifiers";

    public override IReadOnlyList<Field> Fields { get; } =
    [
        Same("name"), new("handler", "handlerId"), new("properties", "handlerProperties", FieldMode.KeyValues),
        Same("runOnSuccessfulResult"), Same("runOnBadResult"), Same("runOnFailedResult"), Same("inactive"),
        new("subscriptions", "runOnSubscriptions", FieldMode.RefList, "subscriptions"),
    ];

    /// <summary>Bitween makes one from its name alone; the rest is an update.</summary>
    public override async Task<string> CreateAsync(Context ctx, JsonObject want)
    {
        var id = IdIn(await ctx.Api.PostAsync("api/notifiers", new { name = (string)want["name"] }));
        var api = (JsonObject)await ctx.Api.GetAsync($"api/notifiers/{id}");
        await UpdateAsync(ctx, ToExisting(ctx, id, api), want);
        return id;
    }
}
