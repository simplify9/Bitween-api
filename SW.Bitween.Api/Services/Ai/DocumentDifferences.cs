#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SW.Bitween.NativeAdapters.Mapper;

namespace SW.Bitween.Services.Ai;

/// <summary>
/// Where a mapped document differs from the one it should have been, in words a person - or a
/// model asked to fix the rules - can act on.
/// </summary>
/// <remarks>
/// <para>
/// Compared as trees rather than as text, so whitespace, key order and number formatting don't
/// count. Types do: <c>42</c> where <c>"42"</c> was wanted is a difference a partner's parser
/// notices.
/// </para>
/// <para>
/// A blank value in the output sample - null, empty or only whitespace - is a placeholder: output
/// samples are often a partner's template with the values left out, and any value the rules write
/// there is an answer, not a difference. The field itself still has to be written.
/// </para>
/// </remarks>
public static class DocumentDifferences
{
    /// <summary>Enough to fix the rules from; a wholly wrong document would otherwise list every field.</summary>
    public const int Limit = 20;

    public static List<MappingDifference> Between(ValueNode expected, ValueNode actual)
    {
        var found = new List<MappingDifference>();
        Compare(expected, actual, "", found);
        return found;
    }

    static void Compare(ValueNode? expected, ValueNode? actual, string path, List<MappingDifference> found)
    {
        if (found.Count >= Limit) return;
        // Named the way the mapper names a failing rule's document, so the editor reads both alike.
        var at = path.Length == 0 ? "(root)" : path;

        if (expected is null && actual is null) return;
        if (expected is null)
        {
            found.Add(new(at, $"not in the output sample, but the rules write {Describe(actual!)}"));
            return;
        }
        if (actual is null)
        {
            found.Add(new(at, $"missing; the output sample has {Describe(expected)}"));
            return;
        }
        if (IsPlaceholder(expected) && actual is ScalarNode) return;
        if (expected.Kind != actual.Kind)
        {
            found.Add(new(at, $"the output sample has {Describe(expected)}, the rules write {Describe(actual)}"));
            return;
        }

        switch (expected)
        {
            case ScalarNode e when !Same(e.Value, ((ScalarNode)actual).Value):
                found.Add(new(at, $"the output sample has {Describe(e)}, the rules write {Describe(actual)}"));
                break;

            case ObjectNode e:
                var a = (ObjectNode)actual;
                foreach (var key in e.Keys) Compare(e[key], a[key], Join(path, key), found);
                foreach (var key in a.Keys.Where(k => e[k] is null)) Compare(null, a[key], Join(path, key), found);
                break;

            case ListNode e:
                var items = ((ListNode)actual).Items;
                if (e.Items.Count != items.Count)
                    found.Add(new(at, $"the output sample has {e.Items.Count} entries, the rules write {items.Count}"));
                for (var i = 0; i < Math.Min(e.Items.Count, items.Count); i++)
                    Compare(e.Items[i], items[i], $"{path}[{i}]", found);
                break;
        }
    }

    static string Join(string path, string key) => path.Length == 0 ? key : $"{path}.{key}";

    static bool IsPlaceholder(ValueNode node) =>
        node is ScalarNode scalar && (scalar.Value is null || scalar.Value is string text && string.IsNullOrWhiteSpace(text));

    static bool Same(object? expected, object? actual) => (expected, actual) switch
    {
        (null, null) => true,
        (decimal e, decimal a) => e == a,
        (string e, string a) => string.Equals(e, a, StringComparison.Ordinal),
        (bool e, bool a) => e == a,
        _ => false,
    };

    static string Describe(ValueNode node) => node switch
    {
        ScalarNode { Value: null } => "null",
        ScalarNode { Value: string s } => $"the text {Quote(s)}",
        ScalarNode { Value: decimal d } => $"the number {d.ToString(CultureInfo.InvariantCulture)}",
        ScalarNode { Value: bool b } => b ? "true" : "false",
        ObjectNode => "an object",
        ListNode l => $"a list of {l.Items.Count}",
        _ => "a value",
    };

    static string Quote(string text) => text.Length <= 60 ? $"\"{text}\"" : $"\"{text[..57]}...\"";
}

/// <summary>One place a mapped document differs from its sample: a path such as <c>items[0].sku</c>, and how.</summary>
public sealed record MappingDifference(string Target, string Reason)
{
    public override string ToString() => $"{Target}: {Reason}";
}
