using System.Text.RegularExpressions;
using System.Web;
using System.Xml.Linq;
using System.Xml.XPath;
using SW.Bitween.NativeAdapters;

namespace SW.Bitween;

public class XmlExchangePayloadReader : IExchangePayloadReader
{
    private readonly XDocument _doc;


    /// <summary>
    /// The payload as this reader parses it: entities decoded, stray ampersands escaped, characters
    /// XML cannot hold removed. Not what the rules-based mapper loads, so a profile of its own.
    /// </summary>
    internal static readonly ParseProfile<XDocument> Cleaned = new("cleaned xml", Load);

    public XmlExchangePayloadReader(string data) : this(data, null)
    {
    }

    /// <summary>Reads the payload's shared parse when the pipeline has one for this text.</summary>
    internal XmlExchangePayloadReader(string data, ParsedPayload shared)
    {
        if (shared == null || !shared.TryGet(Cleaned, data, out _doc))
        {
            NativeAdapters.PayloadParseTally.Parsed();
            _doc = Load(data);
        }
    }

    private static XDocument Load(string data)
    {
        var decodedData = HttpUtility.HtmlDecode((data));
        return XDocument.Parse(RemoveInvalidXmlChars(decodedData));
    }

    private static string RemoveInvalidXmlChars(string input)
    {
        input = Regex.Replace(input, @"&(?!(amp;|lt;|gt;|apos;|quot;))", "&amp;");

        return Regex.Replace(input, @"[^\x09\x0A\x0D\x20-\xD7FF\xE000-\xFFFD\x10000-x10FFFF]", "");
    }

    public bool TryGetValue(string path, out string value)
    {
        value = default;
        var node = _doc.XPathSelectElement(path);
        if (node == null) return false;
        var trimmed = node.Value.Trim();
        value = trimmed == string.Empty ? default : trimmed;
        return true;
    }

    bool IExchangePayloadReader.CanGetValues()
    {
        return true;
    }
}