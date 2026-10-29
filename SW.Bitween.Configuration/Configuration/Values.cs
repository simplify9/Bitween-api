using System.Globalization;
using System.Text.Json.Nodes;

namespace SW.Bitween.Cli.Configuration;

/// <summary>How the file's values and the API's are compared and converted.</summary>
public static class Values
{
    /// <summary>What Bitween answers in place of a secret it holds. Sent back, it means "keep what you have".</summary>
    public const string Masked = "__private__";

    /// <summary>
    /// Whether what the file asks for is what Bitween has. Only what the file says is compared: a key
    /// it leaves out of a mapping is left as it is (with <paramref name="exactKeys"/>, at the top level of
    /// a mapping that replaces everything, it must say every key). A secret Bitween won't show is taken
    /// to match: there's no way to tell, and it is sent again whenever anything else changes.
    /// </summary>
    public static bool Same(JsonNode want, JsonNode have, bool exactKeys = false)
    {
        if (IsMasked(have)) return true;
        if (Empty(want) && Empty(have)) return true;

        switch (want)
        {
            case JsonObject w when have is JsonObject h:
                if (exactKeys && h.Any(p => !w.ContainsKey(p.Key) && !Empty(p.Value))) return false;
                return w.All(p => Same(p.Value, h[p.Key]));
            case JsonArray w when have is JsonArray h:
                return w.Count == h.Count && w.Zip(h).All(x => Same(x.First, x.Second));
            case JsonValue w when have is JsonValue h:
                return Text(w) == Text(h) || (Number(w) is { } a && Number(h) is { } b && a == b);
            default:
                return false;
        }
    }

    public static bool IsMasked(JsonNode node) => node is JsonValue v && v.TryGetValue<string>(out var s) && s == Masked;

    public static bool Empty(JsonNode node) => node switch
    {
        null => true,
        JsonArray a => a.Count == 0,
        JsonObject o => o.Count == 0,
        JsonValue v when v.TryGetValue<string>(out var s) => s.Length == 0,
        _ => false,
    };

    static string Text(JsonValue v) => v.TryGetValue<string>(out var s) ? s
        : v.TryGetValue<bool>(out var b) ? (b ? "true" : "false")
        : v.ToJsonString();

    static decimal? Number(JsonValue v) =>
        decimal.TryParse(v.TryGetValue<string>(out var s) ? s : v.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;

    /// <summary>[{key, value}] as the file writes it: a mapping.</summary>
    public static JsonObject ToMap(JsonNode keyValues)
    {
        var map = new JsonObject();
        if (keyValues is JsonArray list)
            foreach (var item in list.OfType<JsonObject>())
                if ((string)item["key"] is { } key) map[key] = item["value"]?.DeepClone();
        return map;
    }

    /// <summary>A mapping as the API takes it: [{key, value}], every value a string.</summary>
    public static JsonArray ToKeyValues(JsonNode map) =>
        new((map as JsonObject ?? []).Select(p => (JsonNode)new JsonObject
        {
            ["key"] = p.Key,
            ["value"] = AsString(p.Value),
        }).ToArray());

    /// <summary>A mapping whose values are all strings, as the API's dictionaries are.</summary>
    public static JsonObject ToStrings(JsonNode map) =>
        new((map as JsonObject ?? []).Select(p => KeyValuePair.Create(p.Key, (JsonNode)AsString(p.Value))));

    static JsonValue AsString(JsonNode value) => value switch
    {
        null => null,
        JsonValue v when v.TryGetValue<string>(out var s) => JsonValue.Create(s),
        JsonValue v => JsonValue.Create(Text(v)),
        _ => JsonValue.Create(value.ToJsonString()),
    };

    /// <summary>Drops what says nothing (null, false, empty), so an exported file reads as what was set.</summary>
    public static JsonObject Tidy(JsonObject obj)
    {
        foreach (var key in obj.Select(p => p.Key).ToList())
            if (Empty(obj[key]) || obj[key] is JsonValue v && v.TryGetValue<bool>(out var b) && !b)
                obj.Remove(key);
        return obj;
    }

    /// <summary>A value as shown in a plan: secrets as (secret), long text shortened.</summary>
    public static string Show(JsonNode node, ISet<string> secrets)
    {
        if (node is JsonValue v && v.TryGetValue<string>(out var s))
        {
            if (secrets.Contains(s) || s == Masked) return "(secret)";
            return s.Length > 60 || s.Contains('\n') ? $"({s.Length} characters)" : s;
        }
        var text = node?.ToJsonString() ?? "null";
        foreach (var secret in secrets) text = text.Replace(secret, "(secret)");
        return text.Length > 80 ? text[..77] + "..." : text;
    }
}
