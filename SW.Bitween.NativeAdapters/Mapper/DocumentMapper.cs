using System.Globalization;

namespace SW.Bitween.NativeAdapters.Mapper;

/// <summary>
/// Applies rules to a source document tree and produces an output document tree.
/// </summary>
/// <remarks>
/// <para>
/// Knows nothing about JSON, XML or CSV: a reader turned the input into a <see cref="ValueNode"/>
/// and a writer turns the result back into text. A new format therefore costs a reader and a
/// writer, not a change here.
/// </para>
/// <para>
/// A pure function, which is the point — it is tested by asserting on the tree it returns, with no
/// format and no document text involved. That is only possible because it builds its own neutral
/// tree rather than writing into a format's builder as it goes.
/// </para>
/// </remarks>
public static class DocumentMapper
{
    /// <summary>
    /// Where a rule reads from: the entry it sits in, and the document that entry came from.
    /// </summary>
    /// <remarks>
    /// Both are needed because a rule inside a list may want either — <c>sku</c> from the line, or
    /// the order's reference from the top of the document. Carrying them together keeps the two
    /// from drifting apart as they are passed down through nested lists.
    /// </remarks>
    private readonly record struct Scope(ValueNode? Root, ValueNode? Current)
    {
        /// <summary>The same document, now positioned on one entry of a list.</summary>
        public Scope Enter(ValueNode? item) => new(Root, item);
    }

    /// <summary>
    /// What the mapper needs to know about the source document beyond the document itself.
    /// </summary>
    /// <remarks>
    /// Both are facts about the whole source rather than about any one rule — how the partner
    /// writes dates, and whether their format can tell a list of one from a single value — so they
    /// travel together instead of widening four signatures again for the next one.
    /// </remarks>
    private readonly record struct SourceTraits(DateOrder DateOrder, bool SingleValueIsAList);

    /// <summary>
    /// Maps <paramref name="source"/> according to <paramref name="rules"/>.
    /// </summary>
    /// <exception cref="MappingFailedException">
    /// When any rule could not be applied. Every rule is attempted first, so the exception names
    /// all of them rather than stopping at the first.
    /// </exception>
    /// <param name="singleValueIsAList">
    /// Whether the source format makes a list by repeating a name, so that a single occurrence
    /// and a value that is not a list at all are the same document. True for XML. Defaulted
    /// because JSON says which, and every mapping was JSON until XML arrived.
    /// </param>
    public static ValueNode Map(MappingRules rules, ValueNode? source, MappingContext context,
        bool singleValueIsAList = false)
    {
        var errors = new List<MappingError>();
        var traits = new SourceTraits(rules.SourceDateOrder, singleValueIsAList);

        var scope = new Scope(source, source);

        ValueNode output;
        if (rules.Root is not null)
        {
            // A document is a list or an object, never both — so rules for the other shape are
            // not merely unused, they are configuration nobody will ever see take effect. The
            // editor puts them aside when the shape is switched; rules written by hand can carry
            // both, and dropping them silently is the kind of missing data nobody goes looking for.
            if (rules.Fields.Count > 0 || rules.Lists.Count > 0)
                errors.Add(new MappingError("(root)",
                    "the whole output is a list, so the top-level fields and lists cannot be written"));

            output = BuildList(rules.Root, scope, context, traits, errors, path: "");
        }
        else
        {
            var obj = ValueNode.Object();
            MapInto(obj, rules.Fields, rules.Lists, scope, context, traits, errors, path: "");
            output = obj;
        }

        if (errors.Count > 0) throw new MappingFailedException(errors);
        return output;
    }

    /// <summary>
    /// Applies one level of fields and lists, reading against <paramref name="scope"/>.
    /// </summary>
    private static void MapInto(
        ObjectNode output,
        List<FieldRule> fields,
        List<ListRule> lists,
        Scope scope,
        MappingContext context,
        SourceTraits traits,
        List<MappingError> errors,
        string path,
        int? written = null)
    {
        foreach (var field in fields)
        {
            var target = Describe(path, field.Target);

            if (field.Target.Count == 0)
            {
                errors.Add(new MappingError(target, "rule has no target"));
                continue;
            }

            if (!TryResolveField(field, scope, context, traits, written, out var value, out var reason))
            {
                errors.Add(new MappingError(target, reason!));
                continue;
            }

            Values.PlaceAt(output, field.Target, ValueNode.Value(value));
        }

        foreach (var rule in lists)
        {
            var target = Describe(path, rule.Target);

            if (rule.Target.Count == 0)
            {
                errors.Add(new MappingError(target, "list has no target"));
                continue;
            }

            Values.PlaceAt(output, rule.Target, BuildList(rule, scope, context, traits, errors, path));
        }
    }

