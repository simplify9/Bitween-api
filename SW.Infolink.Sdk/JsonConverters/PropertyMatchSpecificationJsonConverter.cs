using System;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SW.Infolink.Model;

namespace SW.Infolink.JsonConverters;

public class PropertyMatchSpecificationJsonConverter : JsonConverter<IPropertyMatchSpecification>
{
    public override void WriteJson(JsonWriter writer, IPropertyMatchSpecification value, JsonSerializer serializer)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("type");
        writer.WriteValue(value.Name);
        if (value is AndSpec andSpec)
        {
            writer.WritePropertyName("left");
            serializer.Serialize(writer, andSpec.Left);
            //writer.WriteValue(andSpec.Left);
            
            writer.WritePropertyName("right");
            serializer.Serialize(writer, andSpec.Right);
            //writer.WriteValue(andSpec.Right);
        }
        else if (value is OrSpec orSpec)
        {
            writer.WritePropertyName("left");
            serializer.Serialize(writer, orSpec.Left);
            //writer.WriteValue(orSpec.Left);
            
            writer.WritePropertyName("right");
            serializer.Serialize(writer, orSpec.Right);
            //writer.WriteValue(orSpec.Right);
        }
        else if (value is OneOfSpec spec)
        {
            writer.WritePropertyName("path");
            writer.WriteValue(spec.Path);
            
            writer.WritePropertyName("values");
            serializer.Serialize(writer, spec.Values);
        }
        else if (value is NotOneOfSpec notOneOfSpec)
        {
            writer.WritePropertyName("path");
            writer.WriteValue(notOneOfSpec.Path);
            
            writer.WritePropertyName("values");
            serializer.Serialize(writer, notOneOfSpec.Values);
        }
        
        writer.WriteEndObject();
    }

    IPropertyMatchSpecification EvaluateAnd(JObject jObj)
    {
        var jLeft = jObj.Property("left")?.Value;
        var jRight = jObj.Property("right")?.Value;
        if (jLeft is JObject jLeftObj && jRight is JObject jRightObj)
        {
            return new AndSpec(Evaluate(jLeftObj), Evaluate(jRightObj));
        }
        
        throw new JsonSerializationException("Invalid Match Specification Format");
    }
    
    IPropertyMatchSpecification EvaluateOr(JObject jObj)
    {
        var jLeft = jObj.Property("left")?.Value;
        var jRight = jObj.Property("right")?.Value;
        if (jLeft is JObject jLeftObj && jRight is JObject jRightObj)
        {
            return new OrSpec(Evaluate(jLeftObj), Evaluate(jRightObj));
        }
        
        throw new JsonSerializationException("Invalid Match Specification Format");
    }

    IPropertyMatchSpecification EvaluateOneOf(JObject jObj)
    {
        var jPath = jObj.Property("path")?.Value;
        var jValues = jObj.Property("values")?.Value;
        if (jPath is JValue vPath && vPath.Type == JTokenType.String && vPath.Value is string path &&
            jValues is JArray jArr)
        {
            var values = jArr.Children().Select(c => c.ToObject<string>()).Where(s => s is not null);
            return new OneOfSpec(path, values);
        }
        
        throw new JsonSerializationException("Invalid Match Specification Format");
    }
    
    IPropertyMatchSpecification EvaluateNotOneOf(JObject jObj)
    {
        var jPath = jObj.Property("path")?.Value;
        var jValues = jObj.Property("values")?.Value;
        if (jPath is JValue vPath && vPath.Type == JTokenType.String && vPath.Value is string path &&
            jValues is JArray jArr)
        {
            var values = jArr.Children().Select(c => c.ToObject<string>()).Where(s => s is not null);
            return new NotOneOfSpec(path, values);
        }
        
        throw new JsonSerializationException("Invalid Match Specification Format");
    }
    
    IPropertyMatchSpecification Evaluate(JObject jObj)
    {
        var type = jObj.Property("type");
        if (type != null && type.Value is JValue v && v.Type == JTokenType.String && v.Value is string matchType)
        {
            switch (matchType)
            {
                case "and":
                    return EvaluateAnd(jObj);

                case "or":
                    return EvaluateOr(jObj);
                
                case "one_of":
                    return EvaluateOneOf(jObj);
                
                case "not_one_of":
                    return EvaluateNotOneOf(jObj);
            }
        }
        
        throw new JsonSerializationException("Invalid Match Specification Format");
    }
    
    public override IPropertyMatchSpecification ReadJson(JsonReader reader, Type objectType, IPropertyMatchSpecification existingValue,
        bool hasExistingValue, JsonSerializer serializer)
    {
        var json = serializer.Deserialize<JToken>(reader);
        if (json is JObject jObj)
        {
            return Evaluate(jObj);
        }

        return null;
    }
}