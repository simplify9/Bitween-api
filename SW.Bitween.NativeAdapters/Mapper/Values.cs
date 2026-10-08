using System.Globalization;

namespace SW.Bitween.NativeAdapters.Mapper;

/// <summary>
/// Converting a value to the type a rule asks for, and reading a path out of a document.
/// </summary>
/// <remarks>
/// <para>
/// Both of these used to be emitted into a Scriban template as generated code, because the old
/// generator ran at save time when no value existed yet — so all it could do was write down
/// instructions to run later, in the only language available. A number cast came out as a
/// 200-character nest of <c>object.typeof</c> ternaries that could not be tested on its own. Here
/// they are ordinary methods, run at the moment the value exists.
/// </para>
/// <para>
/// Numbers are <see cref="decimal"/> throughout, not <see cref="double"/>. The documents this maps
/// are invoices and orders, so the arithmetic is money: in binary floating point a net of 100 times
/// a tax rate of 1.16 is 115.99999999999999, and that is what would be written into the document.
/// Decimal represents those fractions exactly and gives 116.00.
/// </para>
/// </remarks>
public static class Values
{
    /// <summary>
    /// A number as text, without a trailing <c>.0</c> and without scientific notation.
    /// </summary>
    /// <remarks>
    /// An order reference of 42 must not become "42.0", and a quantity must not come out as "4E+01".
    /// Trailing zeros are dropped because decimal keeps the scale of the arithmetic that produced
    /// the value — 116.00 from a multiply would otherwise reach the document with both zeros.
    /// </remarks>
    public static string FormatNumber(decimal value)
    {
        var text = value.ToString("0.############################", CultureInfo.InvariantCulture);
        return text.Length == 0 || text == "-" ? "0" : text;
    }

    /// <summary>
    /// Converts <paramref name="value"/> to <paramref name="type"/>, or returns false when it cannot.
    /// </summary>
    /// <remarks>
    /// Null converts to null for every type: a missing source field is not a failure, it is an
    /// absent value, and the writer decides how to represent that. A conversion that <em>cannot</em>
    /// be done — the string "abc" as a number — returns false so the caller can report which rule
    /// it was, rather than quietly substituting null.
    /// </remarks>
    public static bool TryCoerce(object? value, ValueType? type, out object? result)
    {
        // Values written into the rules — a fixed value, a filter's comparand, a lookup table entry
        // — are deserialised as `object`, so a whole number arrives as long and a fraction as
        // double. Reduce every CLR numeric to decimal first, or a rule holding 0 would fail to
        // convert to a number while a document holding 0 succeeded.
        value = NormaliseNumeric(value);
        result = value;

        if (type is null || value is null) return true;

        switch (type)
        {
            case ValueType.String:
                result = value switch
                {
                    string s => s,
                    bool b => b ? "true" : "false",
                    decimal m => FormatNumber(m),
                    _ => Convert.ToString(value, CultureInfo.InvariantCulture),
                };
                return true;

            case ValueType.Number:
                switch (value)
                {
                    case decimal m:
                        result = m;
                        return true;
                    case bool b:
                        result = b ? 1m : 0m;
                        return true;
                    case string s when TryParseNumber(s, out var parsed):
                        result = parsed;
                        return true;
                    default:
                        result = null;
                        return false;
                }

            case ValueType.Boolean:
                switch (value)
                {
                    case bool b:
                        result = b;
                        return true;
                    // 0 and 1 are the only numbers with an unambiguous truth value. Treating every
                    // non-zero number as true would silently turn a quantity into `true`.
                    case decimal m when m == 0m:
                        result = false;
                        return true;
                    case decimal m when m == 1m:
                        result = true;
                        return true;
                    case string s when TryParseBoolean(s, out var parsed):
                        result = parsed;
                        return true;
                    default:
                        result = null;
                        return false;
                }

            default:
                return true;
        }
    }

