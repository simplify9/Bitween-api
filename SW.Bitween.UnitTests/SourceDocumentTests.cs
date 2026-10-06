using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SW.Bitween.UnitTests;

/// <summary>
/// Source values are read out of the original document by path, the way a mapping reads one.
/// </summary>
[TestClass]
public class SourceDocumentTests
{
    private const string Order = """
        { "order": { "number": "SO-1001", "total": 12.5, "paid": true, "note": null,
          "customer": { "name": "Lina" }, "lines": [ { "sku": "A1" } ] } }
        """;

    [TestMethod]
    public void Values_are_read_by_path_as_text()
    {
        var values = SourceDocument.Read(Order, ["order.number", "order.total", "order.paid", "order.customer.name"]);

        Assert.AreEqual("SO-1001", values["order.number"]);
        Assert.AreEqual("12.5", values["order.total"]);
        Assert.AreEqual("true", values["order.paid"]);
        Assert.AreEqual("Lina", values["order.customer.name"]);
    }

    [TestMethod]
    public void A_path_with_no_value_is_left_out()
    {
        // Missing, null, an object, a path into a list: none of them is a value to fill in.
        var values = SourceDocument.Read(Order,
            ["order.missing", "order.note", "order.customer", "order.lines.sku", "Order.number"]);

        Assert.AreEqual(0, values.Count);
    }

    [TestMethod]
    public void An_xml_document_is_read_from_its_root_element()
    {
        var values = SourceDocument.Read("<Order><Number>SO-1001</Number></Order>", ["Order.Number"]);

        Assert.AreEqual("SO-1001", values["Order.Number"]);
    }

    [TestMethod]
    public void A_document_that_cant_be_read_has_no_values()
    {
        Assert.AreEqual(0, SourceDocument.Read("sku,qty\nA1,2", ["sku"]).Count);
        Assert.AreEqual(0, SourceDocument.Read("{ not json", ["order.number"]).Count);
        Assert.AreEqual(0, SourceDocument.Read("", ["order.number"]).Count);
        Assert.AreEqual(0, SourceDocument.PathsIn("<broken").Count);
    }

    [TestMethod]
    public void The_offered_paths_are_the_values_outside_lists_in_document_order()
    {
        var paths = SourceDocument.PathsIn(Order);

        CollectionAssert.AreEqual(
            new[] { "order.number", "order.total", "order.paid", "order.customer.name" },
            paths.Select(p => p.Path).ToArray());
        Assert.AreEqual("SO-1001", paths[0].Example);
    }

    [TestMethod]
    public void A_long_example_is_cut()
    {
        var long_ = new string('x', SourceDocument.MaxExampleLength + 50);

        var path = SourceDocument.PathsIn($$"""{ "notes": "{{long_}}" }""").Single();

        Assert.AreEqual(long_[..SourceDocument.MaxExampleLength] + "…", path.Example);
    }
}
