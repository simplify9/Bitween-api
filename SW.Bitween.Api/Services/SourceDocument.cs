using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.Bitween.NativeAdapters;
using SW.Bitween.NativeAdapters.Mapper;
using SW.Bitween.NativeAdapters.Mapper.Formats;

namespace SW.Bitween;

/// <summary>
/// Reading source values: the values a response or bus gateway subscription takes from the
/// original document — the input of the exchange whose delivery got the response it runs on.
/// </summary>
/// <remarks>
/// <para>
/// The input, not what that exchange's mapper made of it: the input has its information type's
/// shape, the same for every subscription carrying that type, so a path works whichever of them
/// fed the response, and editing one of their mappings never breaks it. It is also what promoted
/// properties are read from.
/// </para>
/// <para>
/// A path is read the way a mapping reads one — dot-separated, keys matched exactly, never into a
/// list — with the mapper's own JSON and XML readers, so a path the mapping editor shows is a path
/// that works here. The document is read as JSON, or as XML when it starts with <c>&lt;</c>.
/// Anything else — a CSV file, which is a list, or plain text — has no paths, so every value reads
/// as missing.
/// </para>
/// </remarks>
public static class SourceDocument
{
    /// <summary>The most paths <see cref="PathsIn"/> lists, so a huge document can't flood the editor.</summary>
    public const int MaxPaths = 500;

    /// <summary>How much of each value <see cref="PathsIn"/> shows as an example.</summary>
    public const int MaxExampleLength = 100;

    /// <summary>
    /// The source paths a subscription reads: <c>{{source.PATH}}</c> in its handler, and Original
    /// rules in a native mapping.
    /// </summary>
    public static IReadOnlyList<string> PathsUsedBy(Subscription subscription)
    {
        var paths = StartupValuesFiller.SourcePathsIn(subscription.HandlerProperties?.Values).ToList();

        if (string.Equals(subscription.MapperId, nameof(NativeMapper), StringComparison.OrdinalIgnoreCase) &&
            subscription.MapperProperties?.TryGetValue(nameof(NativeMapperInput.MappingRules), out var json) == true &&
            !string.IsNullOrWhiteSpace(json))
        {
            try
            {
                var rules = JsonConvert.DeserializeObject<MappingRules>(json);
                paths.AddRange(rules?.EveryField()
                    .Where(f => f.From.Kind == ValueSourceKind.Source && !string.IsNullOrWhiteSpace(f.From.Path))
                    .Select(f => f.From.Path) ?? []);
            }
            catch (JsonException)
            {
                // Unreadable rules fail every exchange already, with the mapper's own message.
            }
        }

        return paths.Distinct().ToList();
    }

    /// <summary>
    /// The values at <paramref name="paths"/> in <paramref name="document"/>. A path with no value
    /// there, or holding a list or an object, is left out, for whatever reads it to report.
    /// </summary>
    public static Dictionary<string, string> Read(string document, IEnumerable<string> paths)
    {
        var root = Parse(document);
        var values = new Dictionary<string, string>();
        foreach (var path in paths)
            if (Text(Values.ResolveScalar(root, path)) is { } text)
                values[path] = text;
        return values;
    }

    /// <summary>
    /// Every path in <paramref name="document"/> that holds a value, with that value cut to
    /// <see cref="MaxExampleLength"/>, in document order. For the editor to offer.
    /// </summary>
    public static List<SourcePath> PathsIn(string document)
    {
        var paths = new List<SourcePath>();
        Walk(Parse(document), null, paths);
        return paths;
    }

    private static void Walk(ValueNode node, string prefix, List<SourcePath> paths)
    {
        if (node is not ObjectNode obj) return;
        foreach (var (key, child) in obj.Children())
        {
            if (paths.Count >= MaxPaths) return;
            var path = prefix == null ? key : $"{prefix}.{key}";
            if (child is ScalarNode scalar)
            {
                if (Text(scalar.Value) is not { } text) continue;
                paths.Add(new SourcePath { Path = path, Example = Shorten(text) });
            }
            else
                Walk(child, path, paths);
        }
    }

    /// <summary>
    /// A value cut to <see cref="MaxExampleLength"/> for showing, never for using: an example in the
    /// editor, or a value in a list of exchanges.
    /// </summary>
    public static string Shorten(string value) =>
        value?.Length > MaxExampleLength ? value[..MaxExampleLength] + "…" : value;

    private static ValueNode Parse(string document)
    {
        if (string.IsNullOrWhiteSpace(document)) return null;
        var format = DocumentFormats.TryGet(document.TrimStart().StartsWith('<') ? "xml" : "json", out var f)
            ? f
            : null;
        try
        {
            return format?.Read(document);
        }
        catch (DocumentFormatException)
        {
            return null;
        }
    }

    private static string Text(object value) =>
        value != null && Values.TryCoerce(value, NativeAdapters.Mapper.ValueType.String, out var text)
            ? text as string
            : null;
}
