using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.NativeAdapters.JsonMapper;

namespace SW.Bitween.UnitTests;

[TestClass]
public class ScribanTrailingCommaTests
{
    [DataTestMethod]
    [DataRow("{\"a\": 1,}", "{\"a\": 1}")]
    [DataRow("[1, 2,\n  ]", "[1, 2\n  ]")]
    [DataRow("{\"note\": \"a, }\"}", "{\"note\": \"a, }\"}")]
    [DataRow("{\"q\": \"say \\\"x, ]\\\"\",}", "{\"q\": \"say \\\"x, ]\\\"\"}")]
    [DataRow("{\"a\": [1,], \"b\": 2,}", "{\"a\": [1], \"b\": 2}")]
    public void Only_trailing_commas_outside_strings_are_removed(string input, string expected) =>
        Assert.AreEqual(expected, ScribanJsonHelper.StripTrailingCommas(input));
}
