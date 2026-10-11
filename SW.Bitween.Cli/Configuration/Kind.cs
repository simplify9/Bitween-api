using System.Text.Json.Nodes;

namespace SW.Bitween.Cli.Configuration;

/// <summary>Something in Bitween as it is now: its id, its key in the file, and how the file would say it.</summary>
public record Existing(string Id, string Key, JsonObject File, JsonObject Api);

/// <summary>What the commands share: the API, and every item's id by key, which grows as apply creates them.</summary>
public class Context(BitweenApi api, ISet<string> secrets = null)
{
    public BitweenApi Api { get; } = api;
    public ISet<string> Secrets { get; } = secrets ?? new HashSet<string>();
    public Dictionary<string, List<Existing>> Current { get; } = new();

    readonly Dictionary<string, Dictionary<string, string>> ids = new();
    readonly Dictionary<string, Dictionary<string, string>> keys = new();

    readonly Dictionary<string, HashSet<string>> duplicates = new();

    public void Know(string section, string key, string id)
    {
        if (key == null) return;
        if (Map(ids, section).TryGetValue(key, out var other) && other != id)
            (duplicates.TryGetValue(section, out var set) ? set : duplicates[section] = new(StringComparer.Ordinal)).Add(key);
        Map(ids, section)[key] = id;
        Map(keys, section)[id] = key;
    }

    /// <summary>Whether Bitween has more than one by this name: the file can't say which it means.</summary>
    public bool Ambiguous(string section, string key) => duplicates.TryGetValue(section, out var set) && set.Contains(key);

    public void Forget(string section, string key)
    {
        if (Map(ids, section).Remove(key, out var id)) Map(keys, section).Remove(id);
    }

    public bool Has(string section, string key) => key != null && Map(ids, section).ContainsKey(key);

    /// <summary>The id to send for a name in the file.</summary>
    public string IdOf(string section, string key) =>
        key == null ? null
        : Ambiguous(section, key) ? throw new ConfigException($"There's more than one {Kinds.Of(section).Noun} named {Quote(key)}; rename all but one.")
        : Map(ids, section).TryGetValue(key, out var id) ? id
        : throw new ConfigException($"There's no {Kinds.Of(section).Noun} {Quote(key)}.");

    /// <summary>The name to write for an id the API answered with; null when it isn't one this knows.</summary>
    public string KeyOf(string section, JsonNode id) =>
        id == null ? null : Map(keys, section).GetValueOrDefault(id.ToString());

    static Dictionary<string, string> Map(Dictionary<string, Dictionary<string, string>> all, string section) =>
        all.TryGetValue(section, out var map) ? map : all[section] = new(StringComparer.Ordinal);

    public static string Quote(string key) => $"\"{key}\"";
}

public enum FieldMode
{
    /// <summary>The same in the file as in the API; nested values compare by what the file says.</summary>
    Plain,

    /// <summary>An id in the API, the other item's name in the file.</summary>
    Ref,

    /// <summary>[{key, value}] in the API, a mapping in the file; replaces all keys.</summary>
    KeyValues,

    /// <summary>A mapping in both, of strings; replaces all keys.</summary>
    Dictionary,

    /// <summary>[{id}] in the API, a list of names in the file; compared as a set.</summary>
    RefList,

    /// <summary>A list of strings whose order means nothing.</summary>
    Set,
}

/// <summary>One field: what the file calls it, what the API calls it, and how one becomes the other.</summary>
public record Field(string File, string Api, FieldMode Mode = FieldMode.Plain, string Ref = null)
{
    public static Field Same(string name) => new(name, name);
    public static Field Reference(string file, string api, string section) => new(file, api, FieldMode.Ref, section);
}

/// <summary>
/// One section of the file and the resource it configures. The defaults fit most: list, read each,
/// create, update by sending everything back with the file's fields over it, delete. A resource that
/// works differently says so by overriding.
/// </summary>
public abstract class Kind
{
    /// <summary>The section in the file, e.g. informationTypes.</summary>
    public abstract string Section { get; }

    /// <summary>What one is called in sentences, e.g. information type.</summary>
    public abstract string Noun { get; }

    /// <summary>Its path under /api, e.g. documents.</summary>
    public abstract string Resource { get; }

    /// <summary>The field in the file that names it, unique among its kind.</summary>
    public virtual string KeyField => "name";

    public abstract IReadOnlyList<Field> Fields { get; }

    /// <summary>Fields Bitween won't change once the item exists; a different value is an error, not an update.</summary>
    public virtual IReadOnlyList<string> Fixed => [];

