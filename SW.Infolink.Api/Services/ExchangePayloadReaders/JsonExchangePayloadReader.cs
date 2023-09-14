using System.Data;
using System.Linq;
using Newtonsoft.Json.Linq;
using SW.PrimitiveTypes;

namespace SW.Infolink;

public class JsonExchangePayloadReader : IExchangePayloadReader
{
    private readonly JObject _doc;
    private readonly JArray _docArray;

    public JsonExchangePayloadReader(string data)
    {
        if (string.IsNullOrEmpty(data))
            return;

        if (data.StartsWith("["))
        {
            _docArray = JArray.Parse(data);
        }

        if (data.StartsWith("{"))
        {
            _doc = JObject.Parse(data);
        }
    }

    public bool TryGetValue(string path, out string value)
    {
        value = default;
        if (_doc is not null)
        {
            var node = _doc.SelectToken(path);
            if (node == null) return false;
            var nodeValue = node.Value<string>();
            if (nodeValue == null) return true;
            var trimmed = nodeValue.Trim();
            value = trimmed == string.Empty ? null : trimmed;
            return true;
        }

        if (_docArray is not null)
        {
            return false;
        }

        return false;
    }

    public bool CanGetValues()
    {
        return _doc != null;
    }
}