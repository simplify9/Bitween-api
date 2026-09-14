using Newtonsoft.Json;

namespace SW.Bitween.NativeAdapters.Mapper;

/// <summary>
/// What the user configured, written down. The source of truth for a mapping — nothing is derived
/// from a generated artefact, and nothing is inferred from a sample document.
/// </summary>
/// <remarks>
/// <para>
/// The previous mapper saved the Scriban template it generated and threw the rules away, so opening
/// the editor again meant reverse-engineering the template with regular expressions. Rules are
/// source; a template is output. Storing only the output and recovering the source from it is
/// lossy, and it fails silently.
/// </para>
/// <para>
/// <see cref="Version"/> exists from the first release on purpose. Two mapper config formats already
/// shipped without one, so telling them apart now means sniffing for keys.
/// </para>
/// </remarks>
public class MappingRules
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>Format id of the incoming document — see <c>IDocumentFormat.Id</c>.</summary>
    public string SourceFormat { get; set; } = "json";

    /// <summary>Format id of the document to produce.</summary>
    public string TargetFormat { get; set; } = "json";

    /// <summary>
    /// Which of the day and the month comes first in the incoming document's dates.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On the mapping rather than on each rule, because a partner writes dates one way
    /// throughout a document — asking per field would be the same answer typed many times,
    /// and one row set wrongly would be invisible.
    /// </para>
    /// <para>
    /// It has to be asked at all because <c>04.09.2026</c> is the 4th of September to a French
    /// partner and the 9th of April to the invariant parser, which reads it that way without
    /// complaining. Year-first values are unambiguous and need no setting.
    /// </para>
    /// </remarks>
    public DateOrder SourceDateOrder { get; set; } = DateOrder.YearFirst;

    /// <summary>How the incoming document is delimited, when it is delimited text.</summary>
    /// <remarks>
    /// Null for every other format, and null here means the defaults. Two options rather than one
    /// because the two sides are genuinely independent: reading a partner's semicolon file and
    /// writing a comma file for someone else is one mapping.
    /// </remarks>
    public Formats.CsvOptions? SourceCsv { get; set; }

    /// <summary>How the produced document is delimited, when it is delimited text.</summary>
    public Formats.CsvOptions? TargetCsv { get; set; }

    public List<FieldRule> Fields { get; set; } = new();

    public List<ListRule> Lists { get; set; } = new();

    /// <summary>
    /// When set, the whole output document is this list rather than an object.
    /// </summary>
    /// <remarks>
    /// Some partners expect a bare array — <c>[ {...}, {...} ]</c> — rather than an object with a
    /// list inside it. <see cref="Fields"/> and <see cref="Lists"/> are ignored when this is set,
    /// because a document is one thing or the other.
    /// </remarks>
    public ListRule? Root { get; set; }
}

/// <summary>One output field: where its value comes from, and what it should end up as.</summary>
public class FieldRule
{
    /// <summary>
    /// Where to put the value, as path segments.
    /// </summary>
    /// <remarks>
    /// Segments rather than a dotted string, because a dotted string has to be split and then a key
    /// that genuinely contains a dot becomes unreachable — the old mapper turned a target of
    /// <c>file.txt</c> into a nested <c>{ "file": { "txt": … } }</c> with no way to ask for the
    /// literal key.
    /// </remarks>
    public List<string> Target { get; set; } = new();

    public ValueSource From { get; set; } = new();

    /// <summary>Optional named function applied to the value — see <see cref="Transforms"/>.</summary>
    public TransformRule? Transform { get; set; }

    /// <summary>Optional value substitution applied after <see cref="Transform"/>.</summary>
    public LookupRule? Lookup { get; set; }

    /// <summary>
    /// What the value should be coerced to. Null leaves it as the source produced it.
    /// </summary>
    /// <remarks>
    /// On the rule, not inferred from a target sample document. The old mapper read types out of
    /// whatever JSON was pasted into the editor, which meant the sample changed the output — so the
    /// same rules produced different documents depending on what someone had pasted.
    /// </remarks>
    public ValueType? Type { get; set; }
}

