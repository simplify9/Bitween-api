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
        var decodedData = HttpUtility.HtmlDecode(data);
        _doc = XDocument.Parse(RemoveInvalidXmlChars(decodedData));
    }

    private static string RemoveInvalidXmlChars(string input)
    {
        const string invalidXmlCharsPattern = @"[\u0000-\u0008\u000B-\u000C\u000E-\u001F]";
        return Regex.Replace(input, invalidXmlCharsPattern, "");
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