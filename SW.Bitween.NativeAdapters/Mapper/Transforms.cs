using System.Globalization;
using Newtonsoft.Json.Linq;

namespace SW.Bitween.NativeAdapters.Mapper;

/// <summary>
/// The named functions a rule can apply to a value.
/// </summary>
/// <remarks>
/// <para>
/// A closed list rather than a free-text expression, which means there is no expression language to
/// parse, sandbox or generate - and the editor can offer a dropdown instead of a syntax to learn.
/// Each function here is an ordinary method with its own test.
/// </para>
/// <para>
/// Adding a function is the way to extend this. Turning the field back into free text is not: it
/// would put user-supplied code back on the server and take the discoverable list away from the UI.
/// </para>
/// </remarks>
public static class Transforms
{
    /// <summary>Every function name, for validation and for the editor's list.</summary>
    public static IReadOnlyList<string> Names { get; } =
    [
        "upper", "lower", "trim", "substring", "replace", "concat",
        "round", "multiply", "add", "formatDate", "defaultIfEmpty",
    ];

    /// <summary>
    /// Applies a transform, or reports why it could not be applied.
    /// </summary>
    /// <remarks>
    /// A null input passes straight through for every function: transforming an absent value should
    /// leave it absent, not turn it into <c>""</c> or <c>0</c>. The exception is
    /// <c>defaultIfEmpty</c>, whose entire job is to replace one.
    /// </remarks>
    public static bool TryApply(TransformRule rule, object? value, DateOrder dateOrder,
        out object? result, out string? error)
    {
        // Every failure this class knows about is reported through `error`, and DocumentMapper
        // collects those so one mapping run names every rule that is wrong. An exception escapes
        // that: it aborts the run at the first bad rule and surfaces as a stack trace instead.
        //
        // Arguments come from a saved rule and are not validated on the way in, so a few of them
        // throw rather than return - an out-of-range decimal cast for a substring index, a decimal
        // multiplication that overflows, a date format string the framework rejects. Those become
        // ordinary failures here.
        try
        {
            return Apply(rule, value, dateOrder, out result, out error);
        }
        catch (Exception ex) when (ex is OverflowException or FormatException or ArgumentException
                                       or InvalidCastException or Newtonsoft.Json.JsonException)
        {
            result = null;
            error = $"{rule.Fn} could not be applied: {ex.Message}";
            return false;
        }
    }

    private static bool Apply(TransformRule rule, object? value, DateOrder dateOrder,
        out object? result, out string? error)
    {
        result = value;
        error = null;

        if (string.IsNullOrWhiteSpace(rule.Fn))
        {
            error = "transform has no function name";
            return false;
        }

        if (rule.Fn == "defaultIfEmpty")
        {
            var isEmpty = value is null || (value is string s && s.Length == 0);
            result = isEmpty ? Arg(rule, "value")?.ToObject<object?>() : value;
            return true;
        }

        if (value is null) return true;

        switch (rule.Fn)
        {
            case "upper":
                result = AsString(value).ToUpperInvariant();
                return true;

            case "lower":
                result = AsString(value).ToLowerInvariant();
                return true;

            case "trim":
                result = AsString(value).Trim();
                return true;

            case "substring":
            {
                var text = AsString(value);
                var start = (int)(ArgAsDecimal(rule, "start") ?? 0);
                if (start < 0 || start > text.Length)
                {
                    error = $"substring start {start} is outside a value of length {text.Length}";
                    return false;
                }
                var lengthArg = ArgAsDecimal(rule, "length");
                // No length means "to the end", which is what a user leaving the box empty means.
                var length = lengthArg is null
                    ? text.Length - start
                    : Math.Min((int)lengthArg.Value, text.Length - start);
                if (length < 0)
                {
                    error = $"substring length {lengthArg} is negative";
                    return false;
                }
                result = text.Substring(start, length);
                return true;
            }

            case "replace":
            {
                var find = ArgAsString(rule, "find");
                // Replacing the empty string is not a no-op - String.Replace throws for it - and
                // there is no sensible reading of "replace nothing with something".
                if (string.IsNullOrEmpty(find))
                {
                    error = "replace needs 'find'";
                    return false;
                }
                result = AsString(value).Replace(find, ArgAsString(rule, "with") ?? "");
                return true;
            }

            case "concat":
                result = AsString(value) + (ArgAsString(rule, "with") ?? "");
                return true;

            case "round":
            {
                if (!Values.TryCoerce(value, ValueType.Number, out var number) || number is not decimal d)
                {
                    error = $"round needs a number, got '{value}'";
                    return false;
                }
                var decimals = (int)(ArgAsDecimal(rule, "decimals") ?? 0);
                if (decimals is < 0 or > 15)
                {
                    error = $"round decimals must be between 0 and 15, got {decimals}";
                    return false;
                }
                result = Math.Round(d, decimals, MidpointRounding.AwayFromZero);
                return true;
            }

            case "multiply":
            case "add":
            {
                if (!Values.TryCoerce(value, ValueType.Number, out var number) || number is not decimal d)
                {
                    error = $"{rule.Fn} needs a number, got '{value}'";
                    return false;
                }
                var operandName = rule.Fn == "multiply" ? "by" : "amount";
                var operand = ArgAsDecimal(rule, operandName);
                if (operand is null)
                {
                    error = $"{rule.Fn} needs '{operandName}'";
                    return false;
                }
                result = rule.Fn == "multiply" ? d * operand.Value : d + operand.Value;
                return true;
            }

            case "formatDate":
            {
                var format = ArgAsString(rule, "format");
                if (string.IsNullOrEmpty(format))
                {
                    error = "formatDate needs 'format'";
                    return false;
                }
                if (!TryReadDate(AsString(value), dateOrder, out var date, out var readError))
                {
                    error = readError;
                    return false;
                }
                result = date.ToString(format, CultureInfo.InvariantCulture);
                return true;
            }

            default:
                error = $"unknown transform '{rule.Fn}'";
                return false;
        }
    }

