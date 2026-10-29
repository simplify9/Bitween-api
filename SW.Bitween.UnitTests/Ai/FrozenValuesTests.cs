using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using SW.Bitween.NativeAdapters.Mapper;
using SW.Bitween.NativeAdapters.Mapper.Formats;
using SW.Bitween.Services.Ai;

namespace SW.Bitween.UnitTests.Ai;

/// <summary>
/// A constant the source doesn't have is what a fixed value is for; one it does have is data frozen
/// to match the one sample, and has to count as a difference even when the output matches.
/// </summary>
[TestClass]
public class FrozenValuesTests
{
    static readonly ValueNode Source = new JsonFormat().Read("""
        { "o": { "first": "Lina", "last": "Haddad", "currency": "N", "lines": [ { "sku": "A-100" } ] } }
        """);

    static string[] In(string rules) =>
        FrozenValues.In(JsonConvert.DeserializeObject<MappingRules>(rules)!, Source).Select(d => d.ToString()).ToArray();

    [TestMethod]
    public void Source_data_appended_as_text_is_frozen()
    {
        CollectionAssert.AreEqual(new[]
        {
            "fullName: the concat text \"Haddad\" is in the source sample, so it would be the same for every document; read it from the source",
        }, In("""
            { "fields": [ { "target": ["fullName"], "from": { "kind": "path", "path": "o.first" },
                            "transform": { "fn": "concat", "with": " Haddad" } } ] }
            """));
    }

    [TestMethod]
    public void A_fixed_value_the_source_holds_is_frozen_wherever_it_is_written()
    {
        CollectionAssert.AreEqual(new[]
        {
            "items[].sku: the fixed value \"a-100\" is in the source sample, so it would be the same for every document; read it from the source",
        }, In("""
            { "lists": [ { "over": "o.lines", "target": ["items"],
                           "fields": [ { "target": ["sku"], "from": { "kind": "fixed", "value": "a-100" } } ] } ] }
            """));
    }

    [TestMethod]
    public void A_constant_the_source_doesnt_have_and_short_coincidences_are_fine()
    {
        CollectionAssert.AreEqual(new string[0], In("""
            { "fields": [ { "target": ["currency"], "from": { "kind": "fixed", "value": "JOD" } },
                          { "target": ["flag"], "from": { "kind": "fixed", "value": "N" } },
                          { "target": ["qty"], "from": { "kind": "fixed", "value": 4 } },
                          { "target": ["name"], "from": { "kind": "path", "path": "o.last" } } ] }
            """));
    }
}