    /// <summary>Sections whose items this one's fields name, so those are applied first.</summary>
    public virtual IEnumerable<string> DependsOn => Fields.Where(f => f.Ref != null).Select(f => f.Ref).Distinct();

    /// <summary>Keys of others of its own kind this one names, which have to exist first.</summary>
    public IEnumerable<string> NamesOwnKind(JsonObject want) =>
        Fields.Where(f => f.Ref == Section && want.ContainsKey(f.File)).Select(f => want[f.File]?.ToString()).Where(k => !string.IsNullOrEmpty(k));

    public virtual string KeyOf(JsonObject file) => (string)file[KeyField];

    // ------------------------------------------------------------------ reading

    /// <summary>
    /// Every one there is, in the API's form. Turned into the file's form only once every section has
    /// been read, since names for ids can point anywhere, a subscription to another one included.
    /// </summary>
    public virtual async Task<List<(string Id, JsonObject Api)>> ReadAsync(Context ctx)
    {
        var rows = await ListAsync(ctx);
        var full = await Parallel(rows, row => DetailOf(ctx, row));
        return full.Where(x => x.Api != null && Include(x.Id, x.Api)).ToList();
    }

    /// <summary>Its key, from the API's form: what the file calls it.</summary>
    public virtual string KeyOfApi(Context ctx, JsonObject api) =>
        api[Fields.First(f => f.File == KeyField).Api]?.ToString();

    /// <summary>Its rows from the list endpoint.</summary>
    protected virtual async Task<List<JsonObject>> ListAsync(Context ctx) => await Searchy(ctx, Resource);

    /// <summary>One row in full: the GET by id, unless the list row already is.</summary>
    protected virtual async Task<(string Id, JsonObject Api)> DetailOf(Context ctx, JsonObject row)
    {
        var id = row["id"]!.ToString();
        return (id, await ctx.Api.GetAsync($"api/{Resource}/{Uri.EscapeDataString(id)}") as JsonObject);
    }

    /// <summary>False for what isn't configuration to manage, such as built-in items.</summary>
    protected virtual bool Include(string id, JsonObject api) => true;

    public Existing ToExisting(Context ctx, string id, JsonObject api)
    {
        var file = ToFile(ctx, api);
        return new Existing(id, KeyOf(file), file, api);
    }

    /// <summary>The API's form as the file would write it. Names for ids are looked up, so read what's referred to first.</summary>
    public virtual JsonObject ToFile(Context ctx, JsonObject api)
    {
        var file = new JsonObject();
        foreach (var f in Fields)
        {
            var value = api[f.Api];
            file[f.File] = f.Mode switch
            {
                FieldMode.Ref => ctx.KeyOf(f.Ref, value),
                FieldMode.KeyValues => Values.ToMap(value),
                FieldMode.RefList => new JsonArray((value as JsonArray ?? [])
                    .Select(x => ctx.KeyOf(f.Ref, x is JsonObject o ? o["id"] : x)).Where(k => k != null)
                    .OrderBy(k => k, StringComparer.Ordinal).Select(k => (JsonNode)k).ToArray()),
                _ => value?.DeepClone(),
            };
        }
        return file;
    }

    // ------------------------------------------------------------------ comparing

    /// <summary>The fields the file sets that differ from what Bitween has.</summary>
    public virtual List<string> Differences(Context ctx, JsonObject want, Existing have)
    {
        var differ = new List<string>();
        foreach (var f in Fields)
        {
            if (!want.ContainsKey(f.File) || f.File == KeyField) continue;
            var same = f.Mode switch
            {
                FieldMode.KeyValues or FieldMode.Dictionary => Values.Same(want[f.File], have.File[f.File], exactKeys: true),
                FieldMode.RefList or FieldMode.Set => SameSet(want[f.File], have.File[f.File]),
                _ => Values.Same(want[f.File], have.File[f.File]),
            };
            if (!same) differ.Add(f.File);
        }
        return differ;
    }

    protected static bool SameSet(JsonNode want, JsonNode have) =>
        (want as JsonArray ?? []).Select(x => x?.ToString()).Order(StringComparer.Ordinal)
        .SequenceEqual((have as JsonArray ?? []).Select(x => x?.ToString()).Order(StringComparer.Ordinal));