    /// <summary>
    /// Date shapes that mean only one thing, whoever is reading them.
    /// </summary>
    /// <remarks>
    /// Year first, so there is no day-or-month question to get wrong. These are the only
    /// shapes accepted without being told the order.
    /// </remarks>
    private static readonly string[] UnambiguousFormats =
    [
        "o", "s", "u", "r",
        "yyyy-MM-ddTHH:mm:sszzz", "yyyy-MM-ddTHH:mm:ss.FFFFFFFzzz", "yyyy-MM-ddTHH:mm:ssZ",
        "yyyy-MM-ddTHH:mm:ss.FFFFFFF", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm",
        "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd", "yyyy/MM/dd", "yyyyMMdd",
    ];

    private static readonly string[] DayFirstFormats =
    [
        "dd.MM.yyyy HH:mm:ss", "dd.MM.yyyy HH:mm", "dd.MM.yyyy",
        "dd/MM/yyyy HH:mm:ss", "dd/MM/yyyy HH:mm", "dd/MM/yyyy",
        "dd-MM-yyyy HH:mm:ss", "dd-MM-yyyy HH:mm", "dd-MM-yyyy",
        "d.M.yyyy", "d/M/yyyy", "d-M-yyyy", "ddMMyyyy",
    ];

    private static readonly string[] MonthFirstFormats =
    [
        "MM/dd/yyyy HH:mm:ss", "MM/dd/yyyy HH:mm", "MM/dd/yyyy",
        "MM.dd.yyyy HH:mm:ss", "MM.dd.yyyy HH:mm", "MM.dd.yyyy",
        "MM-dd-yyyy HH:mm:ss", "MM-dd-yyyy HH:mm", "MM-dd-yyyy",
        "M/d/yyyy", "M.d.yyyy", "M-d-yyyy", "MMddyyyy",
    ];

    /// <summary>
    /// Reads a date, refusing to guess which of the day and the month comes first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The reason this exists rather than a single <c>DateTimeOffset.Parse</c>: a French
    /// partner's <c>04.09.2026</c> is the 4th of September, and the invariant parser reads
    /// it happily as the 9th of April. It <em>succeeds</em>, so nothing is reported and a
    /// shipment is dated five months wrong. Failing to parse is recoverable; parsing to the
    /// wrong day is not.
    /// </para>
    /// <para>
    /// So a date that is not year-first has to say which order it is in. Each order still
    /// accepts the unambiguous shapes too, because a partner sending mostly
    /// <c>dd/MM/yyyy</c> may well send an ISO timestamp in one field.
    /// </para>
    /// </remarks>
    private static bool TryReadDate(string text, DateOrder order, out DateTimeOffset date, out string? error)
    {
        error = null;
        string[] formats = order switch
        {
            DateOrder.DayFirst => [.. DayFirstFormats, .. UnambiguousFormats],
            DateOrder.MonthFirst => [.. MonthFirstFormats, .. UnambiguousFormats],
            _ => UnambiguousFormats,
        };

        const DateTimeStyles styles = DateTimeStyles.RoundtripKind | DateTimeStyles.AllowWhiteSpaces;
        if (DateTimeOffset.TryParseExact(text.Trim(), formats, CultureInfo.InvariantCulture, styles, out date))
            return true;

        // Naming the way out matters more than naming the failure: whoever sees this is
        // looking at a real partner document and needs to know what to change.
        error = order == DateOrder.YearFirst
            ? $"formatDate could not read '{text}' as a date. If the day or the month comes " +
              "first, say so under the source document."
            : $"formatDate could not read '{text}' as a date with the {order} order set";
        return false;
    }

    private static JToken? Arg(TransformRule rule, string name) =>
        rule.Args.TryGetValue(name, out var token) ? token : null;

    private static string? ArgAsString(TransformRule rule, string name) =>
        Arg(rule, name)?.Type is JTokenType.Null or null ? null : Arg(rule, name)!.ToObject<string>();

    private static decimal? ArgAsDecimal(TransformRule rule, string name)
    {
        var token = Arg(rule, name);
        if (token is null || token.Type is JTokenType.Null) return null;
        return token.Type is JTokenType.String
            ? decimal.TryParse(token.ToObject<string>(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null
            : token.ToObject<decimal?>();
    }

    /// <summary>
    /// A value as text, for the string functions.
    /// </summary>
    /// <remarks>
    /// Uses the same spellings <see cref="Values.TryCoerce"/> does - round-trip for numbers, and
    /// lowercase for booleans - so <c>upper</c> on a number cannot produce a different string than
    /// a <c>string</c> target would have.
    /// </remarks>
    private static string AsString(object value) =>
        Values.TryCoerce(value, ValueType.String, out var text) ? (string?)text ?? "" : "";
}
