using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.NativeAdapters.Mapper;
using SW.Bitween.NativeAdapters.Mapper.Formats;

namespace SW.Bitween.UnitTests.NativeMapper;

/// <summary>
/// Reading XML into the neutral tree.
/// </summary>
/// <remarks>
/// The shapes here are taken from documents real partners send - SOAP requests and responses from
/// two carriers - with the credentials replaced. Every one of them was in the samples: the same URI
/// bound to a prefix and to the default at once, <c>xmlns=""</c> putting a subtree back into no
/// namespace, empty elements by the dozen, and a postcode with a leading space.
/// </remarks>
[TestClass]
public class XmlFormatReadTests
{
    private static readonly XmlFormat Format = new();

    /// <summary>The value at a path, so a test reads the way a rule does.</summary>
    private static object At(ValueNode root, string path) => Values.ResolveScalar(root, path);

    private static ValueNode NodeAt(ValueNode root, string path) => Values.Resolve(root, path);

    private static DocumentFormatException Refuses(string text)
    {
        var thrown = Assert.ThrowsException<DocumentFormatException>(() => Format.Read(text));
        Assert.IsFalse(string.IsNullOrWhiteSpace(thrown.Message), "a refusal must explain itself");
        return thrown;
    }

    /// <summary><c>&lt;a&gt;&lt;a&gt;…&lt;/a&gt;&lt;/a&gt;</c>, nested <paramref name="depth"/> elements deep.</summary>
    private static string Nested(int depth) =>
        string.Concat(Enumerable.Repeat("<a>", depth)) + string.Concat(Enumerable.Repeat("</a>", depth));

    [TestMethod]
    public void A_document_nested_past_the_limit_is_refused_rather_than_read()
    {
        // Not a style preference: the reader recurses once per element, so without this the
        // document below takes the whole process down with a StackOverflowException, which is the
        // one failure no catch anywhere can turn into a failed exchange.
        var thrown = Refuses(Nested(5_000));

        StringAssert.Contains(thrown.Message, "64");
    }

    [TestMethod]
    public void A_document_at_the_limit_is_still_read()
    {
        // The limit has to sit well past anything real, or a legitimate document is refused for a
        // problem it does not have. A SOAP envelope carrying an order reaches about ten.
        var tree = Format.Read(Nested(64));

        Assert.IsNotNull(tree, "64 deep is inside the limit");
    }

    [TestMethod]
    public void The_root_element_is_a_named_key_so_it_appears_in_every_path()
    {
        var tree = Format.Read("<order><ref>A1</ref></order>");

        // Not the tree itself: the root name is the one name that says what the document is.
        Assert.AreEqual("A1", At(tree, "order.ref"));
        Assert.IsNull(At(tree, "ref"), "the root must not be skipped");
    }

    [TestMethod]
    public void A_prefix_is_not_part_of_the_path()
    {
        var tree = Format.Read(
            """
            <s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/">
              <s:Body>
                <CreateShipment xmlns="http://www.cargonet.software">
                  <request><referencenumber>BSO-01157</referencenumber></request>
                </CreateShipment>
              </s:Body>
            </s:Envelope>
            """);

        Assert.AreEqual("BSO-01157", At(tree, "Envelope.Body.CreateShipment.request.referencenumber"));
    }

    [TestMethod]
    public void The_same_namespace_under_two_prefixes_reads_as_one_thing()
    {
        // Both from real documents: one binds the same URI to `h:` and to the default at once, the
        // other uses `soap:` and `s:` for the envelope in the same breath. A path naming a prefix
        // would be naming something the partner changes freely.
        var declaredTwice = Format.Read(
            """
            <h:UserCredentials xmlns="http://www.cargonet.software"
                               xmlns:h="http://www.cargonet.software">
              <userid>ACCOUNT</userid>
            </h:UserCredentials>
            """);
        Assert.AreEqual("ACCOUNT", At(declaredTwice, "UserCredentials.userid"));

        var twoPrefixes = Format.Read(
            """
            <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">
              <s:Header xmlns:s="http://schemas.xmlsoap.org/soap/envelope/"/>
              <soap:Body><ok>1</ok></soap:Body>
            </soap:Envelope>
            """);
        Assert.AreEqual("1", At(twoPrefixes, "Envelope.Body.ok"));
        Assert.AreEqual("", At(twoPrefixes, "Envelope.Header"));
    }

