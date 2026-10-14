using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SW.Bitween.NativeAdapters.Mapper.Formats;

/// <summary>
/// Reads and writes JSON.
/// </summary>
/// <remarks>
/// Writing goes through <see cref="JToken"/> rather than assembling text, which is the whole reason
/// this mapper needs no <c>| json</c> filter, no trailing-comma repair, and no final parse of its
/// own output to check it. A value stays a number, a string or a boolean until this point, and
/// Newtonsoft writes the quotes, commas and escapes because it holds the structure and knows where
/// each value begins and ends.
/// </remarks>
public class JsonFormat : IDocumentFormat
{
    public string Id => "json";

    public string ContentType => "application/json";

    /// <summary>No: an array is an array, and one element in it is a list of one.</summary>
    public bool SingleValueIsAList => false;

    public ValueNode Read(string text) => Read(text, null);

    /// <summary>
    /// <see cref="Read(string)"/> from the payload's shared parse when the pipeline has one for this
    /// text (finding 6c), parsed here otherwise. The tree is only read.
    /// </summary>
    internal ValueNode Read(string text, ParsedPayload? shared)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new DocumentFormatException("The document is empty, so there is nothing to map.");

        if (shared == null || !shared.TryGet(ParseProfiles.Exact, text, out var token))
        {
            PayloadParseTally.Parsed();
            token = ParseExact(text);
        }

        return FromToken(token);
    }

    /// <summary>The text as this format reads it, the one parse profile it shares as <see cref="ParseProfiles.Exact"/>.</summary>
    internal static JToken ParseExact(string text)
    {
        try
        {
            // FloatParseHandling.Decimal so a price arrives exact. The default gives double, and
            // 1.16 has no exact binary representation - the arithmetic downstream is money.
            using var reader = new JsonTextReader(new StringReader(text))
            {
                FloatParseHandling = FloatParseHandling.Decimal,
                DateParseHandling = DateParseHandling.None,
            };
            var token = JToken.ReadFrom(reader);

            // ReadFrom stops at the end of the first value, so two documents concatenated would
            // read as the first one and the rest would be dropped without a word. Reading one more
            // token settles it - the reader itself objects to trailing content, and a token that
            // does arrive means there is a second value.
            if (reader.Read())
                throw new DocumentFormatException(
                    "The document has more than one top-level value; JSON allows exactly one.");

            return token;
        }
        catch (JsonException ex)
        {
            throw new DocumentFormatException($"The document is not valid JSON: {ex.Message}");
        }
    }

    public string Write(ValueNode root) => ToToken(root).ToString(Formatting.Indented);

    private static ValueNode FromToken(JToken token)
    {
        switch (token)
        {
            case JObject obj:
            {
                var node = ValueNode.Object();
                foreach (var property in obj.Properties())
                    node.Set(property.Name, FromToken(property.Value));
                return node;
            }

            case JArray array:
            {
                var node = ValueNode.List();
                foreach (var item in array) node.Add(FromToken(item));
                return node;
            }

            case JValue value:
                return ValueNode.Value(Normalise(value.Value));

            default:
                return ValueNode.Value(null);
        }
    }

    /// <summary>
    /// Reduces JSON's numeric types to <see cref="decimal"/>.
    /// </summary>
    /// <remarks>
    /// Newtonsoft hands back <c>long</c> for a whole number, so without this the same rule would
    /// behave differently for <c>1</c> and <c>1.5</c>. One numeric type through the mapper means one
    /// set of conversion rules to test. A value too large for decimal - only reachable through
    /// scientific notation - is kept as text rather than silently losing precision.
    /// </remarks>
    private static object? Normalise(object? value) => value switch
    {
        null => null,
        decimal m => m,
        long l => (decimal)l,
        int i => (decimal)i,
        double d => d is >= (double)decimal.MinValue and <= (double)decimal.MaxValue
            ? (decimal)d
            : d.ToString("R", CultureInfo.InvariantCulture),
        float f => (decimal)f,
        // An integer past long's range - a 20-digit reference, say - comes back as BigInteger.
        System.Numerics.BigInteger big =>
            big >= new System.Numerics.BigInteger(decimal.MinValue) &&
            big <= new System.Numerics.BigInteger(decimal.MaxValue)
                ? (decimal)big
                : big.ToString(CultureInfo.InvariantCulture),
        bool b => b,
        string s => s,
        _ => value.ToString(),
    };

    private static JToken ToToken(ValueNode node)
    {
        switch (node)
        {
            case ObjectNode obj:
            {
                var token = new JObject();
                foreach (var (key, child) in obj.Children()) token[key] = ToToken(child);
                return token;
            }

            case ListNode list:
            {
                var token = new JArray();
                foreach (var item in list.Items) token.Add(ToToken(item));
                return token;
            }

            // A whole number is written as an integer, so a quantity of 2 reaches the document as
            // `2`. Newtonsoft writes a decimal as `2.0`, and a partner reading a quantity or an
            // order reference has no reason to expect a decimal point that the source never had.
            case ScalarNode { Value: decimal m } when m == decimal.Truncate(m)
                                                      && m >= long.MinValue && m <= long.MaxValue:
                return new JValue((long)m);

            // A fixed value that is a list or an object, as ["Email"]: the rules keep its JSON type,
            // so it is written as the JSON it is rather than as one value.
            case ScalarNode { Value: JContainer container }:
                return container.DeepClone();

            case ScalarNode scalar:
                return scalar.Value is null ? JValue.CreateNull() : new JValue(scalar.Value);

            default:
                return JValue.CreateNull();
        }
    }
}