    /// <summary>
    /// Builds the list a rule produces: the entries written before it, one per entry of the source
    /// list it walks, then the entries written after.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The order is the feature. A header line belongs at the top and a trailer at the bottom, and
    /// a partner's file routinely ends with a record saying how many came before it.
    /// </para>
    /// <para>
    /// Which is why the source entries are matched before anything is built. The number of rows is
    /// then the same wherever <see cref="ValueSourceKind.Count"/> is read from — the header, a row,
    /// or the trailer — rather than a running total that means something different depending on
    /// where it sits.
    /// </para>
    /// </remarks>
    private static ListNode BuildList(
        ListRule rule,
        Scope scope,
        MappingContext context,
        SourceTraits traits,
        List<MappingError> errors,
        string path)
    {
        var target = Describe(path, rule.Target);
        var rows = Matching(rule, scope, traits, errors, target);
        var list = ValueNode.List();

        // Read against the scope the list sits in, since a written entry has no entry of its own.
        foreach (var entry in rule.Fixed)
            AddEntry(list, entry.Item, entry.Fields, entry.Lists, scope, context, traits, errors,
                target, rows.Count);

        foreach (var item in rows)
            AddEntry(list, rule.Item, rule.Fields, rule.Lists, scope.Enter(item), context, traits,
                errors, target, rows.Count);

        // A trailer belongs on the file whether the source list had a thousand entries, none, or
        // was never there at all — a partner expecting a record count still expects to be told
        // that it is zero.
        foreach (var entry in rule.After)
            AddEntry(list, entry.Item, entry.Fields, entry.Lists, scope, context, traits, errors,
                target, rows.Count);

        return list;
    }

    /// <summary>
    /// The entries of the source list that this rule's condition lets through.
    /// </summary>
    /// <remarks>
    /// Separated from building them so that how many there are is known first. Nothing is produced
    /// here; a rule that fails does so when its entry is built, as it always did.
    /// </remarks>
    private static List<ValueNode> Matching(
        ListRule rule,
        Scope scope,
        SourceTraits traits,
        List<MappingError> errors,
        string target)
    {
        var matched = new List<ValueNode>();

        // No source list to walk: the list is whatever its written entries produce.
        if (rule.Over is null) return matched;

        // A path that is absent adds nothing rather than failing. An order with no lines is
        // ordinary; so is an optional section.
        var over = Values.Resolve(scope.Current, rule.Over);
        if (over is null) return matched;

        // XML makes a list by repeating a name, so an order with one <line> is the same document as
        // one whose `line` was never a list. Reading that as no lines would drop the only line
        // without a word, so where the format cannot say, a single value walks as a list of one.
        //
        // Only for a *named* path, though. A document has exactly one root and it is never a
        // repeated element, so there is no ambiguity to forgive at `over: ""` — that is a
        // deliberate claim that the whole document is a list, and a document that is not one has
        // no entries rather than one entry that is the whole document.
        IReadOnlyList<ValueNode> items = over switch
        {
            ListNode found => found.Items,
            _ when traits.SingleValueIsAList && rule.Over.Length > 0 => [over],
            _ => [],
        };

        foreach (var item in items)
        {
            if (rule.Where is not null && !Matches(rule.Where, item, out var whereError))
            {
                if (whereError is null) continue;

                // The condition itself is unusable, so every remaining item would report the same
                // thing. Say it once.
                errors.Add(new MappingError(target, whereError));
                break;
            }

            matched.Add(item);
        }

        return matched;
    }

    /// <summary>
    /// Adds one entry to a list: a single value when <paramref name="item"/> is set, otherwise an
    /// object built from <paramref name="fields"/> and <paramref name="lists"/>.
    /// </summary>
    /// <remarks>
    /// Shared by walked and fixed entries, which is the point — the two differ only in what they
    /// read against, so anything that works in one works in the other.
    /// </remarks>
    private static void AddEntry(
        ListNode list,
        FieldRule? item,
        List<FieldRule> fields,
        List<ListRule> lists,
        Scope scope,
        MappingContext context,
        SourceTraits traits,
        List<MappingError> errors,
        string target,
        int written)
    {
        if (item is not null)
        {
            if (TryResolveField(item, scope, context, traits, written, out var value, out var reason))
                list.Add(ValueNode.Value(value));
            else
                errors.Add(new MappingError(target, reason!));
            return;
        }

        var row = ValueNode.Object();
        MapInto(row, fields, lists, scope, context, traits, errors, target, written);
        list.Add(row);
    }

