#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using SW.Bitween.NativeAdapters.Mapper;

namespace SW.Bitween.Services.Ai;

/// <summary>
/// What a model is told to write mapping rules, and how its answer becomes rules again.
/// </summary>
/// <remarks>
/// <para>
/// The answer is asked for as plain JSON and read with the mapper's own types, rather than
/// constrained by a schema: the rules nest lists inside lists, which providers' schema-constrained
/// output can't express. Reading it strictly - an unknown property is an error - and then running
/// it against the sample catches more than a schema would.
/// </para>
/// <para>
/// <see cref="Instructions"/> is the same text on every call, so a provider can cache it. Everything
/// that varies goes in the message.
/// </para>
/// </remarks>
public static class MappingRulesPrompt
{
    public const string Instructions = """
        You write mapping rules for Bitween's rules-based mapper. Given a sample of a source document
        and the document that should be produced from it, answer with the rules that turn one into the
        other.

        Answer with exactly one JSON object: the complete rules. No prose, no Markdown, no code fence.

        # The rules format

        {
          "version": 1,
          "sourceFormat": "json",          // given in the message; copy it
          "targetFormat": "json",          // given in the message; copy it
          "sourceDateOrder": "yearFirst",  // yearFirst | dayFirst | monthFirst
          "fields": [ FieldRule, ... ],
          "lists": [ ListRule, ... ],
          "root": ListRule                 // optional: makes the whole output an array
        }

        Copy any sourceCsv, targetCsv, sourceFixedWidth, targetFixedWidth, sourceExcel and targetExcel
        settings the message gives you, unchanged. Use no property this description doesn't name.

        ## FieldRule: writes one value

        { "target": ["customer", "name"], "from": ValueSource, "transform": Transform, "lookup": Lookup, "type": "number" }

        - target: the output path as segments, so a key containing a dot is one segment. Missing
          objects along the path are created.
        - from (required), then optionally transform, lookup and type, applied in that order.
        - type converts the value to "string", "number" or "boolean". Use it whenever the output
          wants a different JSON type than the source has, e.g. "42" in the source and 42 in the output.

        ValueSource, by kind:
        - { "kind": "path", "path": "order.id" } - a dot path read from the current scope: the
          document, or the current list entry inside a list.
        - { "kind": "rootPath", "path": "order.id" } - a dot path from the document root, even inside
          a list. Use it to copy a header value onto every line.
        - { "kind": "fixed", "value": "EUR" } - a literal, keeping its JSON type.
        - { "kind": "count" } - how many source entries the enclosing list walked; for trailer records.
        - { "kind": "partner", "key": "SenderId" } - a property of the exchange's partner.
        - { "kind": "global", "setId": "regions", "key": "default" } - a value from a global value set.
          You only ever see these two by name, in the message. Use them only for names it lists, and
          only when the output wants a value the source doesn't have and a name plainly matches it.
        - "source" also exists, but needs a document you can't see. Don't use it.

        Paths match keys exactly and never step into a list: "lines.0.sku" is not a path. A list
        rule is the only way to reach list entries. A missing path gives null, not an error.

        Transform, one per field, exactly these:
        - { "fn": "upper" }, { "fn": "lower" }, { "fn": "trim" }
        - { "fn": "substring", "start": 0, "length": 3 } - start counts from 0; length optional (to the end)
        - { "fn": "replace", "find": "-", "with": "" }
        - { "fn": "concat", "with": " Ltd" } - appends text
        - { "fn": "round", "decimals": 2 } - halves away from zero
        - { "fn": "multiply", "by": 100 }, { "fn": "add", "amount": -1 }
        - { "fn": "formatDate", "format": "yyyy-MM-dd" } - reads a date, writes it with a .NET format string
        - { "fn": "defaultIfEmpty", "value": "N/A" } - replaces null or empty text
        Null passes through every transform except defaultIfEmpty. Numbers are exact decimals.

        Lookup: { "table": { "N": "New", "S": "Shipped" }, "fallback": "Unknown" } - replaces the value
        by comparing it as text; anything not in the table becomes fallback (null when left out). Use it
        when the samples show codes being translated.

        ## ListRule: writes an array

        { "over": "order.lines", "target": ["items"], "where": Filter, "fields": [...], "lists": [...] }

        - over: the path of the source list to walk. "" walks the document itself (a CSV, fixed-width or
          Excel source is a list of rows, so it is walked with ""). Leaving over out walks nothing, and
          the list holds only its fixed entries.
        - target: where the array is written.
        - fields and lists: applied to each entry, with paths relative to the entry. Lists nest.
        - item: one FieldRule (its target unused) that makes each entry a plain value instead of an
          object, for ["A1", "B7"] rather than [{ "sku": "A1" }].
        - where: { "field": "qty", "operator": "greaterThan", "value": 0 } skips entries that don't
          match. Operators: equal, notEqual (compare as text), greaterThan, greaterThanOrEqual,
          lessThan, lessThanOrEqual (compare as numbers).
        - fixed and after: entries placed before or after the walked ones, each
          { "item": FieldRule } or { "fields": [...], "lists": [...] }, read from the scope the list sits
          in. For header and trailer records, or a list built from separate fields.

        A top-level "root" makes the output an array; top-level fields and lists must then be empty.

        ## Formats

        - XML read: the root element's name is the top-level key, attributes are "@name" keys, mixed
          text is "#text", repeated elements are lists, and namespaces are dropped.
        - XML written: exactly one top-level key, not a list; "@name" targets become attributes;
          "@xmlns" or "@xmlns:prefix" targets declare namespaces.
        - CSV, fixed-width and Excel read: a list of rows whose values are all text, keyed by header
          name, or by position "1", "2", ... without a header.
        - CSV, fixed-width and Excel written: a list of rows (use root), or one object for one row. A
          nested target becomes a dotted column name.
        - Dates: formatDate always reads year-first dates such as 2026-09-10 or 2026-09-10T08:30:00Z.
          Set sourceDateOrder to dayFirst or monthFirst when the source writes dates like 10/09/2026.

        # How to choose rules

        - Take every output value from the source when the source has it, directly or after a
          transform, lookup or type. Use "fixed" only for values the source can't supply.
        - Never write a value that appears in the source sample as fixed text - not as a "fixed"
          value, and not as the text of a concat, replace or defaultIfEmpty. It is data that
          changes from one document to the next, and freezing it would match this sample only.
          When the format can't build an output value from the source (it can't join two fields,
          for example), map what you can and leave that value different; it will be finished by
          hand.
        - The output sample may hold placeholder values. Match its shape and its value types, and
          don't copy placeholder values as fixed values when a source field plainly corresponds.
        - Write the output keys in the order the output sample has them.
        - Keep it minimal: no transform, lookup or type that the samples don't call for.
        - When the message has instructions from the person building the mapping, follow them: they
          say how the mapping should behave beyond what the samples show, such as codes a lookup
          needs that the sample doesn't happen to contain.
        """;

