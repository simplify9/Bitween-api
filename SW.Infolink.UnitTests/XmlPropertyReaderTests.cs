using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SW.Infolink.UnitTests;

[TestClass]
public class XmlPropertyReaderTests
{


    [TestMethod]
    public void Test()
    {
        var samplePayload = @"
            <shipment>
            <name>samer</name>
            <country>Jordan</country>
            </shipment>";

        var propertyReader = new XmlExchangePayloadReader(samplePayload);

        propertyReader.TryGetValue("/shipment/name", out var name);
        propertyReader.TryGetValue("/shipment/country", out var country);
        
        Assert.AreEqual("samer",name);
        Assert.AreEqual("Jordan",country);

    }
}