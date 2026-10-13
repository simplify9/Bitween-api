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
        new NotificationChannels(),
        new RetryPolicies(),
        new DataSources(),
        new DataSourceStatements(),
        new Subscriptions(),
        new ApiGateways(),
        new BusGateways(),
        new Notifiers(),
    ];

    public static readonly IReadOnlyList<string> Sections = [.. All.Select(k => k.Section).Take(9), Adapters, .. All.Select(k => k.Section).Skip(9)];

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
        Same("duplicateInterval"), Same("dropDuplicates"), Same("disregardsUnfilteredMessages"),
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

/// <summary>Where notifications and alerts go: an adapter and what it needs, such as recipients or a webhook.</summary>
public class NotificationChannels : Kind
{
    public override string Section => "notificationChannels";
    public override string Noun => "notification channel";
    public override string Resource => "notificationchannels";

    public override IReadOnlyList<Field> Fields { get; } =
        [Same("name"), new("handler", "handlerId"), new("properties", "handlerProperties", FieldMode.Dictionary)];
}

/// <summary>
/// Retry policies. Bitween keys each group's usage and alert overrides by the group's id, and drops
/// them for a group missing from an update, so groups keep their ids by name; the file never says them.
/// Alerts go to a notification channel, named in alertChannel on the policy and on each group.
/// </summary>
public class RetryPolicies : Kind
{
    public override string Section => "retryPolicies";
    public override string Noun => "retry policy";
    public override string Resource => "retrypolicies";

    public override IReadOnlyList<Field> Fields { get; } =
        [Same("name"), Same("groups"), Reference("alertChannel", "alertChannelId", "notificationChannels")];

    /// <summary>How a group sent its alert before channels; Bitween turns a group that still says them away.</summary>
    static readonly string[] RetiredGroupFields = ["alertHandlerId", "alertHandlerProperties"];

    public override JsonObject ToFile(Context ctx, JsonObject api)
    {
        var file = base.ToFile(ctx, api);
        file["groups"] = GroupsToFile(ctx, file["groups"]);
        foreach (var group in (file["groups"] as JsonArray ?? []).OfType<JsonObject>()) group.Remove("id");
        return file;
    }

    public override JsonObject ToApi(Context ctx, JsonObject want)
    {
        var api = base.ToApi(ctx, want);
        if (api.ContainsKey("groups")) api["groups"] = GroupsToApi(ctx, api["groups"]);
        return api;
    }

    /// <summary>Groups as the file writes them: the alert channel by name, the retired fields left out.</summary>
    internal static JsonArray GroupsToFile(Context ctx, JsonNode groups) =>
        Each(groups, group => Swap(group, "alertChannelId", "alertChannel", id => ctx.KeyOf("notificationChannels", id)));

    /// <summary>Groups as the API takes them: the alert channel by id.</summary>
    internal static JsonArray GroupsToApi(Context ctx, JsonNode groups) =>
        Each(groups, group => Swap(group, "alertChannel", "alertChannelId", name => Id(ctx.IdOf("notificationChannels", name?.ToString()))));

    /// <summary>Names a group's alert gives that are nowhere, and alerts still sent the retired way.</summary>
    internal static IEnumerable<string> GroupProblems(string field, JsonNode groups, Func<string, string, bool> known)
    {
        foreach (var group in (groups as JsonArray ?? []).OfType<JsonObject>())
        {
            var name = Context.Quote((string)group["name"] ?? "(unnamed)");
            // An older export wrote them as null, which says nothing.
            if (RetiredGroupFields.Any(f => !Values.Empty(group[f])))
                yield return $"{field}: {name} sends its alert through alertHandlerId, which is retired; name a notification channel in alertChannel";
            if (group["alertChannel"]?.ToString() is { Length: > 0 } channel && !known("notificationChannels", channel))
                yield return $"{field}: {name} names alertChannel {Context.Quote(channel)}, and there's no notification channel by that name";
        }
    }

    static JsonArray Each(JsonNode groups, Func<JsonObject, JsonObject> change) =>
        new((groups as JsonArray ?? []).Select(g => g is JsonObject group ? change(group) : g?.DeepClone()).ToArray());

    /// <summary>The group with one field renamed and converted where it stands.</summary>
    static JsonObject Swap(JsonObject group, string from, string to, Func<JsonNode, JsonNode> convert)
    {
        var swapped = new JsonObject();
        foreach (var (key, value) in group)
            if (key == from) swapped[to] = convert(value);
            else if (!RetiredGroupFields.Contains(key)) swapped[key] = value?.DeepClone();
        return swapped;
    }