    /// <summary>At most this many names of each kind go in a message, so a big partner can't crowd out the samples.</summary>
    public const int NameLimit = 100;

    /// <summary>The message for one call: the formats, both samples, and what went wrong last time.</summary>
    /// <param name="formats">The editor's rules, for their formats and format settings.</param>
    /// <param name="names">
    /// The partner properties and global values the rules may read, by name. Never their values:
    /// those stay on the server, and are only filled in when the mapping runs.
    /// </param>
    /// <param name="instructions">What the person building the mapping wrote for the model, if anything.</param>
    /// <param name="previous">Rules to fix rather than start from scratch, with what was wrong with them.</param>
    public static string Message(MappingRules formats, string sourceSample, string targetSample,
        ValueNames? names = null, string? instructions = null,
        (MappingRules Rules, IReadOnlyList<string> Problems)? previous = null)
    {
        var message = new StringBuilder();
        message.AppendLine("Formats and their settings (copy them into your answer):");
        message.AppendLine(Serialize(FormatsOnly(formats)));
        message.AppendLine();
        message.AppendLine("<source_sample>");
        message.AppendLine(sourceSample.Trim());
        message.AppendLine("</source_sample>");
        message.AppendLine();
        message.AppendLine("<output_sample>");
        message.AppendLine(targetSample.Trim());
        message.AppendLine("</output_sample>");

        if (names is { } known && (known.Partner.Count > 0 || known.Globals.Count > 0))
        {
            message.AppendLine();
            message.AppendLine("Values Bitween fills in when the mapping runs, by name only:");
            if (known.Partner.Count > 0)
                message.AppendLine("- partner properties: " + string.Join(", ", known.Partner.Take(NameLimit)));
            foreach (var (setId, keys) in known.Globals.Take(NameLimit))
                message.AppendLine($"- global value set \"{setId}\", keys: " + string.Join(", ", keys.Take(NameLimit)));
        }

        if (!string.IsNullOrWhiteSpace(instructions))
        {
            message.AppendLine();
            message.AppendLine("<instructions>");
            message.AppendLine(instructions.Trim());
            message.AppendLine("</instructions>");
        }

        if (previous is { } last)
        {
            message.AppendLine();
            message.AppendLine("These rules were tried and did not produce the output sample:");
            message.AppendLine(Serialize(last.Rules));
            message.AppendLine();
            message.AppendLine("What was wrong:");
            foreach (var problem in last.Problems) message.AppendLine("- " + problem);
            message.AppendLine();
            message.AppendLine("Answer with corrected rules. Keep what was right.");
        }

        return message.ToString();
    }

