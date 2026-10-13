using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using SW.PrimitiveTypes;

namespace SW.Bitween.UnitTests.NativeMapper;

/// <summary>A fixed value keeps its JSON type, a list or an object included, as an API's request often needs.</summary>
[TestClass]
public class FixedValueTests
{
    static JToken Map(string rules, string document)
    {
        var mapper = new SW.Bitween.NativeAdapters.Mapper.NativeMapper();
        mapper.InitializeStartupValues(new Dictionary<string, string> { ["MappingRules"] = rules });
        return JToken.Parse(mapper.Handle(new XchangeFile(document)).Result.Data);
    }

    [TestMethod]
    public void A_fixed_list_and_a_fixed_object_are_written_as_the_JSON_they_are()
    {
        var output = Map("""
            {
              "version": 1, "sourceFormat": "json", "targetFormat": "json",
              "fields": [
                { "target": ["duplicate_check_fields"], "from": { "kind": "fixed", "value": ["Email"] } },
                { "target": ["options"], "from": { "kind": "fixed", "value": { "trigger": ["workflow"], "notify": false } } },
                { "target": ["count"], "from": { "kind": "fixed", "value": 3 } }
              ],
              "lists": [
                { "target": ["data"], "fixed": [ { "fields": [ { "target": ["Last_Name"], "from": { "kind": "path", "path": "lastName" } } ] } ] }
              ]
            }
            """, """{"lastName":"Saleh"}""");

        Assert.AreEqual("""["Email"]""", output["duplicate_check_fields"]!.ToString(Newtonsoft.Json.Formatting.None));
        Assert.AreEqual("""{"trigger":["workflow"],"notify":false}""", output["options"]!.ToString(Newtonsoft.Json.Formatting.None));
        Assert.AreEqual(3, (int)output["count"]!);
        Assert.AreEqual("Saleh", (string)output["data"]![0]!["Last_Name"]!);
    }
}
