using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;

namespace SW.Bitween.Services.Mirror;

/// <summary>
/// Compares what a rehearsed exchange did on staging with what prod's did, as staging keeps it: faked.
/// Both went through the same faker, so a value staging copied from its input reads as prod's did, and
/// one a mapping works out from faked values reads differently but keeps its look. So a difference is:
/// <list type="bullet">
/// <item><b>different</b> when how it ended changed, or the file's format, its fields, how many of them,
/// or how a value is written: a date in another format, a number turned text, a value left empty;</item>
/// <item><b>check</b> when only a value's text changed and its look didn't, which faked data explains
/// when a mapping works values out (a sum, a cut, a lookup) - and a wrong field taken doesn't;</item>
/// <item><b>same</b> otherwise.</item>
/// </list>
/// </summary>
public static class MirrorCompare
{
    public const string Different = "different", Check = "check", Same = "same", Info = "info", Faked = "faked";

    /// <summary>Fields listed per file: the ones that differ first.</summary>
    const int MaxFields = 40;

    const int MaxValue = 120;

    /// <summary>How a rehearsed exchange compares: by how it ended and what it sent and got back. What it used only explains.</summary>
    public static string Verdict(IEnumerable<MirrorDiff> diffs)
    {
        var levels = diffs.Where(d => d.Part is "result" or "output" or "answer").Select(d => d.Level).ToList();
        return levels.Contains(Different) ? Different : levels.Contains(Check) ? Check : Same;
    }

    // ------------------------------------------------------------------ how it ended

    static readonly HashSet<string> Unreached = new(StringComparer.Ordinal)
    {
        "HttpRequestException", "SocketException", "IOException", "TimeoutException", "TaskCanceledException",
        "OperationCanceledException", "AuthenticationException", "WebException",
    };

    /// <summary>An error that says the call never got through: refused, cut, timed out.</summary>
    internal static bool Unreachable(IEnumerable<string> types) => types.Any(Unreached.Contains);

    public static MirrorDiff Result(MirrorSample prod, string outcome, int? status, IReadOnlyList<string> errorTypes)
    {
        var prodTypes = prod.ErrorTypes ?? [];
        errorTypes ??= [];
        var level = prod.Outcome != outcome ? Different
            : outcome == "failed" && (prod.Status != status || !SameFailure(prodTypes, errorTypes)) ? Check
            : Same;
        var note = level switch
        {
            Different when prod.Outcome == "success" => "It worked on prod and doesn't on staging.",
            Different when outcome == "success" => "It works on staging where it didn't on prod.",
            Check => "Both failed, in different ways.",
            _ => null,
        };
        return new MirrorDiff("result", "Result", level, Ending(prod.Outcome, prod.Status, prodTypes), Ending(outcome, status, errorTypes), note);
    }

    /// <summary>The same failure, or both a call that never got through - which is how staging replays one of prod's that didn't.</summary>
    static bool SameFailure(IReadOnlyList<string> prod, IReadOnlyList<string> staging) =>
        prod.SequenceEqual(staging) || Unreachable(prod) && Unreachable(staging);

    internal static string Ending(string outcome, int? status, IReadOnlyList<string> types) => outcome switch
    {
        "success" => "Worked",
        "badResponse" => "Answered badly",
        "failed" => string.Join(" · ", new[] { "Failed", status is { } s ? $"HTTP {s}" : null, types.Count > 0 ? string.Join(" → ", types) : null }
            .Where(p => p != null)),
        _ => "Didn't finish",
    };

    // ------------------------------------------------------------------ files

    /// <summary>A leaf value of a file: where it is, the field it is a value of, and what kind of value.</summary>
    internal record Leaf(string Field, string Path, string Value, string Kind);

    /// <summary>How many times something repeats in one place: a list's items, an element's same-named children, a file's rows.</summary>
    internal record Count(string Field, string Path, int N);

    internal record Read(string Format, List<Leaf> Leaves, List<Count> Counts);

