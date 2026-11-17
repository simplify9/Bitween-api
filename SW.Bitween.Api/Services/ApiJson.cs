using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SW.Bitween.Services;

/// <summary>
/// The API's JSON settings - camelCase, enums as names, Bitween's own converters - for answers given
/// outside the API's controllers, such as the MCP tools, so they read exactly as the API's do.
/// </summary>
public sealed class ApiJson(JsonSerializer serializer)
{
    public string Write(object value)
    {
        using var writer = new StringWriter();
        serializer.Serialize(writer, value);
        return writer.ToString();
    }

    public JToken ToToken(object value) => value is null ? JValue.CreateNull() : JToken.FromObject(value, serializer);
}