    [TestMethod]
    public void A_subtree_put_back_into_no_namespace_reads_like_any_other()
    {
        // `xmlns=""` un-declares the default namespace. Chronopost does it for every section of
        // the request body, which is what makes ignoring namespaces the only workable rule.
        var tree = Format.Read(
            """
            <shipping xmlns="http://cxf.shipping.soap.chronopost.fr/">
              <headerValue xmlns="">
                <accountNumber>55480501</accountNumber>
              </headerValue>
            </shipping>
            """);

        Assert.AreEqual("55480501", At(tree, "shipping.headerValue.accountNumber"));
    }

    [TestMethod]
    public void An_empty_element_reads_as_present_and_empty()
    {
        var tree = Format.Read("<v><a/><b></b><c>x</c></v>");

        // `<a/>` and `<b></b>` are the same document, so they must read the same. Empty rather than
        // missing, because the element is there - and a rule can tell the two apart.
        Assert.AreEqual("", At(tree, "v.a"));
        Assert.AreEqual("", At(tree, "v.b"));
        Assert.IsNull(NodeAt(tree, "v.missing"), "an element that is not there reads as nothing");
    }

    [TestMethod]
    public void A_name_used_more_than_once_is_a_list()
    {
        var tree = Format.Read(
            "<order><line><sku>A</sku></line><line><sku>B</sku></line></order>");

        var list = NodeAt(tree, "order.line") as ListNode;
        Assert.IsNotNull(list, "a repeated name is how XML writes a list");
        Assert.AreEqual(2, list!.Items.Count);
        Assert.AreEqual("A", Values.ResolveScalar(list.Items[0], "sku"));
        Assert.AreEqual("B", Values.ResolveScalar(list.Items[1], "sku"));
    }

    [TestMethod]
    public void A_name_used_once_is_the_value_itself_not_a_list_of_one()
    {
        // XML cannot say which. The tree reflects the document; making a single occurrence walk as
        // a list of one is the mapper's job, not the reader's.
        var tree = Format.Read("<order><line><sku>A</sku></line></order>");

        Assert.IsInstanceOfType<ObjectNode>(NodeAt(tree, "order.line"));
        Assert.AreEqual("A", At(tree, "order.line.sku"));
    }

    [TestMethod]
    public void An_attribute_is_its_name_with_an_at_sign()
    {
        var tree = Format.Read("""<label type="PDF_A6" size="A6"><name>front</name></label>""");

        Assert.AreEqual("PDF_A6", At(tree, "label.@type"));
        Assert.AreEqual("A6", At(tree, "label.@size"));
        Assert.AreEqual("front", At(tree, "label.name"));
    }

    [TestMethod]
    public void Text_beside_an_attribute_or_a_child_is_hash_text()
    {
        var withAttribute = Format.Read("""<weight unit="kg">0.940</weight>""");
        Assert.AreEqual("0.940", At(withAttribute, "weight.#text"));
        Assert.AreEqual("kg", At(withAttribute, "weight.@unit"));

        // Mixed content is rare in a partner's document, but dropping the text would lose data
        // silently, which is the one outcome worth ruling out.
        var mixed = Format.Read("<note>see <ref>A1</ref></note>");
        Assert.AreEqual("A1", At(mixed, "note.ref"));
        Assert.AreEqual("see ", At(mixed, "note.#text"));
    }

    [TestMethod]
    public void A_namespace_declaration_is_not_data()
    {
        var tree = Format.Read(
            """
            <s:Body xmlns:s="http://schemas.xmlsoap.org/soap/envelope/"
                    xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                    xmlns:xsd="http://www.w3.org/2001/XMLSchema">
              <ok>1</ok>
            </s:Body>
            """);

        // Four of these sit at the top of a real SOAP body. In the tree they would push the fields
        // anyone is looking for below a screenful of machinery.
        var body = NodeAt(tree, "Body") as ObjectNode;
        Assert.IsNotNull(body);
        CollectionAssert.AreEqual(new[] { "ok" }, body!.Keys.ToArray());
    }

