using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.NativeAdapters.Mapper.Formats;
using SW.Bitween.Services.Ai;

namespace SW.Bitween.UnitTests.Ai;

/// <summary>
/// The comparison that tells the editor - and a model fixing its rules - whether generated rules do
/// what the output sample asks.
/// </summary>
[TestClass]
public class DocumentDifferencesTests
{
    static readonly JsonFormat Json = new();

    static string[] Between(string expected, string actual) =>
        DocumentDifferences.Between(Json.Read(expected), Json.Read(actual)).Select(d => d.ToString()).ToArray();

    /// <summary>The editor finds the rule to mark from the target, so it carries the path on its own.</summary>
    [TestMethod]
    public void A_difference_names_its_path_separately_from_the_reason()
    {
        var found = DocumentDifferences.Between(Json.Read("""{ "lines": [ { "qty": 1 } ] }"""), Json.Read("""{ "lines": [ { "qty": 2 } ] }"""));

        Assert.AreEqual("lines[0].qty", found.Single().Target);
        Assert.AreEqual("the output sample has the number 1, the rules write the number 2", found.Single().Reason);
        Assert.AreEqual("(root)", DocumentDifferences.Between(Json.Read("[]"), Json.Read("{}")).Single().Target);
    }

    [TestMethod]
    public void Whitespace_and_key_order_are_not_differences()
    {
        CollectionAssert.AreEqual(new string[0],
            Between("""{ "a": 1, "b": { "c": "x" } }""", """{"b":{"c":"x"},"a":1.0}"""));
    }

    /// <summary>A partner's parser notices 42 where "42" was wanted.</summary>
    [TestMethod]
    public void A_number_where_text_was_wanted_is_a_difference()
    {
        CollectionAssert.AreEqual(new[] { "qty: the output sample has the text \"42\", the rules write the number 42" },
            Between("""{ "qty": "42" }""", """{ "qty": 42 }"""));
    }

    [TestMethod]
    public void Missing_and_extra_fields_are_named_by_path()
    {
        var found = Between("""{ "order": { "id": "A", "ref": "R" } }""", """{ "order": { "id": "A", "note": "n" } }""");

        CollectionAssert.AreEqual(new[]
        {
            "order.ref: missing; the output sample has the text \"R\"",
            "order.note: not in the output sample, but the rules write the text \"n\"",
        }, found);
    }

    [TestMethod]
    public void Lists_are_compared_entry_by_entry_and_by_length()
    {
        var found = Between("""{ "lines": [ { "sku": "A" }, { "sku": "B" } ] }""", """{ "lines": [ { "sku": "a" } ] }""");

        CollectionAssert.AreEqual(new[]
        {
            "lines: the output sample has 2 entries, the rules write 1",
            "lines[0].sku: the output sample has the text \"A\", the rules write the text \"a\"",
        }, found);
    }

    /// <summary>An output sample is often the partner's template, with the values left blank.</summary>
    [TestMethod]
    public void A_blank_value_in_the_output_sample_is_a_placeholder()
    {
        CollectionAssert.AreEqual(new string[0],
            Between("""{ "name": "", "city": "  ", "qty": null }""", """{ "name": "Acme", "city": "Roissy", "qty": 2 }"""));

        // The field still has to be written, and a value still can't stand where a list or object was asked for.
        CollectionAssert.AreEqual(new[]
        {
            "name: missing; the output sample has the text \"\"",
            "city: the output sample has the text \" \", the rules write a list of 0",
        }, Between("""{ "name": "", "city": " " }""", """{ "city": [] }"""));
    }

    [TestMethod]
    public void A_wholly_wrong_document_is_described_in_a_bounded_list()
    {
        var expected = "{" + string.Join(",", Enumerable.Range(0, 50).Select(i => $"\"f{i}\": {i}")) + "}";

        Assert.AreEqual(DocumentDifferences.Limit, Between(expected, "{}").Length);
    }
}
