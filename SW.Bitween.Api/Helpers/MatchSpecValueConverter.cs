using System.IO;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Newtonsoft.Json;
using SW.Bitween.JsonConverters;
using SW.Bitween.Model;

namespace SW.Bitween;

public static class MatchSpecValueConverter
{
    /// <summary>
    /// Applies the standard MatchExpression string&lt;-&gt;<see cref="IPropertyMatchSpecification"/>
    /// conversion. Shared so BusGatewayRoute and Subscription can't drift from each other.
    /// </summary>
    public static PropertyBuilder<IPropertyMatchSpecification> HasMatchExpressionConversion(
        this PropertyBuilder<IPropertyMatchSpecification> builder)
    {
        return builder.HasConversion(
            domainObject => domainObject == null ? null : SerializeMatchSpec(domainObject),
            dbString => dbString == null ? null : DeserializeMatchSpec(dbString));
    }

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