    /// <summary>Names in the file's fields that are neither in Bitween nor in the file.</summary>
    public virtual IEnumerable<string> Unresolved(JsonObject want, Func<string, string, bool> known)
    {
        foreach (var f in Fields.Where(f => f.Ref != null && want.ContainsKey(f.File)))
        {
            var names = f.Mode == FieldMode.RefList
                ? (want[f.File] as JsonArray ?? []).Select(x => x?.ToString())
                : [want[f.File]?.ToString()];
            foreach (var name in names.Where(n => !string.IsNullOrEmpty(n) && !known(f.Ref, n)))
                yield return $"{f.File}: there's no {Kinds.Of(f.Ref).Noun} {Context.Quote(name)}";
        }
    }

    // ------------------------------------------------------------------ writing

    /// <summary>The file's fields in the API's form, ids for names.</summary>
    public virtual JsonObject ToApi(Context ctx, JsonObject want)
    {
        var api = new JsonObject();
        foreach (var f in Fields.Where(f => want.ContainsKey(f.File)))
        {
            var value = want[f.File];
            api[f.Api] = f.Mode switch
            {
                FieldMode.Ref => Id(ctx.IdOf(f.Ref, value?.ToString())),
                FieldMode.KeyValues => Values.ToKeyValues(value),
                FieldMode.Dictionary => Values.ToStrings(value),
                FieldMode.RefList => new JsonArray((value as JsonArray ?? [])
                    .Select(x => (JsonNode)new JsonObject { ["id"] = Id(ctx.IdOf(f.Ref, x?.ToString())) }).ToArray()),
                _ => value?.DeepClone(),
            };
        }
        return api;
    }

    /// <summary>Ids go back as numbers where they are numbers.</summary>
    protected static JsonNode Id(string id) => id == null ? null : int.TryParse(id, out var n) ? JsonValue.Create(n) : JsonValue.Create(id);

    /// <summary>Creates it and returns its id.</summary>
    public virtual async Task<string> CreateAsync(Context ctx, JsonObject want)
    {
        var answer = await ctx.Api.PostAsync($"api/{Resource}", CreateBody(ctx, want));
        return IdIn(answer);
    }

    protected virtual JsonObject CreateBody(Context ctx, JsonObject want) => ToApi(ctx, want);

    /// <summary>An id however the API answered it: a number, or { id }.</summary>
    protected static string IdIn(JsonNode answer) => answer is JsonObject o ? o["id"]?.ToString() : answer?.ToString();

    public virtual async Task UpdateAsync(Context ctx, Existing have, JsonObject want) =>
        await ctx.Api.PostAsync($"api/{Resource}/{Uri.EscapeDataString(have.Id)}", UpdateBody(ctx, have, want));

    /// <summary>Everything Bitween has, with the file's fields over it: updates replace the whole item.</summary>
    protected virtual JsonObject UpdateBody(Context ctx, Existing have, JsonObject want)
    {
        var body = (JsonObject)have.Api.DeepClone();
        foreach (var (key, value) in ToApi(ctx, want)) body[key] = value?.DeepClone();
        return body;
    }

    public virtual async Task DeleteAsync(Context ctx, Existing have) =>
        await ctx.Api.DeleteAsync($"api/{Resource}/{Uri.EscapeDataString(have.Id)}");

    // ------------------------------------------------------------------ helpers

    /// <summary>Every row of a Searchy list, a page at a time.</summary>
    protected static async Task<List<JsonObject>> Searchy(Context ctx, string resource, string filter = null)
    {
        const int size = 500;
        var rows = new List<JsonObject>();
        for (var page = 0; ; page++)
        {
            var answer = await ctx.Api.GetAsync($"api/{resource}?page={page}&size={size}" + (filter == null ? "" : "&filter=" + filter));
            var result = answer?["result"] as JsonArray ?? [];
            rows.AddRange(result.OfType<JsonObject>());
            var total = (int?)answer?["totalCount"] ?? rows.Count;
            if (result.Count < size || rows.Count >= total) return rows;
        }
    }

    /// <summary>The rows of a plain list that answers { result } to a large limit.</summary>
    protected static async Task<List<JsonObject>> Listed(Context ctx, string path) =>
        ((await ctx.Api.GetAsync(path))?["result"] as JsonArray ?? []).OfType<JsonObject>().ToList();

    protected static async Task<List<T>> Parallel<TIn, T>(IEnumerable<TIn> items, Func<TIn, Task<T>> each)
    {
        using var gate = new SemaphoreSlim(8);
        return (await Task.WhenAll(items.Select(async item =>
        {
            await gate.WaitAsync();
            try { return await each(item); }
            finally { gate.Release(); }
        }))).ToList();
    }
}