    /// <summary>
    /// One of the exchange's files, prod's beside staging's: <paramref name="part"/> is output or answer.
    /// Null when neither has one.
    /// </summary>
    public static MirrorDiff File(string part, string name, MirrorSampleFile prodFile, byte[] prod, byte[] staging)
    {
        if (prodFile == null && staging == null) return null;
        if (prodFile == null)
            return new MirrorDiff(part, name, Different, "None", Describe(Parse(staging)), "Staging made one where prod made none.");
        if (prodFile.Key == null || prod == null)
            return new MirrorDiff(part, name, Info, prodFile.Format == null ? "Not kept" : $"Not kept ({FormatName(prodFile.Format)})",
                staging == null ? "None" : Describe(Parse(staging)),
                $"Prod's isn't kept, so it isn't compared. {prodFile.NotCopied}".Trim());
        var a = Parse(prod);
        if (staging == null)
            return new MirrorDiff(part, name, Different, Describe(a), "None", "Prod made one; staging didn't.");
        var b = Parse(staging);
        if (a.Format != b.Format)
            return new MirrorDiff(part, name, Different, Describe(a), Describe(b), $"Written as {FormatName(b.Format)} where prod's was {FormatName(a.Format)}.");
        if (a.Leaves == null)
            return prod.AsSpan().SequenceEqual(staging)
                ? new MirrorDiff(part, name, Same, Describe(a), Describe(b))
                : new MirrorDiff(part, name, Check, Describe(a), Describe(b), $"{FormatName(a.Format)} files aren't compared value by value yet: open both to compare.");

        var (level, note, fields) = Compare(a, b);
        return new MirrorDiff(part, name, level, Describe(a), Describe(b), note, fields);
    }

    internal static (string Level, string Note, List<MirrorFieldDiff> Fields) Compare(Read prod, Read staging)
    {
        var fields = new List<MirrorFieldDiff>();
        int missing = 0, added = 0, counted = 0, written = 0, changed = 0;

        // How often things repeat: a list two items shorter shows once, not once per field of its items.
        var prodCounts = prod.Counts.GroupBy(c => c.Field).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        foreach (var group in staging.Counts.GroupBy(c => c.Field))
        {
            if (!prodCounts.TryGetValue(group.Key, out var p)) continue;
            foreach (var (pc, sc) in p.Zip(group))
                if (pc.N != sc.N)
                {
                    counted++;
                    fields.Add(new MirrorFieldDiff(sc.Path, Different, Times(pc.N, prod.Format), Times(sc.N, staging.Format)));
                }
        }

        var listsDiffer = counted > 0;
        var prodBy = prod.Leaves.GroupBy(l => l.Field).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var stagingBy = staging.Leaves.GroupBy(l => l.Field).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var order = prod.Leaves.Select(l => l.Field).Concat(staging.Leaves.Select(l => l.Field)).Distinct(StringComparer.Ordinal);
        foreach (var field in order)
        {
            var inProd = prodBy.TryGetValue(field, out var p);
            var inStaging = stagingBy.TryGetValue(field, out var s);
            if (!inStaging)
            {
                missing++;
                fields.Add(new MirrorFieldDiff(p[0].Path, Different, Cut(p[0].Value), "Missing"));
                continue;
            }
            if (!inProd)
            {
                added++;
                fields.Add(new MirrorFieldDiff(s[0].Path, Different, "Missing", Cut(s[0].Value)));
                continue;
            }
            // Repeats are already said by the counts above; a field missing from some of a list's items isn't.
            if (!listsDiffer && p.Count != s.Count)
            {
                counted++;
                fields.Add(new MirrorFieldDiff(s[0].Path, Different, Times(p.Count, prod.Format), Times(s.Count, staging.Format)));
            }
            foreach (var (pv, sv) in p.Zip(s))
            {
                if (pv.Kind == sv.Kind && pv.Value == sv.Value) continue;
                if (pv.Kind != sv.Kind || Look(pv.Value) != Look(sv.Value))
                {
                    written++;
                    fields.Add(new MirrorFieldDiff(sv.Path, Different, Shown(pv), Shown(sv)));
                }
                else
                {
                    changed++;
                    fields.Add(new MirrorFieldDiff(sv.Path, Check, Shown(pv), Shown(sv)));
                }
            }
        }

        var level = missing + added + counted + written > 0 ? Different : changed > 0 ? Check : Same;
        var parts = new[]
        {
            missing > 0 ? $"{Many(missing, "field")} missing" : null,
            added > 0 ? $"{Many(added, "new field")}" : null,
            counted > 0 ? $"{Many(counted, "list")} of another length" : null,
            written > 0 ? $"{Many(written, "value")} written another way" : null,
            changed > 0 ? $"{Many(changed, "value")} with other text but the same look, which fake data can explain" : null,
        }.Where(x => x != null).ToList();
        var listed = fields.OrderBy(f => f.Level == Different ? 0 : 1).Take(MaxFields).ToList();
        var note = parts.Count == 0 ? null : string.Join("; ", parts) + "." + (fields.Count > MaxFields ? $" The first {MaxFields} are listed." : "");
        return (level, note is null ? null : char.ToUpperInvariant(note[0]) + note[1..], listed);
    }

