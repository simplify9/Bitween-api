using System.Linq;
using System.Threading.Tasks;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SW.Bitween.UnitTests;

/// <summary>Checking documents against the schema an information type carries.</summary>
[TestClass]
public class DocumentSchemaTests
{
    private const string OrderSchema = """
        {
          "type": "object",
          "required": ["orderId", "lines"],
          "additionalProperties": false,
          "properties": {
            "orderId": { "type": "string" },
            "lines": { "type": "array", "items": { "$ref": "#/definitions/line" } }
          },
          "definitions": {
            "line": { "type": "object", "required": ["sku"], "properties": { "sku": { "type": "string" } } }
          }
        }
        """;

    private const string OrderXsd = """
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
          <xs:element name="order">
            <xs:complexType>
              <xs:sequence>
                <xs:element name="orderId" type="xs:string" />
                <xs:element name="quantity" type="xs:int" />
              </xs:sequence>
            </xs:complexType>
          </xs:element>
        </xs:schema>
        """;

    private static async Task Clean(Task<System.Collections.Generic.IReadOnlyList<string>> check)
    {
        var errors = await check;
        Assert.AreEqual(0, errors.Count, string.Join("\n", errors));
    }

    [TestMethod]
    public async Task A_matching_json_document_has_no_errors()
    {
        await Clean(DocumentSchema.Check(DocumentFormat.Json, OrderSchema,
            """{ "orderId": "A1", "lines": [ { "sku": "X" } ] }"""));
    }

    [TestMethod]
    public async Task A_json_document_that_doesnt_match_says_where()
    {
        var errors = await DocumentSchema.Check(DocumentFormat.Json, OrderSchema,
            """{ "lines": [ { "qty": 1 } ], "extra": true }""");

        Assert.IsTrue(errors.Any(e => e.Contains("orderId")));
        Assert.IsTrue(errors.Any(e => e.Contains("sku")));
        Assert.IsTrue(errors.Any(e => e.Contains("extra")));
    }

    [TestMethod]
    public async Task What_the_older_partner_endpoint_adds_is_not_held_against_the_document()
    {
        await Clean(DocumentSchema.Check(DocumentFormat.Json, OrderSchema,
            """{ "orderId": "A1", "lines": [], "_ExternalRequestContext": "[]" }"""));
    }

    [TestMethod]
    public async Task Malformed_json_is_one_error_not_an_exception()
    {
        var errors = await DocumentSchema.Check(DocumentFormat.Json, OrderSchema, "{ not json");
        Assert.AreEqual(1, errors.Count);
        Assert.IsTrue(errors[0].StartsWith("Not valid JSON"));
    }

    [TestMethod]
    public async Task An_xml_document_is_checked_against_its_xsd()
    {
        await Clean(DocumentSchema.Check(DocumentFormat.Xml, OrderXsd,
            "<order><orderId>A1</orderId><quantity>2</quantity></order>"));

        var errors = await DocumentSchema.Check(DocumentFormat.Xml, OrderXsd,
            "<order><orderId>A1</orderId><quantity>two</quantity></order>");
        Assert.IsTrue(errors.Any(e => e.Contains("quantity")));

        Assert.IsTrue((await DocumentSchema.Check(DocumentFormat.Xml, OrderXsd, "<order>"))[0].StartsWith("Not valid XML"));
    }

    [TestMethod]
    public async Task No_schema_and_carried_formats_check_nothing()
    {
        await Clean(DocumentSchema.Check(DocumentFormat.Json, null, "{ not json"));
        await Clean(DocumentSchema.Check(DocumentFormat.Csv, OrderSchema, "a,b"));
    }

    [DataTestMethod]
    [DataRow(DocumentFormat.Json, "{ \"type\": ")]
    [DataRow(DocumentFormat.Json, "{ \"$ref\": \"https://example.com/order.json\" }")]
    [DataRow(DocumentFormat.Json, "{ \"$ref\": \"order.json\" }")]
    [DataRow(DocumentFormat.Xml, "<xs:schema xmlns:xs=\"http://www.w3.org/2001/XMLSchema\"><xs:include schemaLocation=\"http://example.com/a.xsd\" /></xs:schema>")]
    [DataRow(DocumentFormat.Xml, "<not-a-schema />")]
    [DataRow(DocumentFormat.Csv, "{}")]
    public async Task A_schema_that_cant_be_used_or_reaches_outside_itself_is_refused(DocumentFormat format, string schema)
    {
        await Assert.ThrowsExceptionAsync<SWValidationException>(() => DocumentSchema.EnsureUsable(format, schema));
    }

    [TestMethod]
    public async Task A_schema_fixed_after_being_refused_is_accepted()
    {
        await Assert.ThrowsExceptionAsync<SWValidationException>(() => DocumentSchema.EnsureUsable(DocumentFormat.Json, "{"));
        await DocumentSchema.EnsureUsable(DocumentFormat.Json, OrderSchema);
    }
}
