using System.IO;
using Newtonsoft.Json;
using SW.Infolink.JsonConverters;

namespace SW.Infolink;

public static class MatchSpecValueConverter
{
    static readonly JsonSerializer Serializer = new JsonSerializer
    {
        Converters =
        {
            new PropertyMatchSpecificationJsonConverter()
        }
    };
    public static IPropertyMatchSpecification DeserializeMatchSpec(string data)
    {
        using StringReader sr = new StringReader(data);
        using JsonReader reader = new JsonTextReader(sr);
        return Serializer.Deserialize<IPropertyMatchSpecification>(reader);
    }

    public static string SerializeMatchSpec(IPropertyMatchSpecification spec)
    {
        using StringWriter sw = new StringWriter();
        using JsonWriter writer = new JsonTextWriter(sw);
        Serializer.Serialize(writer, spec);
        return sw.ToString();
    }
}