    [TestMethod]
    public void A_value_arrives_exactly_as_the_document_wrote_it()
    {
        var tree = Format.Read(
            """
            <a>
              <zipCode> 64310</zipCode>
              <weight>0.940</weight>
              <height>0</height>
              <city>Saint-Pée-sur-Nivelle</city>
              <phone>+33- 6 50 94 98 60</phone>
            </a>
            """);

        // The leading space is in the real document. Trimming it here would be the reader deciding
        // that a partner's data is wrong; there is a `trim` transform for saying so on purpose.
        Assert.AreEqual(" 64310", At(tree, "a.zipCode"));

        // Text, not a number: XML has no types, `0.940` would lose its shape as a decimal for no
        // reason, and a rule's own type is where converting belongs.
        Assert.AreEqual("0.940", At(tree, "a.weight"));
        Assert.AreEqual("0", At(tree, "a.height"));
        Assert.AreEqual("Saint-Pée-sur-Nivelle", At(tree, "a.city"));
        Assert.AreEqual("+33- 6 50 94 98 60", At(tree, "a.phone"));
    }

    [TestMethod]
    public void A_base64_payload_arrives_whole()
    {
        // A label response carries a PDF this way, tens of kilobytes of it, in the middle of the
        // document rather than at the end.
        var payload = new string('A', 60_000);
        var tree = Format.Read($"<r><errorCode>0</errorCode><skybill>{payload}</skybill><n>7</n></r>");

        Assert.AreEqual(payload, At(tree, "r.skybill"));
        Assert.AreEqual("7", At(tree, "r.n"), "the fields after it still read");
    }

    [TestMethod]
    public void Cdata_is_text_and_comments_are_not_in_the_tree()
    {
        var tree = Format.Read("<a><!-- note --><b><![CDATA[<raw> & ok]]></b></a>");

        Assert.AreEqual("<raw> & ok", At(tree, "a.b"));
        var a = NodeAt(tree, "a") as ObjectNode;
        CollectionAssert.AreEqual(new[] { "b" }, a!.Keys.ToArray());
    }

    [TestMethod]
    public void A_document_that_is_not_xml_says_so()
    {
        StringAssert.Contains(Refuses("{\"not\":\"xml\"}").Message, "not valid XML");
        StringAssert.Contains(Refuses("<a><b></a>").Message, "not valid XML");
        StringAssert.Contains(Refuses("   ").Message, "empty");
    }

    [TestMethod]
    public void A_document_that_declares_its_own_entities_is_refused()
    {
        // The billion-laughs expansion, and the file-reading variant. A partner's document is
        // untrusted input; this one is refused before it can expand or fetch anything.
        var bomb =
            """
            <?xml version="1.0"?>
            <!DOCTYPE lolz [<!ENTITY lol "lol"><!ENTITY lol2 "&lol;&lol;&lol;&lol;">]>
            <lolz>&lol2;</lolz>
            """;
        Refuses(bomb);

        var external =
            """
            <?xml version="1.0"?>
            <!DOCTYPE f [<!ENTITY xxe SYSTEM "file:///etc/passwd">]>
            <f>&xxe;</f>
            """;
        Refuses(external);
    }
}

/// <summary>
/// Walking an XML list, where the document cannot say whether there is one entry or none.
/// </summary>
/// <remarks>
/// The failure this guards against is the quietest one in XML mapping: an order with a single line
/// maps to no lines, the mapping reports success, and the line is gone. It only shows up in
/// production, on the one order that happened to have one item.
/// </remarks>
[TestClass]
public class XmlSingleEntryListTests
{
    private static readonly XmlFormat Xml = new();
    private static readonly JsonFormat Json = new();

