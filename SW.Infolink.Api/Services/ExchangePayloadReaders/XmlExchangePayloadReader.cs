using System.Text.RegularExpressions;
using System.Web;
using System.Xml.Linq;
using System.Xml.XPath;

namespace SW.Infolink;

public class XmlExchangePayloadReader : IExchangePayloadReader
{
    private readonly XDocument _doc;


    public XmlExchangePayloadReader(string data)
    {
        var decodedData = HttpUtility.HtmlDecode((data));
        _doc = XDocument.Parse(RemoveInvalidXmlChars(decodedData));
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
}