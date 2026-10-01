using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SW.Bitween.UnitTests;

/// <summary>Promoted values come back in the order their information type lists the properties.</summary>
[TestClass]
public class PromotedPropertiesOrderTests
{
    [TestMethod]
    public void Promoted_values_follow_the_types_order_with_unlisted_ones_last()
    {
        var values = new Dictionary<string, string> { ["zeta"] = "z", ["gone"] = "g", ["alpha"] = "a" };
        var definition = new Dictionary<string, string> { ["alpha"] = "$.a", ["zeta"] = "$.z", ["unused"] = "$.u" };

        CollectionAssert.AreEqual(new[] { "alpha", "zeta", "gone" }, values.InDefinedOrder(definition).Keys.ToArray());
        Assert.IsNull(((IReadOnlyDictionary<string, string>)null).InDefinedOrder(definition));
    }
}
