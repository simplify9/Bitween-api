using System.Text;
using System.Text.Json.Nodes;

namespace SW.Bitween.Cli.Configuration;

/// <summary>
/// A Bitween's configuration as a file apply takes: names for ids, nothing that only says a default,
/// and each secret Bitween won't show as a ${secret:NAME} to be supplied when it's applied.
/// </summary>
public static class Exporter
{
    public static async Task<(string Yaml, List<string> Secrets, int Count)> ExportAsync(Context ctx, string name, ICollection<string> only = null)
    {
        await new Planner(ctx, null).ReadAsync();

        var root = new JsonObject
        {
            ["apiVersion"] = ConfigFile.ApiVersion,
            ["kind"] = ConfigFile.Kind,
            ["metadata"] = new JsonObject { ["name"] = name },
        };
        var secrets = new List<string>();
        var count = 0;
        foreach (var kind in Kinds.All.Where(k => only == null || only.Count == 0 || only.Contains(k.Section)))
        {
            var items = ctx.Current[kind.Section].Where(e => e.Key != null).OrderBy(e => e.Key, StringComparer.Ordinal).ToList();
            if (items.Count == 0) continue;
            root[kind.Section] = new JsonArray(items.Select(e =>
            {
                var file = Values.Tidy((JsonObject)e.File.DeepClone());
                if (kind is Partners && file["keys"] is JsonObject keys)
                    foreach (var key in keys.Select(k => k.Key).ToList())
                        keys[key] = Secret(secrets, kind, e.Key, "key", key);
                return (JsonNode)Unmask(file, secrets, kind, e.Key, []);
            }).ToArray());
            count += items.Count;
        }
        return (Yaml.Write(root), secrets, count);
    }

    static JsonNode Unmask(JsonNode node, List<string> secrets, Kind kind, string key, List<string> path)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var name in obj.Select(p => p.Key).ToList())
                    obj[name] = Unmask(obj[name]?.DeepClone(), secrets, kind, key, [.. path, name]);
                return obj;
            case JsonArray list:
                return new JsonArray(list.Select((x, i) => Unmask(x?.DeepClone(), secrets, kind, key, [.. path, (i + 1).ToString()])).ToArray());
            default:
                return Values.IsMasked(node) ? Secret(secrets, kind, key, path.Where(p => p != "properties").ToArray()) : node;
        }
    }

    static JsonNode Secret(List<string> secrets, Kind kind, string key, params string[] path)
    {
        var name = Name([kind.Noun, key, .. path]);
        var unique = name;
        for (var n = 2; secrets.Contains(unique); n++) unique = $"{name}_{n}";
        secrets.Add(unique);
        return $"${{secret:{unique}}}";
    }

    /// <summary>"partner", "Acme Ltd", "key", "main" as PARTNER_ACME_LTD_KEY_MAIN.</summary>
    public static string Name(IEnumerable<string> parts)
    {
        var text = new StringBuilder();
        foreach (var c in string.Join("_", parts).ToUpperInvariant())
        {
            var keep = char.IsAsciiLetterOrDigit(c) ? c : '_';
            if (keep == '_' && (text.Length == 0 || text[^1] == '_')) continue;
            text.Append(keep);
        }
        return text.ToString().TrimEnd('_');
    }
}