    private static bool TryResolveField(
        FieldRule field,
        Scope scope,
        MappingContext context,
        SourceTraits traits,
        int? written,
        out object? value,
        out string? reason)
    {
        reason = null;

        // Outside a list there is nothing to count, and answering zero would be a number the
        // partner would act on rather than a mistake anyone would notice.
        if (field.From.Kind == ValueSourceKind.Count && written is null)
        {
            value = null;
            reason = "counting entries only means something inside a list";
            return false;
        }

        value = field.From.Kind switch
        {
            ValueSourceKind.Fixed => field.From.Value,
            ValueSourceKind.Path => Values.ResolveScalar(scope.Current, field.From.Path),
            ValueSourceKind.RootPath => Values.ResolveScalar(scope.Root, field.From.Path),
            ValueSourceKind.Partner => context.PartnerValue(field.From.Key),
            ValueSourceKind.Global => context.GlobalValue(field.From.SetId, field.From.Key),
            ValueSourceKind.Count => (decimal)(written ?? 0),
            _ => null,
        };

        if (field.Transform is not null &&
            !Transforms.TryApply(field.Transform, value, traits.DateOrder, out value, out reason))
            return false;

        if (field.Lookup is not null)
            value = ApplyLookup(field.Lookup, value);

        // Into a separate variable: a failed conversion clears its output, so reading the message
        // off `value` would report "cannot convert 'null'" and hide what the value actually was.
        if (!Values.TryCoerce(value, field.Type, out var coerced))
        {
            reason = $"cannot convert {DescribeValue(value, field.From.Kind)} to " +
                     field.Type.ToString()!.ToLowerInvariant();
            return false;
        }

        value = coerced;
        return true;
    }

    /// <summary>
    /// A value in an error message, with configured values described rather than quoted.
    /// </summary>
    /// <remarks>
    /// A value out of the document being mapped is safe to quote, and quoting it is most of what
    /// makes the message useful. A partner property or a values-set entry is configuration, and
    /// some of it is secret — an adapter password reaching a number field would otherwise be
    /// written into <c>XchangeResult.Exception</c>, which is stored and shown on the exchange, and
    /// returned by the preview API. So those are described by length instead.
    /// </remarks>
    private static string DescribeValue(object? value, ValueSourceKind kind) =>
        (kind is ValueSourceKind.Partner or ValueSourceKind.Global) && value is not null
            ? $"the configured value ({Display(value).Length} characters)"
            : $"'{Display(value)}'";

    /// <summary>
    /// Substitutes a value using the rule's table.
    /// </summary>
    /// <remarks>
    /// Keys are compared as text, because a lookup table is written by hand in the editor and the
    /// document it matches against may be XML or CSV, where <c>1</c> and <c>"1"</c> are the same
    /// thing. A miss gives the fallback, which is null unless the rule says otherwise.
    /// </remarks>
    private static object? ApplyLookup(LookupRule lookup, object? value)
    {
        if (value is null) return lookup.Fallback;

        var key = Values.TryCoerce(value, ValueType.String, out var text) ? text as string : null;
        return key is not null && lookup.Table.TryGetValue(key, out var mapped)
            ? mapped
            : lookup.Fallback;
    }

    /// <summary>
    /// Whether an item satisfies a list's condition.
    /// </summary>
    /// <remarks>
    /// Returns false with a null <paramref name="error"/> for "does not match", and false with an
    /// error for "cannot be compared" — an ordering operator against a value that is not a number.
    /// A missing field does not match and is not an error, so filtering on an optional field skips
    /// the items that lack it instead of failing the exchange.
    /// </remarks>
    private static bool Matches(FilterRule filter, ValueNode item, out string? error)
    {
        error = null;
        var actual = Values.ResolveScalar(item, filter.Field);

        if (filter.Operator is FilterOperator.Equal or FilterOperator.NotEqual)
        {
            var same = string.Equals(AsComparableText(actual), AsComparableText(filter.Value),
                StringComparison.Ordinal);
            return filter.Operator == FilterOperator.Equal ? same : !same;
        }

        if (!TryAsNumber(actual, out var left) || !TryAsNumber(filter.Value, out var right))
        {
            // Only report when the rule itself is unusable. An item missing the field is skipped.
            if (actual is not null)
                error = $"cannot compare '{Display(actual)}' with '{Display(filter.Value)}' using {filter.Operator}";
            return false;
        }

        return filter.Operator switch
        {
            FilterOperator.GreaterThan => left > right,
            FilterOperator.GreaterThanOrEqual => left >= right,
            FilterOperator.LessThan => left < right,
            FilterOperator.LessThanOrEqual => left <= right,
            _ => false,
        };
    }

    private static bool TryAsNumber(object? value, out decimal number)
    {
        if (Values.TryCoerce(value, ValueType.Number, out var coerced) && coerced is decimal d)
        {
            number = d;
            return true;
        }
        number = 0;
        return false;
    }

    private static string? AsComparableText(object? value) =>
        value is null ? null : Values.TryCoerce(value, ValueType.String, out var text) ? text as string : null;

    private static string Display(object? value) => value switch
    {
        null => "null",
        decimal m => Values.FormatNumber(m),
        bool b => b ? "true" : "false",
        _ => value.ToString() ?? "",
    };

    /// <summary>The target as the user wrote it, for error messages.</summary>
    private static string Describe(string path, IReadOnlyList<string> target)
    {
        var joined = target.Count == 0 ? "(no target)" : string.Join(".", target);
        return path.Length == 0 ? joined : $"{path}[].{joined}";
    }
}