    /// <summary>
    /// Reduces any CLR numeric to <see cref="decimal"/>, leaving everything else alone.
    /// </summary>
    /// <remarks>
    /// A value too large for decimal — reachable only through scientific notation — is left as the
    /// double it was, so it fails a numeric conversion loudly rather than silently wrapping.
    /// </remarks>
    private static object? NormaliseNumeric(object? value) => value switch
    {
        null => null,
        decimal => value,
        byte or sbyte or short or ushort or int or uint or long or ulong =>
            Convert.ToDecimal(value, CultureInfo.InvariantCulture),
        float f when f is >= (float)decimal.MinValue and <= (float)decimal.MaxValue => (decimal)f,
        double d when d is >= (double)decimal.MinValue and <= (double)decimal.MaxValue => (decimal)d,
        _ => value,
    };

    /// <summary>
    /// The spellings of true and false that appear in real documents.
    /// </summary>
    /// <remarks>
    /// XML and CSV have no types, so a boolean arrives as whatever the sender writes — and
    /// <c>xs:boolean</c> itself permits both <c>true</c>/<c>false</c> and <c>1</c>/<c>0</c>.
    /// Anything outside this list is a failure rather than a guess.
    /// </remarks>
    private static bool TryParseBoolean(string text, out bool value)
    {
        switch (text.Trim().ToLowerInvariant())
        {
            case "true" or "1" or "yes" or "y":
                value = true;
                return true;
            case "false" or "0" or "no" or "n":
                value = false;
                return true;
            default:
                value = false;
                return false;
        }
    }

    /// <summary>
    /// Reads a dot-separated path out of a document tree.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Returns the node so the caller can tell a list apart from a value — a list rule needs the list, a
    /// field needs the value. A path that does not exist gives null, which is an absent value and
    /// not an error: optional fields are ordinary.
    /// </para>
    /// <para>
    /// Deliberately does not step into lists. The old mapper's variable layer made an array behave
    /// as its own first element, so <c>order.line.sku</c> silently meant "the first line's sku" and
    /// there was no way to say which you meant. A path into a list belongs to a list rule.
    /// </para>
    /// </remarks>
    public static ValueNode? Resolve(ValueNode? root, string? path)
    {
        if (root is null) return null;
        if (string.IsNullOrEmpty(path)) return root;

        var current = root;
        foreach (var segment in path.Split('.'))
        {
            if (current is not ObjectNode obj) return null;
            if (!obj.TryGet(segment, out var child)) return null;
            current = child;
        }

        return current;
    }

    /// <summary>The scalar at a path, or null when the path is missing or holds a list or object.</summary>
    public static object? ResolveScalar(ValueNode? root, string? path) =>
        Resolve(root, path) is ScalarNode scalar ? scalar.Value : null;

    /// <summary>
    /// Puts a value at a path of segments, creating the objects along the way.
    /// </summary>
    public static void PlaceAt(ObjectNode root, IReadOnlyList<string> target, ValueNode value)
    {
        if (target.Count == 0) return;

        var current = root;
        for (var i = 0; i < target.Count - 1; i++)
            current = current.GetOrAddObject(target[i]);

        current.Set(target[^1], value);
    }

    /// <summary>
    /// A number written in invariant form: an optional sign, currency symbol or accounting
    /// parentheses, and commas only where thousands separators belong.
    /// </summary>
    /// <remarks>
    /// <c>NumberStyles.Any</c> ignores where a comma sits, so "1,5" — one and a half, as half the
    /// world writes it — became fifteen with no error, and a partner got ten times the amount.
    /// A comma now has to separate groups of three digits ahead of the decimal point; anything
    /// else is not a number, and the mapping says so instead of guessing.
    /// </remarks>
    internal static bool TryParseNumber(string text, out decimal number)
    {
        number = 0;
        if (text.Contains(','))
        {
            var integerPart = System.Text.RegularExpressions.Regex.Match(text, @"\d[\d,]*");
            var afterInteger = text[(integerPart.Index + integerPart.Length)..];
            if (!integerPart.Success ||
                !System.Text.RegularExpressions.Regex.IsMatch(integerPart.Value, @"^\d{1,3}(,\d{3})+$") ||
                afterInteger.Contains(','))
                return false;
        }

        return decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out number);
    }
}
