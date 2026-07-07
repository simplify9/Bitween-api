using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SW.Bitween.Model;

namespace SW.Bitween.JsonConverters;

public class DelayStrategyJsonConverter : JsonConverter<DelayStrategy>
{
    public override void WriteJson(JsonWriter writer, DelayStrategy value, JsonSerializer serializer)
    {
        writer.WriteStartObject();

        switch (value)
        {
            case FixedDelayStrategy fixedDelay:
                writer.WritePropertyName("type");
                writer.WriteValue("fixed");
                writer.WritePropertyName("delayMs");
                writer.WriteValue(fixedDelay.DelayMs);
                break;

            case LinearDelayStrategy linear:
                writer.WritePropertyName("type");
                writer.WriteValue("linear");
                writer.WritePropertyName("initialDelayMs");
                writer.WriteValue(linear.InitialDelayMs);
                writer.WritePropertyName("incrementMs");
                writer.WriteValue(linear.IncrementMs);
                break;

            case ExponentialDelayStrategy exponential:
                writer.WritePropertyName("type");
                writer.WriteValue("exponential");
                writer.WritePropertyName("initialDelayMs");
                writer.WriteValue(exponential.InitialDelayMs);
                writer.WritePropertyName("multiplier");
                writer.WriteValue(exponential.Multiplier);
                writer.WritePropertyName("maxDelayMs");
                writer.WriteValue(exponential.MaxDelayMs);
                break;

            default:
                throw new JsonSerializationException($"Unknown DelayStrategy type '{value.GetType()}'");
        }

        writer.WriteEndObject();
    }

    public override DelayStrategy ReadJson(JsonReader reader, Type objectType, DelayStrategy existingValue,
        bool hasExistingValue, JsonSerializer serializer)
    {
        var jObject = serializer.Deserialize<JObject>(reader);
        if (jObject is null) return null;

        var type = jObject.Property("type")?.Value?.ToString();
        switch (type)
        {
            case "fixed":
                return new FixedDelayStrategy
                {
                    DelayMs = jObject.Property("delayMs")?.Value?.ToObject<int>() ?? 0
                };

            case "linear":
                return new LinearDelayStrategy
                {
                    InitialDelayMs = jObject.Property("initialDelayMs")?.Value?.ToObject<int>() ?? 0,
                    IncrementMs = jObject.Property("incrementMs")?.Value?.ToObject<int>() ?? 0
                };

            case "exponential":
                return new ExponentialDelayStrategy
                {
                    InitialDelayMs = jObject.Property("initialDelayMs")?.Value?.ToObject<int>() ?? 0,
                    Multiplier = jObject.Property("multiplier")?.Value?.ToObject<double>() ?? 2.0,
                    MaxDelayMs = jObject.Property("maxDelayMs")?.Value?.ToObject<int>() ?? 30_000
                };

            default:
                throw new JsonSerializationException($"Unknown or missing DelayStrategy discriminator 'type': '{type}'");
        }
    }
}
