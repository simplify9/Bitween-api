using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.Services.Mirror;

namespace SW.Bitween.UnitTests;

/// <summary>
/// Prod's exchange files as staging keeps them: faked in their own format, so they read the way the real
/// ones did, or not kept at all, with why. Never kept as they are.
/// </summary>
[TestClass]
public class MirrorFilesTests
{
    static readonly MirrorFaker Faker = new(Encoding.UTF8.GetBytes("a key only these tests use"), ["SHIPPED"]);

    static MirrorFakedFile Fake(string text, string name = "in.txt") => MirrorFiles.Fake(Faker, Encoding.UTF8.GetBytes(text), name);

    static string Text(MirrorFakedFile file) => Encoding.UTF8.GetString(file.Content);

    [TestMethod]
    public void Json_keeps_its_keys_and_the_words_the_setup_names()
    {
        var file = Fake("""{"orderNo":"SO-1001","status":"SHIPPED","qty":3,"lines":[{"sku":"AB-77"}]}""", "order.json");

        Assert.AreEqual("json", file.Format);
        var json = JsonNode.Parse(Text(file))!;
        Assert.AreEqual("SHIPPED", (string)json["status"]!);
        Assert.AreNotEqual("SO-1001", (string)json["orderNo"]!);
        Assert.AreEqual(1, json["qty"]!.ToJsonString().Length);
        Assert.AreNotEqual("AB-77", (string)json["lines"]![0]!["sku"]!);
        StringAssert.EndsWith(file.Name, ".json");
    }

    [TestMethod]
    public void Xml_keeps_its_names_namespaces_types_and_encoding()
    {
        const string xml = """
            <?xml version="1.0" encoding="ISO-8859-1"?>
            <o:Order xmlns:o="urn:acme:orders" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:type="o:Sale" ref="SO-1001">
              <o:Customer>Jöhn Smith</o:Customer>
              <o:Note><![CDATA[call 0791234567]]></o:Note>
              <o:Status>SHIPPED</o:Status>
            </o:Order>
            """;
        var file = MirrorFiles.Fake(Faker, Encoding.Latin1.GetBytes(xml), "order.xml");

        Assert.AreEqual("xml", file.Format);
        var text = Encoding.Latin1.GetString(file.Content);
        StringAssert.Contains(text, "encoding=\"iso-8859-1\"");
        var doc = XDocument.Parse(text);
        XNamespace o = "urn:acme:orders";
        Assert.AreEqual(o + "Order", doc.Root!.Name);
        Assert.AreEqual("o:Sale", (string)doc.Root.Attribute(XNamespace.Get("http://www.w3.org/2001/XMLSchema-instance") + "type"));
        Assert.AreNotEqual("SO-1001", (string)doc.Root.Attribute("ref"));
        Assert.AreEqual("SHIPPED", (string)doc.Root.Element(o + "Status"));
        Assert.IsFalse(text.Contains("Smith") || text.Contains("0791234567"), text);
    }

    [TestMethod]
    public void Xml_escaped_into_an_element_is_faked_as_xml()
    {
        var file = Fake("<Envelope><Body>&lt;Order&gt;&lt;Id&gt;SO-1001&lt;/Id&gt;&lt;/Order&gt;</Body></Envelope>", "soap.xml");

        var inner = XDocument.Parse(XDocument.Parse(Text(file)).Root!.Element("Body")!.Value);
        Assert.AreEqual("Order", inner.Root!.Name.LocalName);
        Assert.AreNotEqual("SO-1001", (string)inner.Root.Element("Id"));
    }

    [TestMethod]
    public void Csv_keeps_its_delimiter_quoting_and_line_endings()
    {
        var file = Fake("id;name;note\r\n1;\"Smith; John\";\"said \"\"hi\"\"\"\r\n2;Doe;SHIPPED\r\n", "orders.csv");

        Assert.AreEqual("csv", file.Format);
        var lines = Text(file).Split("\r\n");
        Assert.AreEqual(4, lines.Length);
        Assert.AreEqual("", lines[3]);
        StringAssert.Matches(lines[1], new System.Text.RegularExpressions.Regex("^[0-9];\"[A-Za-z]+; [A-Za-z]+\";\"[a-z]+ \"\"[a-z]+\"\"\"$"));
        StringAssert.EndsWith(lines[2], ";SHIPPED");
        Assert.IsFalse(Text(file).Contains("Smith"));
    }

