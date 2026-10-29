#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using SW.Bitween.NativeAdapters.Mapper;

namespace SW.Bitween.Services.Ai;

/// <summary>
/// Text a rule writes as a constant that the source sample holds as data.
/// </summary>
/// <remarks>
/// <para>
/// A constant the source doesn't have - a currency, a sender code - is what a fixed value is for.
/// One the source does have is a value that changes from document to document, frozen to match the
/// one sample: <c>first + " Haddad"</c> turns every customer into a Haddad, and still matches the
/// output sample, so the comparison alone would call it right.
/// </para>
/// <para>
/// Whole values only, ignoring case and surrounding spaces, and only text of three characters or
/// more: a constant <c>"1"</c> or <c>"N"</c> turns up in most documents by coincidence.
/// </para>
/// </remarks>
public static class FrozenValues
{
    const int MinLength = 3;

    /// <summary>One difference per frozen value, named the way the mapper names the rule's errors.</summary>
    public static List<MappingDifference> In(MappingRules rules, ValueNode source)
    {
        var data = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Collect(source, data);
        var found = new List<MappingDifference>();
        if (data.Count == 0) return found;

        if (rules.Root is not null) List(rules.Root, "", data, found);
        Fields(rules.Fields, rules.Lists, "", data, found);
        return found;
    }

    static void Fields(IEnumerable<FieldRule> fields, IEnumerable<ListRule> lists, string prefix,
        HashSet<string> data, List<MappingDifference> found)
    {
        foreach (var field in fields) Field(field, Describe(prefix, field.Target), data, found);
        foreach (var list in lists) List(list, prefix, data, found);
    }

    static void List(ListRule list, string prefix, HashSet<string> data, List<MappingDifference> found)
    {
        var key = Describe(prefix, list.Target);
        if (list.Item is not null) Field(list.Item, key, data, found);
        Fields(list.Fields, list.Lists, key, data, found);
        foreach (var entry in list.Fixed.Concat(list.After))
        {
            if (entry.Item is not null) Field(entry.Item, key, data, found);
            Fields(entry.Fields, entry.Lists, key, data, found);
        }
    }

    static void Field(FieldRule field, string target, HashSet<string> data, List<MappingDifference> found)
    {
        if (field.From.Kind == ValueSourceKind.Fixed && Frozen(field.From.Value, data) is { } value)
            found.Add(new(target, $"the fixed value \"{value}\" is in the source sample, so it would be the same for every document; read it from the source"));

        if (field.Transform is { } transform
            && transform.Fn is "concat" or "replace" or "defaultIfEmpty"
            && transform.Args.TryGetValue(transform.Fn == "defaultIfEmpty" ? "value" : "with", out var arg)
            && Frozen(arg, data) is { } text)
            found.Add(new(target, $"the {transform.Fn} text \"{text}\" is in the source sample, so it would be the same for every document; read it from the source"));
    }

    static string? Frozen(object? value, HashSet<string> data)
    {
        var text = (value is JValue jvalue ? jvalue.Value : value) as string;
        text = text?.Trim();
        return text is { Length: >= MinLength } && data.Contains(text) ? text : null;
    }

    static void Collect(ValueNode node, HashSet<string> data)
    {
        switch (node)
        {
            case ScalarNode { Value: string text } when text.Trim().Length >= MinLength:
                data.Add(text.Trim());
                break;
            case ObjectNode obj:
                foreach (var (_, child) in obj.Children()) Collect(child, data);
                break;
            case ListNode list:
                foreach (var item in list.Items) Collect(item, data);
                break;
        }
    }

    /// <summary>As <c>DocumentMapper</c> describes a rule's target, so the editor marks the right row.</summary>
    static string Describe(string prefix, List<string> target)
    {
        var joined = target.Count == 0 ? "(no target)" : string.Join(".", target);
        return prefix.Length == 0 ? joined : $"{prefix}[].{joined}";
    }
}
