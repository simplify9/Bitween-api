using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using SW.Infolink.JsonConverters;
using SW.Infolink.Model;

namespace SW.Infolink.UnitTests;

[TestClass]
public class PropertyMatchSpecificationJsonConverterTests
{
    private IPropertyMatchSpecification[] testSamples = new IPropertyMatchSpecification[]
    {
        new AndSpec(
            new OrSpec(
                new OneOfSpec("client", new[] { "samer", "yaser" }),
                new NotOneOfSpec("code", new[] { "Mohammad", "Waheeb" })),
            right: new OneOfSpec("client", new[] { "samer", "yaser" }))
    };

    [TestMethod]
    public void TestRoundTrip()
    {
        var serializer = new JsonSerializer();
        serializer.Converters.Add(new PropertyMatchSpecificationJsonConverter());

        foreach (var sample in testSamples)
        {
            serializer.Formatting = Formatting.Indented;
            serializer.TypeNameHandling = TypeNameHandling.Auto;
            string json;
            using (var sw = new StringWriter())
            using (JsonWriter writer = new JsonTextWriter(sw))
            {
                serializer.Serialize(writer, sample);
                json = sw.ToString();
            }

            using (var sr = new StringReader(json))
            using (JsonReader reader = new JsonTextReader(sr))
            {
                var sampleBack = serializer.Deserialize<IPropertyMatchSpecification>(reader);
                Console.WriteLine(sample);
                Console.WriteLine(sampleBack);
                Assert.AreEqual(sample.ToString(), sampleBack?.ToString());
            }
        }
    }
}