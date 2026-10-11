using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace SW.Bitween.Cli.Configuration;

/// <summary>
/// YAML in and out, by way of JSON nodes: the API speaks JSON, so everything past the file is JSON
/// and only reading and writing the file knows it was YAML.
/// </summary>
public static class Yaml
{
    /// <summary>Every document in the text, as JSON. Unquoted scalars become numbers, booleans and nulls.</summary>
    public static List<JsonNode> Read(string text)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(text));
        return stream.Documents.Select(d => ToJson(d.RootNode)).ToList();
    }

    static JsonNode ToJson(YamlNode node) => node switch
    {
        YamlMappingNode map => new JsonObject(map.Children.Select(c =>
            KeyValuePair.Create(((YamlScalarNode)c.Key).Value ?? "", ToJson(c.Value)))),
        YamlSequenceNode list => new JsonArray(list.Children.Select(ToJson).ToArray()),
        YamlScalarNode scalar => Scalar(scalar),
        _ => null,
    };

    static JsonNode Scalar(YamlScalarNode scalar)
    {
        var value = scalar.Value;
        if (scalar.Style is not ScalarStyle.Plain) return JsonValue.Create(value);
        switch (value)
        {
            case null or "" or "~" or "null" or "Null" or "NULL": return null;
            case "true" or "True" or "TRUE": return JsonValue.Create(true);
            case "false" or "False" or "FALSE": return JsonValue.Create(false);
        }
        if (long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var whole))
            return JsonValue.Create(whole);
        if (value.Any(c => c is '.' or 'e' or 'E') &&
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var fraction) && double.IsFinite(fraction))
            return JsonValue.Create(fraction);
        return JsonValue.Create(value);
    }

    /// <summary>JSON as YAML: block style, strings quoted only where they'd read as something else.</summary>
    public static string Write(JsonNode node)
    {
        var text = new StringBuilder();
        WriteNode(text, node, 0, inList: false);
        return text.ToString();
    }

    static void WriteNode(StringBuilder text, JsonNode node, int indent, bool inList)
    {
        switch (node)
        {
            case JsonObject obj when obj.Count > 0:
                var first = true;
                foreach (var (key, value) in obj)
                {
                    if (!(first && inList)) text.Append(' ', indent);
                    first = false;
                    text.Append(Key(key)).Append(':');
                    WriteValue(text, value, indent);
                }
                break;
            case JsonArray list when list.Count > 0:
                foreach (var item in list)
                {
                    text.Append(' ', indent).Append('-');
                    if (item is JsonArray { Count: > 0 })
                    {
                        text.Append('\n');
                        WriteNode(text, item, indent + 2, inList: false);
                    }
                    else if (item is JsonObject { Count: > 0 })
                    {
                        text.Append(' ');
                        WriteNode(text, item, indent + 2, inList: true);
                    }
                    else text.Append(' ').Append(Inline(item)).Append('\n');
                }
                break;
            default:
                text.Append(Inline(node)).Append('\n');
                break;
        }
    }

    static void WriteValue(StringBuilder text, JsonNode value, int indent)
    {
        switch (value)
        {
            case JsonObject { Count: > 0 }:
                text.Append('\n');
                WriteNode(text, value, indent + 2, inList: false);
                break;
            case JsonArray { Count: > 0 }:
                text.Append('\n');
                WriteNode(text, value, indent, inList: false);
                break;
            case JsonValue v when v.TryGetValue<string>(out var s) && s.Contains('\n'):
                // Schemas and SQL read best as they were written.
                text.Append(" |").Append(s.EndsWith('\n') ? "" : "-").Append('\n');
                foreach (var line in s.TrimEnd('\n').Split('\n'))
                    text.Append(line.Length == 0 ? "" : new string(' ', indent + 2) + line.TrimEnd('\r')).Append('\n');
                break;
            default:
                text.Append(' ').Append(Inline(value)).Append('\n');
                break;
        }
    }

    static string Inline(JsonNode node) => node switch
    {
        null => "null",
        JsonObject => "{}",
        JsonArray => "[]",
        JsonValue v when v.TryGetValue<string>(out var s) => Quote(s),
        JsonValue v when v.TryGetValue<bool>(out var b) => b ? "true" : "false",
        _ => node.ToJsonString(),
    };

    static string Key(string key) => Plain(key) ? key : Quote(key, force: true);

    static string Quote(string s, bool force = false)
    {
        if (!force && Plain(s) && Scalar(new YamlScalarNode(s) { Style = ScalarStyle.Plain }) is JsonValue v && v.TryGetValue<string>(out _)) return s;
        return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\t", "\\t").Replace("\r", "\\r") + "\"";
    }

    /// <summary>Safe to write without quotes: nothing YAML would read as structure.</summary>
    static bool Plain(string s) =>
        s.Length > 0 && !char.IsWhiteSpace(s[0]) && !char.IsWhiteSpace(s[^1]) &&
        "-?:,[]{}#&*!|>'\"%@`".IndexOf(s[0]) < 0 && !s.Contains(": ") && !s.Contains(" #") && !s.EndsWith(':') &&
        s.All(c => !char.IsControl(c));
}
