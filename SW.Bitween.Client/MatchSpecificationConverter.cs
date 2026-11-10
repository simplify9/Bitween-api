using System.Text.Json;
using System.Text.Json.Serialization;
using SW.Bitween.Model;

namespace SW.Bitween.Client;

/// <summary>
/// A match expression - a subscription's or a bus gateway route's - as the API writes and reads it:
/// <c>{ "type": "one_of", "path", "values" }</c>, <c>not_one_of</c> the same, and <c>and</c> / <c>or</c>
/// with <c>left</c> and <c>right</c>. The model's own converter is Newtonsoft's; without this one the
/// client would send an expression as its interface, the name alone, and the server would drop it.
/// </summary>
sealed class MatchSpecificationConverter : JsonConverter<IPropertyMatchSpecification>
{
    public override IPropertyMatchSpecification? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        using var document = JsonDocument.ParseValue(ref reader);
        return Read(document.RootElement);
    }

    static IPropertyMatchSpecification? Read(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Null) return null;
        if (element.ValueKind != JsonValueKind.Object) throw new JsonException("A match expression is an object.");
        var type = Property(element, "type")?.GetString();
        switch (type)
        {
            case "one_of":
                return new OneOfSpec(Path(element), Values(element));
            case "not_one_of":
                return new NotOneOfSpec(Path(element), Values(element));
            case "and":
            case "or":
                var left = Property(element, "left") is { } l ? Read(l) : null;
                var right = Property(element, "right") is { } r ? Read(r) : null;
                // As the server reads it: one side alone is that side.
                if (left == null || right == null)
                    return left ?? right ?? throw new JsonException($"An '{type}' expression needs a left or a right.");
                return type == "and" ? new AndSpec(left, right) : new OrSpec(left, right);
            default:
                throw new JsonException($"'{type}' isn't a match expression type: one_of, not_one_of, and or or.");
        }
    }

    static JsonElement? Property(JsonElement element, string name)
    {
        foreach (var property in element.EnumerateObject())
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                return property.Value;
        return null;
    }

    static string Path(JsonElement element) =>
        Property(element, "path")?.GetString() ?? throw new JsonException("A match expression needs a path.");

    static IEnumerable<string> Values(JsonElement element) =>
        Property(element, "values") is { ValueKind: JsonValueKind.Array } values
            ? values.EnumerateArray().Select(v => v.ToString()).ToArray()
            : throw new JsonException("A match expression needs its values.");

    public override void Write(Utf8JsonWriter writer, IPropertyMatchSpecification value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("type", value.Name);
        switch (value)
        {
            case OneOfSpec one:
                WriteValues(writer, one.Path, one.Values);
                break;
            case NotOneOfSpec notOne:
                WriteValues(writer, notOne.Path, notOne.Values);
                break;
            case AndSpec and:
                WriteSides(writer, and.Left, and.Right, options);
                break;
            case OrSpec or:
                WriteSides(writer, or.Left, or.Right, options);
                break;
            default:
                throw new JsonException($"{value.GetType().Name} isn't a match expression the API reads.");
        }
        writer.WriteEndObject();
    }

    static void WriteValues(Utf8JsonWriter writer, string path, string[] values)
    {
        writer.WriteString("path", path);
        writer.WriteStartArray("values");
        foreach (var v in values) writer.WriteStringValue(v);
        writer.WriteEndArray();
    }

    void WriteSides(Utf8JsonWriter writer, IPropertyMatchSpecification left, IPropertyMatchSpecification right,
        JsonSerializerOptions options)
    {
        writer.WritePropertyName("left");
        Write(writer, left, options);
        writer.WritePropertyName("right");
        Write(writer, right, options);
    }
}