    private static ListNode Lines(IDocumentFormat format, string document, string over)
    {
        var rules = new MappingRules
        {
            Lists =
            [
                new ListRule
                {
                    Over = over, As = "line", Target = ["lines"],
                    Fields = [new FieldRule
                    {
                        Target = ["code"],
                        From = new ValueSource { Kind = ValueSourceKind.Path, Path = "sku" },
                    }],
                },
            ],
        };

        var output = DocumentMapper.Map(rules, format.Read(document), MappingContext.Empty,
            format.SingleValueIsAList);
        return (ListNode)Values.Resolve(output, "lines");
    }

    private static string[] Codes(ListNode list) =>
        list.Items.Select(i => (string)Values.ResolveScalar(i, "code")).ToArray();

    [TestMethod]
    public void Three_repeated_elements_are_three_entries()
    {
        var lines = Lines(Xml,
            """
            <order>
              <line><sku>A1</sku></line>
              <line><sku>B7</sku></line>
              <line><sku>C2</sku></line>
            </order>
            """, "order.line");

        CollectionAssert.AreEqual(new[] { "A1", "B7", "C2" }, Codes(lines));
    }

    [TestMethod]
    public void One_element_is_one_entry_not_none()
    {
        // The whole reason the trait exists. `<line>` used once reads as an object, so without it
        // the list rule finds no list and produces nothing - losing the only line in silence.
        var lines = Lines(Xml, "<order><line><sku>A1</sku></line></order>", "order.line");

        CollectionAssert.AreEqual(new[] { "A1" }, Codes(lines));
    }

    [TestMethod]
    public void An_element_that_is_not_there_is_still_no_entries()
    {
        // Absent is absent. An order with no lines at all is ordinary, and inventing an empty entry
        // for it would put a blank line into the partner's document.
        Assert.AreEqual(0, Lines(Xml, "<order><ref>A1</ref></order>", "order.line").Items.Count);
    }

    [TestMethod]
    public void The_document_itself_is_not_a_list_of_one()
    {
        // `over: ""` says the whole document is the list, which is a claim about a root array
        // and never true of XML - a document has exactly one root, and it is not a repeated
        // element. It is also what a list rule holds before a source list has been chosen, so
        // forgiving it here made an unconfigured list quietly produce one entry: the document,
        // with every field inside it resolving to nothing.
        Assert.AreEqual(0, Lines(Xml, "<order><parcel><sku>A</sku></parcel></order>", "").Items.Count);
    }

    [TestMethod]
    public void Json_is_unchanged_because_json_says_which()
    {
        // A JSON object at the path is not a list and never was, and the mapper has always read it
        // as no entries. XML's problem must not become JSON's behaviour.
        Assert.AreEqual(0, Lines(Json, """{"order":{"line":{"sku":"A1"}}}""", "order.line").Items.Count);
        CollectionAssert.AreEqual(new[] { "A1" },
            Codes(Lines(Json, """{"order":{"line":[{"sku":"A1"}]}}""", "order.line")));
    }
}

/// <summary>
/// Writing the neutral tree back out as XML.
/// </summary>
/// <remarks>
/// The conventions are the reader's, in reverse: <c>@name</c> becomes an attribute, <c>#text</c>
/// becomes the element's own text, and a list becomes the same element repeated. Namespaces are
/// ordinary <c>@xmlns</c> fields, which is what they are in XML - so declaring one needs no
/// machinery of its own, and the prefixes in the output are the ones the mapping asked for.
/// </remarks>
[TestClass]
public class XmlFormatWriteTests
{
    private static readonly XmlFormat Format = new();

    /// <summary>The document, with newlines normalised so an expectation holds on any platform.</summary>
    private static string Written(ValueNode tree) => Format.Write(tree).Replace("\r\n", "\n");

    private static ObjectNode Obj(params (string Key, ValueNode Node)[] children)
    {
        var node = ValueNode.Object();
        foreach (var (key, child) in children) node.Set(key, child);
        return node;
    }

    private static ValueNode V(object value) => ValueNode.Value(value);

    private static ListNode L(params ValueNode[] items)
    {
        var list = ValueNode.List();
        foreach (var item in items) list.Add(item);
        return list;
    }

