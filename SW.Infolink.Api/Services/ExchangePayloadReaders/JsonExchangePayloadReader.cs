using System.Data;
using Newtonsoft.Json.Linq;

namespace SW.Infolink;

public class JsonExchangePayloadReader : IExchangePayloadReader
{
    private readonly JObject _doc;
    
    public JsonExchangePayloadReader(string data)
    {
        _doc = JObject.Parse(data);
    }
    
    public bool TryGetValue(string path, out string value)
    {
        value = default(string);
        var node = _doc.SelectToken(path);
        if (node == null) return false;
        var nodeValue = node.Value<string>();
        if (nodeValue == null) return true;
        var trimmed = nodeValue.Trim();
        value = trimmed == string.Empty ? null : trimmed;
        return true;
    }
}