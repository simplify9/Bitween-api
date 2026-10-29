using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using SW.PrimitiveTypes;

namespace SW.Bitween.UnitTests
{
    [TestClass]
    public class XchangeFileTests
    {
        /// <summary>An exchange file travels as JSON on the bus and in storage, and comes back whole.</summary>
        [TestMethod]
        public void An_exchange_file_survives_a_json_round_trip()
        {
            var file = new XchangeFile("test data", "file.txt");

            var back = JsonConvert.DeserializeObject<XchangeFile>(JsonConvert.SerializeObject(file));

            Assert.AreEqual("test data", back.Data);
            Assert.AreEqual("file.txt", back.Filename);
        }
    }
}