    [TestMethod]
    public void A_workbook_keeps_its_sheets_and_formulas_and_loses_its_values()
    {
        var file = MirrorFiles.Fake(Faker, Workbook(), "report.xlsx");

        Assert.AreEqual("xlsx", file.Format);
        using var zip = new ZipArchive(new MemoryStream(file.Content));
        var strings = Read(zip, "xl/sharedStrings.xml");
        Assert.IsFalse(strings.Contains("John"), strings);
        var sheet = XDocument.Parse(Read(zip, "xl/worksheets/sheet1.xml"));
        var cells = sheet.Descendants().Where(e => e.Name.LocalName == "c").ToDictionary(c => (string)c.Attribute("r")!);
        Assert.AreEqual("0", Value(cells["A1"]));
        var number = Value(cells["B1"]);
        Assert.IsTrue(double.TryParse(number, System.Globalization.CultureInfo.InvariantCulture, out _), number);
        Assert.AreNotEqual("1234.5", number);
        Assert.AreNotEqual("SO-1001", cells["C1"].Descendants().Single(e => e.Name.LocalName == "t").Value);
        Assert.AreEqual("1", Value(cells["D1"]));
        Assert.AreEqual("SUM(B1)", cells["E1"].Elements().Single(e => e.Name.LocalName == "f").Value);
    }

    [TestMethod]
    public void What_cant_be_faked_isnt_kept_and_says_why()
    {
        foreach (var (content, format) in new (byte[], string)[]
                 {
                     (Workbook(picture: true), "xlsx"),
                     (Encoding.ASCII.GetBytes("%PDF-1.7 ..."), "pdf"),
                     ([1, 0, 2, 3], "binary"),
                     (Encoding.ASCII.GetBytes("UNA:+.? 'UNB+UNOC:3+SENDER+RECEIVER'"), "edi"),
                     (new byte[MirrorFiles.MaxBytes + 1], "large"),
                 })
        {
            var file = MirrorFiles.Fake(Faker, content, "file");
            Assert.AreEqual(format, file.Format);
            Assert.IsNull(file.Content, format);
            Assert.IsFalse(string.IsNullOrEmpty(file.NotCopied), format);
        }
        StringAssert.Contains(MirrorFiles.Fake(Faker, Workbook(picture: true), "report.xlsx").NotCopied, "pictures");
    }

    [TestMethod]
    public void Html_keeps_its_tags_and_entities()
    {
        var file = Fake("<html><body><p class=\"note\">Dear John,<br> order SO-1001 &amp; more</p></body></html>", "reply.html");

        Assert.AreEqual("html", file.Format);
        var text = Text(file);
        StringAssert.Contains(text, "<p class=\"");
        StringAssert.Contains(text, "<br>");
        StringAssert.Contains(text, "&amp;");
        Assert.IsFalse(text.Contains("John") || text.Contains("SO-1001"), text);
    }

    [TestMethod]
    public void Text_in_a_one_byte_encoding_keeps_every_byte_it_doesnt_fake()
    {
        var bytes = Encoding.Latin1.GetBytes("Café Müller, 12 rue d'Arc\n");
        var file = MirrorFiles.Fake(Faker, bytes, "note.txt");

        Assert.AreEqual(bytes.Length, file.Content.Length);
        var text = Encoding.Latin1.GetString(file.Content);
        Assert.IsFalse(text.Contains("Müller"), text);
        StringAssert.EndsWith(text, "\n");
        StringAssert.Contains(text, ", ");
    }

    // ------------------------------------------------------------------ a workbook

    static byte[] Workbook(bool picture = false)
    {
        const string main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(zip, "[Content_Types].xml", """<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"/>""");
            Write(zip, "xl/workbook.xml", $"""<workbook xmlns="{main}"><sheets><sheet name="Orders" sheetId="1"/></sheets></workbook>""");
            Write(zip, "xl/sharedStrings.xml", $"""<sst xmlns="{main}" count="1" uniqueCount="1"><si><t>John Smith</t></si></sst>""");
            Write(zip, "xl/worksheets/sheet1.xml", $"""
                <worksheet xmlns="{main}"><sheetData><row r="1">
                <c r="A1" t="s"><v>0</v></c>
                <c r="B1"><v>1234.5</v></c>
                <c r="C1" t="inlineStr"><is><t>SO-1001</t></is></c>
                <c r="D1" t="b"><v>1</v></c>
                <c r="E1"><f>SUM(B1)</f><v>1234.5</v></c>
                </row></sheetData></worksheet>
                """);
            if (picture) Write(zip, "xl/media/image1.png", "not really a picture");
        }
        return output.ToArray();
    }

    static void Write(ZipArchive zip, string name, string text)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open());
        writer.Write(text);
    }

    static string Read(ZipArchive zip, string name)
    {
        using var reader = new StreamReader(zip.GetEntry(name)!.Open());
        return reader.ReadToEnd();
    }

    static string Value(XElement cell) => cell.Elements().Single(e => e.Name.LocalName == "v").Value;
}
