using System.Data;
using System.Linq;
using Newtonsoft.Json.Linq;
using SW.Bitween.NativeAdapters;
using SW.PrimitiveTypes;

namespace SW.Bitween;

public class JsonExchangePayloadReader : IExchangePayloadReader
{
    private readonly JObject _doc;
    private readonly JArray _docArray;

    public JsonExchangePayloadReader(string data) : this(data, null)
    {
    }

    /// <summary>
    /// Reads the payload's shared parse when the pipeline has one for this text. It is
    /// the same tree: <c>JArray.Parse</c> and <c>JObject.Parse</c> are <c>JToken.Parse</c> with the
    /// same reader and settings, for text that opens with the bracket each expects.
    /// </summary>
    internal JsonExchangePayloadReader(string data, ParsedPayload shared)
    {
        if (string.IsNullOrEmpty(data))
            return;

        if (data.StartsWith("["))
        {
            if (shared != null && shared.TryGet(ParseProfiles.Defaults, data, out var tree) && tree is JArray array)
                _docArray = array;
            else
            {
                NativeAdapters.PayloadParseTally.Parsed();
                _docArray = JArray.Parse(data);
            }
        }

        if (data.StartsWith("{"))
        {
            if (shared != null && shared.TryGet(ParseProfiles.Defaults, data, out var tree) && tree is JObject obj)
                _doc = obj;
            else
            {
                NativeAdapters.PayloadParseTally.Parsed();
                _doc = JObject.Parse(data);
            }
        }
    }

    public bool TryGetValue(string path, out string value)
    {
        value = default;
        if (_doc is not null)
        {
            var node = _doc.SelectToken(path);
            if (node == null) return false;
            var nodeValue = node is JValue jv ? jv.Value<string>() : node.ToString();
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