/// <summary>
/// Where a field's value comes from. Exactly one shape, chosen by <see cref="Kind"/>.
/// </summary>
/// <remarks>
/// The old model had six optional properties and picked whichever happened to be set, in a fixed
/// precedence order. That made "two sources set at once" representable, which is what the editor's
/// mode-switching bugs were: switching a field from one source to another had to remember to clear
/// all five others.
/// </remarks>
public class ValueSource
{
    public ValueSourceKind Kind { get; set; } = ValueSourceKind.Fixed;

    /// <summary>Dot-separated path into the source document. <see cref="ValueSourceKind.Path"/>.</summary>
    public string? Path { get; set; }

    /// <summary>Literal value. <see cref="ValueSourceKind.Fixed"/>.</summary>
    public object? Value { get; set; }

    /// <summary>Partner adapter-property key. <see cref="ValueSourceKind.Partner"/>.</summary>
    public string? Key { get; set; }

    /// <summary>Global values-set id. <see cref="ValueSourceKind.Global"/>, with <see cref="Key"/>.</summary>
    public string? SetId { get; set; }
}

public enum ValueSourceKind
{
    /// <summary>A literal written into the rule.</summary>
    Fixed,

    /// <summary>A path into the source document, read from wherever the rule sits.</summary>
    Path,

    /// <summary>
    /// A path read from the top of the document rather than from the current list entry.
    /// </summary>
    /// <remarks>
    /// Only different from <see cref="Path"/> inside a list: it is how one value from the
    /// document — an order reference, say — gets written onto every line of that order.
    /// </remarks>
    RootPath,

    /// <summary>A property of the exchange's partner.</summary>
    Partner,

    /// <summary>A key in one of the global values sets.</summary>
    Global,

    /// <summary>
    /// How many entries the source list produced for the list this rule sits in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For a trailer record: a partner's file ends with a line carrying the number of records
    /// above it, and nothing else in a mapping knows that number.
    /// </para>
    /// <para>
    /// Entries written by hand are not counted, which is the whole point of the distinction — a
    /// trailer says how many <em>records</em> there are, and the header line above it is not one.
    /// Counting them would also mean adding a header later quietly changed the trailer, with
    /// nothing on screen to say the number had moved.
    /// </para>
    /// <para>
    /// The same number wherever it is read in one list, so a format that puts its count in the
    /// header rather than the trailer works without anything special.
    /// </para>
    /// </remarks>
    Count,
}

/// <summary>How a document writes dates that are not year-first.</summary>
public enum DateOrder
{
    /// <summary>Only year-first values are accepted; anything else is refused rather than guessed.</summary>
    YearFirst,

    /// <summary><c>04.09.2026</c> is the 4th of September.</summary>
    DayFirst,

    /// <summary><c>04.09.2026</c> is the 9th of April.</summary>
    MonthFirst,
}

public enum ValueType
{
    String,
    Number,
    Boolean,
}

/// <summary>
/// A named function and its arguments.
/// </summary>
/// <remarks>
/// Named rather than a free-text expression, so there is no expression language to parse, sandbox or
/// generate. Each function is an ordinary method with its own test, and the editor can offer a list
/// instead of a syntax. If arbitrary arithmetic across several fields is ever genuinely needed, it
/// arrives as an explicit additional <see cref="ValueSourceKind"/> rather than by making this field
/// free text again.
/// </remarks>
public class TransformRule
{
    [JsonProperty("fn")]
    public string Fn { get; set; } = "";

    /// <summary>Arguments by name, e.g. <c>by</c> for multiply or <c>format</c> for formatDate.</summary>
    [JsonExtensionData]
    public Dictionary<string, Newtonsoft.Json.Linq.JToken> Args { get; set; } = new();
}

/// <summary>Replaces a value using a lookup table, with a fallback for anything not in it.</summary>
public class LookupRule
{
    public Dictionary<string, object?> Table { get; set; } = new();

