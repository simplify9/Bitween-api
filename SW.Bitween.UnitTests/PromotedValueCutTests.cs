using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.Domain;

namespace SW.Bitween.UnitTests;

/// <summary>A promoted value is stored whole up to the limit, and cut with "…" past it.</summary>
[TestClass]
public class PromotedValueCutTests
{
    private const int Max = XchangePromotedProperties.MaxValueLength;

    [TestMethod]
    public void A_value_up_to_the_limit_is_stored_whole()
    {
        Assert.IsNull(XchangePromotedProperties.Cut(null));
        Assert.AreEqual("SO-1001", XchangePromotedProperties.Cut("SO-1001"));
        var exactly = new string('x', Max);
        Assert.AreEqual(exactly, XchangePromotedProperties.Cut(exactly));
    }

    [TestMethod]
    public void A_longer_value_is_cut_and_says_so()
    {
        var cut = XchangePromotedProperties.Cut(new string('x', Max + 1));

        Assert.AreEqual(new string('x', Max) + "…", cut);
    }

    [TestMethod]
    public void A_cut_never_splits_a_character_in_two()
    {
        // An emoji is two UTF-16 halves; place one across the limit.
        var value = new string('x', Max - 1) + "😀" + "tail";

        var cut = XchangePromotedProperties.Cut(value);

        Assert.AreEqual(new string('x', Max - 1) + "…", cut);
    }
}