    private static string Refuses(ValueNode tree)
    {
        var thrown = Assert.ThrowsException<DocumentFormatException>(() => Format.Write(tree));
        Assert.IsFalse(string.IsNullOrWhiteSpace(thrown.Message), "a refusal must explain itself");
        return thrown.Message;
    }

    [TestMethod]
    public void The_single_top_level_key_is_the_root_element()
    {
        var xml = Written(Obj(("order", Obj(("ref", V("A1"))))));

        Assert.AreEqual("<order>\n  <ref>A1</ref>\n</order>", xml);
    }

    [TestMethod]
    public void A_shape_xml_cannot_hold_is_refused_by_name()
    {
        // JSON can write several top-level keys and a list at the top; XML can do neither, and
        // finding out from a partner's parser is worse than finding out here.
        StringAssert.Contains(
            Refuses(Obj(("a", V("1")), ("b", V("2")))),
            "exactly one root element");
        StringAssert.Contains(Refuses(Obj(("a", V("1")), ("b", V("2")))), "a, b");
        StringAssert.Contains(Refuses(L(V("1"))), "cannot be a list");
        StringAssert.Contains(Refuses(Obj()), "needs a root element");
        StringAssert.Contains(Refuses(Obj(("order", L(V("1"))))), "cannot repeat the root");
    }

    [TestMethod]
    public void An_at_sign_field_becomes_an_attribute()
    {
        var xml = Written(Obj(("label", Obj(
            ("@type", V("PDF_A6")),
            ("name", V("front"))))));

        Assert.AreEqual("<label type=\"PDF_A6\">\n  <name>front</name>\n</label>", xml);
    }

    [TestMethod]
    public void A_hash_text_field_becomes_the_elements_own_text()
    {
        var xml = Written(Obj(("weight", Obj(("@unit", V("kg")), ("#text", V("0.940"))))));

        Assert.AreEqual("<weight unit=\"kg\">0.940</weight>", xml);
    }

    [TestMethod]
    public void A_list_is_the_same_element_repeated()
    {
        var xml = Written(Obj(("order", Obj(("line", L(
            Obj(("sku", V("A1"))),
            Obj(("sku", V("B7")))))))));

        Assert.AreEqual(
            "<order>\n  <line>\n    <sku>A1</sku>\n  </line>\n  <line>\n    <sku>B7</sku>\n  </line>\n</order>",
            xml);
    }

    [TestMethod]
    public void A_list_inside_a_list_is_refused()
    {
        StringAssert.Contains(
            Refuses(Obj(("order", Obj(("line", L(L(V("A1")))))))),
            "list inside a list");
    }

    [TestMethod]
    public void An_empty_or_null_value_is_an_empty_element()
    {
        // The two are the same document. XML has no null, and an element with no text is all it can
        // say - which is also what the partner's own documents are full of.
        var xml = Written(Obj(("v", Obj(
            ("a", ValueNode.Value(null)),
            ("b", V(""))))));

        Assert.AreEqual("<v>\n  <a />\n  <b />\n</v>", xml);
    }

    [TestMethod]
    public void A_value_is_written_the_way_every_parser_reads_it()
    {
        var xml = Written(Obj(("v", Obj(
            ("weight", V(0.940m)),
            ("qty", V(2m)),
            ("urgent", V(true)),
            ("holidays", V(false))))));

        // Invariant, so no comma decimal separator; the scale as given, so 0.940 stays 0.940; and
        // lower-case booleans, which is what XML Schema calls a boolean.
        Assert.AreEqual(
            "<v>\n  <weight>0.940</weight>\n  <qty>2</qty>\n  <urgent>true</urgent>\n  <holidays>false</holidays>\n</v>",
            xml);
    }

    [TestMethod]
    public void A_namespace_is_an_ordinary_xmlns_field()
    {
        var xml = Written(Obj(("soap:Envelope", Obj(
            ("@xmlns:soap", V("http://schemas.xmlsoap.org/soap/envelope/")),
            ("soap:Body", Obj(("ok", V("1"))))))));

        Assert.AreEqual(
            "<soap:Envelope xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\">\n" +
            "  <soap:Body>\n    <ok>1</ok>\n  </soap:Body>\n</soap:Envelope>",
            xml);
    }