    /// <summary>Used when the value is not a key in <see cref="Table"/>. Null means output null.</summary>
    public object? Fallback { get; set; }
}

/// <summary>A list in the output, whose entries come from walking a list in the source.</summary>
/// <remarks>
/// Named for what it produces rather than for how — a list, not a loop. Walking a source list is
/// one way to fill one, and the name leaves room for the others without becoming a lie.
/// </remarks>
public class ListRule
{
    /// <summary>
    /// Path to the source list to walk.
    /// </summary>
    /// <remarks>
    /// Three states, and the difference between the first two matters: absent means nothing is
    /// walked and the list is whatever <see cref="Fixed"/> holds; empty means the source document
    /// is itself the list; anything else is a path to one.
    /// </remarks>
    /// No initialiser on purpose: one would turn an absent path into an empty one, and those
    /// two mean different things.
    public string? Over { get; set; }

    /// <summary>Name the entry is known by inside this list's paths and condition.</summary>
    public string As { get; set; } = "item";

    /// <summary>Where the resulting list goes, as path segments.</summary>
    public List<string> Target { get; set; } = new();

    /// <summary>Optional condition; entries that do not match are skipped.</summary>
    public FilterRule? Where { get; set; }

    /// <summary>
    /// When set, each entry produces a single value instead of an object — a list of strings or
    /// numbers rather than a list of records.
    /// </summary>
    /// <remarks>
    /// <c>{ "skus": ["A1","B7"] }</c> rather than <c>{ "lines": [{"sku":"A1"}] }</c>. Its
    /// <see cref="FieldRule.Target"/> is unused, since the value has nowhere to be named.
    /// <see cref="Fields"/> and <see cref="Lists"/> are ignored when this is set.
    /// </remarks>
    public FieldRule? Item { get; set; }

    public List<FieldRule> Fields { get; set; } = new();

    /// <summary>
    /// Lists nested inside this one, each walking a list found on the current entry.
    /// </summary>
    /// <remarks>
    /// Nesting is structural rather than a flat list joined by parent ids, which is how the old
    /// model did it — that needed three separate recursive helpers to rebuild the full source and
    /// target paths, and a rule whose parent had been deleted became unreachable rather than
    /// invalid.
    /// </remarks>
    public List<ListRule> Lists { get; set; } = new();

    /// <summary>
    /// Entries put into the list before the walked ones.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For a list that is partly or wholly constant: a header line a partner expects, or a list
    /// whose entries each come from a different field rather than from one source list.
    /// </para>
    /// <para>
    /// Each entry is built from its own rules, so a constant entry may still read the document,
    /// the partner or a values set, and may contain lists of its own — where the old mapper had
    /// two separate features holding literal JSON and a flat array of value slots.
    /// </para>
    /// </remarks>
    public List<ListEntry> Fixed { get; set; } = new();

    /// <summary>
    /// Entries put into the list after the walked ones.
    /// </summary>
    /// <remarks>
    /// The other end of <see cref="Fixed"/>, and the reason it exists: a delimited file from a
    /// carrier ends with a trailer record carrying the number of records above it. Built exactly
    /// the same way, reading against the same scope — the only difference is where they land, and
    /// that <see cref="ValueSourceKind.Count"/> can see the whole list by the time they run.
    /// </remarks>
    public List<ListEntry> After { get; set; } = new();
}

/// <summary>One entry of a list that no source list produced.</summary>
public class ListEntry
{
    /// <summary>
    /// When set, the entry is a single value rather than an object — the same choice
    /// <see cref="ListRule.Item"/> makes for a walked entry.
    /// </summary>
    public FieldRule? Item { get; set; }

    public List<FieldRule> Fields { get; set; } = new();

    public List<ListRule> Lists { get; set; } = new();
}

/// <summary>A condition on one field of the current item.</summary>
public class FilterRule
{
    public string Field { get; set; } = "";

    public FilterOperator Operator { get; set; } = FilterOperator.Equal;

    public object? Value { get; set; }
}

public enum FilterOperator
{
    Equal,
    NotEqual,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
}