    /// <summary>
    /// The rules in a model's answer, with the editor's formats put back.
    /// </summary>
    /// <exception cref="FormatException">With a reason a model can act on, when the answer isn't rules.</exception>
    public static MappingRules Parse(string answer, MappingRules formats)
    {
        var json = JsonPart(answer)
                   ?? throw new FormatException("The answer contained no JSON object.");
        MappingRules? rules;
        try
        {
            rules = JsonConvert.DeserializeObject<MappingRules>(json, Strict);
        }
        catch (JsonException ex)
        {
            throw new FormatException($"The answer is not valid rules JSON: {ex.Message}");
        }
        if (rules is null) throw new FormatException("The answer was empty.");

        if (rules.Version != MappingRules.CurrentVersion)
            throw new FormatException($"\"version\" must be {MappingRules.CurrentVersion}.");
        if (rules.Root is not null && (rules.Fields.Count > 0 || rules.Lists.Count > 0))
            throw new FormatException("With \"root\" set, top-level \"fields\" and \"lists\" must be empty.");

        // Whatever the model wrote, the formats are the editor's: they describe the exchange, and
        // the samples were read with them.
        rules.SourceFormat = formats.SourceFormat;
        rules.TargetFormat = formats.TargetFormat;
        rules.SourceCsv = formats.SourceCsv;
        rules.TargetCsv = formats.TargetCsv;
        rules.SourceFixedWidth = formats.SourceFixedWidth;
        rules.TargetFixedWidth = formats.TargetFixedWidth;
        rules.SourceExcel = formats.SourceExcel;
        rules.TargetExcel = formats.TargetExcel;
        return rules;
    }

    /// <summary>
    /// The names of the partner properties and global values a mapping can read - the keys of the
    /// context a preview maps against, with every value left behind.
    /// </summary>
    /// <param name="masked">A context built with its secrets masked, as the preview's is.</param>
    /// <remarks>
    /// Secrets are left out by name as well: a rule reading one would copy a credential into a
    /// document, which is never what a sample asks for and is better written by hand if it is meant.
    /// </remarks>
    public static ValueNames NamesOf(MappingContext masked) => new(
        Plain(masked.Partner),
        masked.Globals.OrderBy(set => set.Key, StringComparer.Ordinal)
            .Select(set => (set.Key, Keys: Plain(set.Value)))
            .Where(set => set.Keys.Count > 0)
            .ToDictionary(set => set.Key, set => set.Keys));

    static IReadOnlyList<string> Plain(IReadOnlyDictionary<string, string> values) =>
        values.Where(kv => kv.Value != AdapterSecretProperties.Sentinel)
            .Select(kv => kv.Key).Order(StringComparer.Ordinal).ToList();

    /// <summary>The rules as the editor stores them: camelCase, enums as camelCase text, nulls left out.</summary>
    public static string Serialize(MappingRules rules) => JsonConvert.SerializeObject(rules, Wire);

    /// <summary>
    /// From the first brace to the last: some models wrap JSON in a code fence or a sentence even
    /// when asked not to, and that shouldn't cost a second call.
    /// </summary>
    static string? JsonPart(string answer)
    {
        var start = answer.IndexOf('{');
        var end = answer.LastIndexOf('}');
        return start >= 0 && end > start ? answer[start..(end + 1)] : null;
    }

    static MappingRules FormatsOnly(MappingRules rules) => new()
    {
        SourceFormat = rules.SourceFormat,
        TargetFormat = rules.TargetFormat,
        SourceDateOrder = rules.SourceDateOrder,
        SourceCsv = rules.SourceCsv,
        TargetCsv = rules.TargetCsv,
        SourceFixedWidth = rules.SourceFixedWidth,
        TargetFixedWidth = rules.TargetFixedWidth,
        SourceExcel = rules.SourceExcel,
        TargetExcel = rules.TargetExcel,
    };

    // An unknown property is an error rather than ignored: it is a rule the model meant and the
    // mapper would silently drop.
    static readonly JsonSerializerSettings Strict = new() { ContractResolver = new StrictExceptTransforms() };

    /// <summary>
    /// Strict for every type but a transform, whose arguments sit beside "fn" as extension data.
    /// Strictness set on the serializer would refuse those too: Newtonsoft checks for a missing
    /// member before it looks for extension data.
    /// </summary>
    sealed class StrictExceptTransforms : DefaultContractResolver
    {
        protected override JsonObjectContract CreateObjectContract(Type objectType)
        {
            var contract = base.CreateObjectContract(objectType);
            contract.MissingMemberHandling = objectType == typeof(TransformRule)
                ? MissingMemberHandling.Ignore
                : MissingMemberHandling.Error;
            return contract;
        }
    }

    static readonly JsonSerializerSettings Wire = new()
    {
        // Lookup tables are dictionaries whose keys are data, so they keep their case.
        ContractResolver = new DefaultContractResolver
        {
            NamingStrategy = new CamelCaseNamingStrategy { ProcessDictionaryKeys = false },
        },
        Converters = { new StringEnumConverter(new CamelCaseNamingStrategy()) },
        NullValueHandling = NullValueHandling.Ignore,
    };
}

/// <summary>Names a mapping can read values by, without the values.</summary>
public sealed record ValueNames(
    IReadOnlyList<string> Partner,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Globals);