    internal static string Many(int n, string noun) => $"{n} {noun}{(n == 1 ? "" : "s")}";

    static string Times(int n, string format) => format switch
    {
        "csv" => Many(n, "row"),
        "text" or "html" => Many(n, "line"),
        _ => Many(n, "item"),
    };

    static string Shown(Leaf leaf) => leaf.Kind switch
    {
        "string" => $"\"{Cut(leaf.Value)}\"",
        _ => Cut(leaf.Value),
    };

    static string Cut(string value) => value == null ? null : value.Length <= MaxValue ? value : value[..MaxValue] + "…";

    /// <summary>
    /// How a value looks: letters as A or a, digits as 9, the rest as it is, each run as one. Faking keeps it
    /// (2026-03-14 and 2031-07-22 both look 9-9-9), and a change in how a value is written changes it.
    /// </summary>
    internal static string Look(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var look = new StringBuilder();
        foreach (var c in value)
        {
            var k = char.IsDigit(c) ? '9' : char.IsUpper(c) ? 'A' : char.IsLetter(c) ? 'a' : char.IsWhiteSpace(c) ? ' ' : c;
            if (look.Length == 0 || look[^1] != k || k is not ('9' or 'A' or 'a' or ' ')) look.Append(k);
        }
        return look.ToString();
    }

    // ------------------------------------------------------------------ reading a file

    internal static Read Parse(byte[] content)
    {
        var format = MirrorFiles.FormatOf(content);
        var leaves = new List<Leaf>();
        var counts = new List<Count>();
        try
        {
            switch (format)
            {
                case "json":
                    Json(JsonNode.Parse(MirrorFiles.TextOf(content)), "", "", leaves, counts);
                    break;
                case "xml":
                    using (var reader = XmlReader.Create(new MemoryStream(content), new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null }))
                    {
                        var root = XDocument.Load(reader).Root;
                        if (root != null) Xml(root, root.Name.LocalName, root.Name.LocalName, leaves, counts);
                    }
                    break;
                case "csv":
                    var text = MirrorFiles.TextOf(content);
                    var rows = MirrorFiles.Csv.Rows(text, MirrorFiles.Csv.Delimiter(text) ?? ',');
                    counts.Add(new Count("rows", "Rows", rows.Count));
                    for (var r = 0; r < rows.Count; r++)
                    for (var c = 0; c < rows[r].Count; c++)
                        leaves.Add(new Leaf($"column {c + 1}", $"row {r + 1} · column {c + 1}", rows[r][c], "text"));
                    break;
                case "text":
                case "html":
                    var lines = MirrorFiles.TextOf(content).Split('\n').Select(l => l.TrimEnd('\r')).ToList();
                    if (lines.Count > 1 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
                    counts.Add(new Count("lines", "Lines", lines.Count));
                    for (var i = 0; i < lines.Count; i++) leaves.Add(new Leaf("line", $"line {i + 1}", lines[i], "text"));
                    break;
                default:
                    return new Read(format, null, null);
            }
        }
        catch (Exception ex) when (ex is JsonException or XmlException or DecoderFallbackException or InvalidOperationException)
        {
            return new Read(format, null, null);
        }
        return new Read(format, leaves, counts);
    }

