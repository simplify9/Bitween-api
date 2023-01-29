using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SW.Infolink.UnitTests;

[TestClass]
public class TestJsonPropertyReader
{
    
    
    [TestMethod]
    public void Test()
    {
        var samplePayload = "{\"name\":\"samer\",\"address\":{\"country\":\"Jordan\"}}";

        var propertyReader = new JsonPropertyReader(samplePayload);

        propertyReader.TryGetValue("name", out var name);
        propertyReader.TryGetValue("address.country", out var country);
        
        Assert.AreEqual("samer",name);
        Assert.AreEqual("Jordan",country);

    }
}