    [TestMethod]
    public void A_default_namespace_applies_to_the_elements_under_it()
    {
        var xml = Written(Obj(("CreateShipment", Obj(
            ("@xmlns", V("http://www.cargonet.software")),
            ("request", Obj(("referencenumber", V("BSO-01157"))))))));

        Assert.AreEqual(
            "<CreateShipment xmlns=\"http://www.cargonet.software\">\n" +
            "  <request>\n    <referencenumber>BSO-01157</referencenumber>\n  </request>\n" +
            "</CreateShipment>",
            xml);
    }

    [TestMethod]
    public void An_empty_xmlns_field_puts_a_subtree_back_into_no_namespace()
    {
        // Chronopost does this for every section of the request body, so it has to be writable as
        // well as readable.
        var xml = Written(Obj(("shipping", Obj(
            ("@xmlns", V("http://cxf.shipping.soap.chronopost.fr/")),
            ("headerValue", Obj(
                ("@xmlns", V("")),
                ("accountNumber", V("55480501"))))))));

        Assert.AreEqual(
            "<shipping xmlns=\"http://cxf.shipping.soap.chronopost.fr/\">\n" +
            "  <headerValue xmlns=\"\">\n    <accountNumber>55480501</accountNumber>\n  </headerValue>\n" +
            "</shipping>",
            xml);
    }

    [TestMethod]
    public void An_unprefixed_attribute_is_in_no_namespace_even_under_a_default_one()
    {
        // The one place XML treats an attribute differently from an element. Getting it wrong
        // produces a document that looks right and validates wrong.
        var xml = Written(Obj(("a", Obj(
            ("@xmlns", V("http://x")),
            ("@id", V("7"))))));

        Assert.AreEqual("<a xmlns=\"http://x\" id=\"7\" />", xml);
    }

    [TestMethod]
    public void A_prefix_nothing_declares_says_which_one_and_what_to_add()
    {
        var message = Refuses(Obj(("soap:Envelope", Obj(("soap:Body", Obj(("ok", V("1"))))))));

        StringAssert.Contains(message, "'soap:'");
        StringAssert.Contains(message, "@xmlns:soap");
    }

    [TestMethod]
    public void Reading_and_writing_the_same_document_keeps_the_data_and_drops_the_namespaces()
    {
        // Worth pinning rather than leaving to be discovered: the reader drops declarations on
        // purpose, so xml→xml does not carry a namespace across. For an xml→xml mapping the
        // target's namespaces come from the sample of the output, the same as its structure.
        const string source =
            """
            <s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/">
              <s:Body>
                <shipping xmlns="http://cxf.shipping.soap.chronopost.fr/">
                  <headerValue xmlns="">
                    <accountNumber>55480501</accountNumber>
                  </headerValue>
                </shipping>
              </s:Body>
            </s:Envelope>
            """;

        var written = Format.Write(Format.Read(source));

        StringAssert.Contains(written, "<accountNumber>55480501</accountNumber>");
        Assert.IsFalse(written.Contains("xmlns"), "declarations are not data and do not survive");
        Assert.IsFalse(written.Contains("s:"), "nor do prefixes");
    }

    [TestMethod]
    public void A_field_name_XML_cannot_hold_is_refused_rather_than_thrown_from_the_writer()
    {
        // A space is legal in a JSON key and in the editor's name box, so this is reachable by
        // typing rather than by anything exotic. Untranslated it leaves the writer as an
        // XmlException, which the preview does not catch and the pipeline reports as a crash.
        var message = Refuses(Obj(("order ref", V("A1"))));

        StringAssert.Contains(message, "XML cannot be written from");
    }

    [TestMethod]
    public void A_prefix_declared_with_nothing_in_it_is_refused_the_same_way()
    {
        // `@xmlns:s` mapped from a source field that turned out empty. XML has no way to bind a
        // prefix to nothing, and the writer says so with an ArgumentException.
        var message = Refuses(Obj(("s:Envelope", Obj(("@xmlns:s", V("")), ("ok", V("1"))))));

        StringAssert.Contains(message, "XML cannot be written from");
    }
}
