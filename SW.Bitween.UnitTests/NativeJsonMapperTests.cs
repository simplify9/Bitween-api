using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.NativeAdapters;
using SW.PrimitiveTypes;
using static SW.Bitween.UnitTests.ScribanJsonTestHelper;

namespace SW.Bitween.UnitTests;

[TestClass]
public class NativeJsonMapperTests
{
    [TestMethod]
    public async Task NativeJsonMapper_MapsInputUsingStartupTemplate()
    {
        var mapper = new NativeJSONMapper();
        mapper.InitializeStartupValues(new Dictionary<string, string>
        {
            ["ScribanTemplate"] = "{ \"id\": {{ orderId | json }} }"
        });

        var input = new XchangeFile("{\"orderId\":\"123\"}");
        var output = await mapper.Handle(input);

        AssertJsonEquals("{\"id\":\"123\"}", output.Data);
    }

    [TestMethod]
    public async Task NativeJsonMapper_WhenTemplateMissing_UsesDefaultEmptyObject()
    {
        var mapper = new NativeJSONMapper();
        mapper.InitializeStartupValues(new Dictionary<string, string>());

        var output = await mapper.Handle(new XchangeFile("{\"x\":1}"));

        AssertJsonEquals("{}", output.Data);
    }

    [TestMethod]
    public async Task NativeJsonMapper_ReturnsNewXchangeFileInstance()
    {
        var mapper = new NativeJSONMapper();
        mapper.InitializeStartupValues(new Dictionary<string, string>
        {
            ["ScribanTemplate"] = "{ \"x\": {{ x | json }} }"
        });

        var input = new XchangeFile("{\"x\":1}");
        var output = await mapper.Handle(input);

        Assert.AreNotSame(input, output);
        AssertJsonEquals("{\"x\":1}", output.Data);
    }
}