    /// <summary>
    /// A budget that leaves maxAttemptsTotal out allows no retries at all: Bitween reads a missing total as
    /// zero, as stored policies always have. Written in a file that is never what was meant, so it is
    /// refused; 0 written out, or null for no shared ceiling, is taken as said.
    /// </summary>
    public override IEnumerable<string> Unresolved(JsonObject want, Func<string, string, bool> known)
    {
        foreach (var problem in base.Unresolved(want, known)) yield return problem;
        // A file from before channels: the alert would otherwise be dropped without a word.
        if (!Values.Empty(want["alertHandler"]) || !Values.Empty(want["alertHandlerProperties"]))
            yield return "alertHandler is retired: alerts go through a notification channel; add one under notificationChannels and name it in alertChannel";
        foreach (var problem in GroupProblems("groups", want["groups"], known)) yield return problem;
        foreach (var group in (want["groups"] as JsonArray ?? []).OfType<JsonObject>())
            if (group["budget"] is JsonObject budget && !string.Equals((string)group["action"], "Block", StringComparison.OrdinalIgnoreCase) &&
                !budget.ContainsKey("maxAttemptsTotal"))
                yield return $"groups: {Context.Quote((string)group["name"] ?? "(unnamed)")} has no budget.maxAttemptsTotal, which Bitween reads as no retries at all; " +
                             "give the shared total, or null for no ceiling";
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
/// follows whatever is current. paused: true pauses one and false resumes it. notifications are rows of
/// { channel, onFailure, onBadResult, onSuccess, onQuiet }, a tick left out being off. What kind it is, its
/// information type, partner and what it aggregates are fixed once it exists.
/// </summary>
public class Subscriptions : Kind
{
    static readonly string[] Roles = ["receiver", "validator", "mapper", "handler"];
    static readonly string[] Ticks = ["onFailure", "onBadResult", "onSuccess", "onQuiet"];

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
        Same("expectTraffic"),
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
        file["notifications"] = new JsonArray((api["notifications"] as JsonArray ?? []).OfType<JsonObject>().Select(n =>
        {
            var row = new JsonObject { ["channel"] = ctx.KeyOf("notificationChannels", n["channelId"]) };
            foreach (var tick in Ticks.Where(t => On(n[t]))) row[tick] = true;
            return (JsonNode)row;
        }).ToArray());
        if (file["customRetryPolicy"] is JsonObject inline && inline.ContainsKey("groups"))
            inline["groups"] = RetryPolicies.GroupsToFile(ctx, inline["groups"]);
        // Bitween answers every member, set or not; the file names only what is set.
        if (file["expectTraffic"] is JsonObject expect)
            foreach (var key in expect.Where(p => Values.Empty(p.Value)).Select(p => p.Key).ToList()) expect.Remove(key);
        // Every aggregation is of the one built-in type; the file doesn't say it.
        if ((string)api["type"] == "Aggregation") file.Remove("informationType");
        return file;
    }

    public override IEnumerable<string> Unresolved(JsonObject want, Func<string, string, bool> known)
    {
        foreach (var problem in base.Unresolved(want, known)) yield return problem;
        // Bitween keeps none for an aggregation, so one in the file was taken once and then read back as a
        // change to a field that can't change, on every apply after.
        if ((string)want["type"] == "Aggregation" && want.ContainsKey("informationType"))
            yield return "informationType: an aggregation has none of its own (it rolls up the exchanges of what it aggregates); leave it out";
        foreach (var row in (want["notifications"] as JsonArray ?? []).OfType<JsonObject>())
        {
            var channel = row["channel"]?.ToString();
            if (string.IsNullOrEmpty(channel)) yield return "notifications: a row names no channel";
            else if (!known("notificationChannels", channel)) yield return $"notifications: there's no notification channel {Context.Quote(channel)}";
        }
        foreach (var problem in RetryPolicies.GroupProblems("customRetryPolicy", want["customRetryPolicy"]?["groups"], known)) yield return problem;
    }

    static bool On(JsonNode tick) => Values.Same(tick, true);

    /// <summary>Notification rows as a set, every tick said: one left out is off.</summary>
    static IEnumerable<string> Rows(JsonNode rows) =>
        (rows as JsonArray ?? []).OfType<JsonObject>()
        .Select(r => string.Join("|", Ticks.Select(t => On(r[t]) ? t : "").Prepend(r["channel"]?.ToString())))
        .Order(StringComparer.Ordinal);

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
        // A member the file leaves out is unset (days: every day, no hours, UTC), not "keep what Bitween has",
        // so one set in Bitween alone is a difference too.
        if (want.ContainsKey("expectTraffic"))
        {
            differ.Remove("expectTraffic");
            if (!Values.Same(want["expectTraffic"], have.File["expectTraffic"], exactKeys: true)) differ.Add("expectTraffic");
        }
        if (want.ContainsKey("notifications") && !Rows(want["notifications"]).SequenceEqual(Rows(have.File["notifications"])))
            differ.Add("notifications");
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
        if (api["customRetryPolicy"] is JsonObject inline && inline.ContainsKey("groups"))
            inline["groups"] = RetryPolicies.GroupsToApi(ctx, inline["groups"]);
        // Left out, Bitween keeps the rows it has; said, even empty, they replace them.
        if (want.ContainsKey("notifications"))
            api["notifications"] = new JsonArray((want["notifications"] as JsonArray ?? []).OfType<JsonObject>().Select(r =>
            {
                var row = new JsonObject { ["channelId"] = Id(ctx.IdOf("notificationChannels", r["channel"]?.ToString())) };
                foreach (var tick in Ticks) row[tick] = On(r[tick]);
                return (JsonNode)row;
            }).ToArray());
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

    public override IReadOnlyList<Field> Fields { get; } =
        [Same("name"), Same("urlName"), Same("inactive"), Same("authentication"), Same("failureAnswer")];

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

/// <summary>Legacy notifiers, kept for those still in use; subscriptions now notify through notificationChannels.</summary>
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