    static void Json(JsonNode node, string path, string field, List<Leaf> leaves, List<Count> counts)
    {
        var at = path.Length == 0 ? "(the whole file)" : path;
        switch (node)
        {
            case null:
                leaves.Add(new Leaf(field, at, "null", "null"));
                break;
            case JsonObject { Count: 0 }:
                leaves.Add(new Leaf(field, at, "{}", "object"));
                break;
            case JsonObject o:
                foreach (var (key, value) in o)
                    Json(value, path.Length == 0 ? key : $"{path}.{key}", field.Length == 0 ? key : $"{field}.{key}", leaves, counts);
                break;
            case JsonArray a:
                counts.Add(new Count(field + "[]", at, a.Count));
                if (a.Count == 0) leaves.Add(new Leaf(field + "[]", at, "[]", "array"));
                for (var i = 0; i < a.Count; i++) Json(a[i], $"{path}[{i}]", field + "[]", leaves, counts);
                break;
            case JsonValue v:
                var kind = v.GetValueKind();
                leaves.Add(new Leaf(field, at, kind == JsonValueKind.String ? v.GetValue<string>() : v.ToJsonString(), kind switch
                {
                    JsonValueKind.String => "string",
                    JsonValueKind.Number => "number",
                    JsonValueKind.True or JsonValueKind.False => "bool",
                    _ => "null",
                }));
                break;
        }
    }

    static void Xml(XElement element, string path, string field, List<Leaf> leaves, List<Count> counts)
    {
        foreach (var attribute in element.Attributes().Where(a => !a.IsNamespaceDeclaration))
            leaves.Add(new Leaf($"{field}/@{attribute.Name.LocalName}", $"{path}/@{attribute.Name.LocalName}", attribute.Value, "text"));
        var children = element.Elements().ToList();
        if (children.Count == 0)
        {
            leaves.Add(new Leaf(field, path, element.Value, "text"));
            return;
        }
        var named = children.GroupBy(c => c.Name.LocalName).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        foreach (var (name, n) in named) counts.Add(new Count($"{field}/{name}", $"{path}/{name}", n));
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var child in children)
        {
            var name = child.Name.LocalName;
            var i = seen[name] = seen.GetValueOrDefault(name) + 1;
            Xml(child, named[name] > 1 ? $"{path}/{name}[{i}]" : $"{path}/{name}", $"{field}/{name}", leaves, counts);
        }
    }

    static string Describe(Read read) => read.Leaves == null ? FormatName(read.Format) : read.Format switch
    {
        "csv" => $"CSV · {Many(read.Counts[0].N, "row")}",
        "text" or "html" => $"{FormatName(read.Format)} · {Many(read.Counts[0].N, "line")}",
        _ => $"{FormatName(read.Format)} · {Many(read.Leaves.Select(l => l.Field).Distinct(StringComparer.Ordinal).Count(), "field")}",
    };

    internal static string FormatName(string format) => format switch
    {
        "json" => "JSON",
        "xml" => "XML",
        "csv" => "CSV",
        "html" => "HTML",
        "xlsx" => "Excel",
        "text" => "Text",
        "docx" => "Word",
        null => "A file",
        _ => format.ToUpperInvariant(),
    };

    // ------------------------------------------------------------------ setup

    /// <summary>
    /// A setup item's fields, prod's beside staging's, where they differ: nested values field by field
    /// (handler · properties · Url), so a long block shows only what changed in it.
    /// </summary>
    internal static List<MirrorFieldDiff> SetupFields(JsonObject want, JsonObject have, IEnumerable<string> fields)
    {
        var diffs = new List<MirrorFieldDiff>();
        foreach (var field in fields)
        {
            var a = new List<Leaf>();
            var b = new List<Leaf>();
            Json(want?[field]?.DeepClone(), field, field, a, []);
            Json(have?[field]?.DeepClone(), field, field, b, []);
            var prodBy = a.GroupBy(l => l.Path).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            var stagingBy = b.GroupBy(l => l.Path).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            foreach (var path in prodBy.Keys.Concat(stagingBy.Keys).Distinct(StringComparer.Ordinal))
            {
                prodBy.TryGetValue(path, out var p);
                stagingBy.TryGetValue(path, out var s);
                if (p != null && s != null && p.Kind == s.Kind && p.Value == s.Value) continue;
                diffs.Add(new MirrorFieldDiff(path.Replace(".", " · "), Info, p == null ? "Not set" : Shown(p), s == null ? "Not set" : Shown(s)));
            }
        }
        return diffs.Take(MaxFields).ToList();
    }
}
