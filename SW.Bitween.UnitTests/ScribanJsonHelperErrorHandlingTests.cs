using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static SW.Bitween.UnitTests.ScribanJsonTestHelper;

namespace SW.Bitween.UnitTests;

[TestClass]
public class ScribanJsonHelperErrorHandlingTests
{
    [TestMethod]
    public void InvalidScribanTemplate_ThrowsTemplateParseError()
    {
        var input = "{\"x\":1}";
        var badTemplate = "{ \"x\": {{ for i in }} }";

        var ex = Assert.ThrowsException<InvalidOperationException>(() => Render(badTemplate, input));

        StringAssert.Contains(ex.Message, "Template parse error");
    }

    [TestMethod]
    public void TemplateThatProducesInvalidJson_ThrowsExpectedError()
    {
        var input = "{\"x\":1}";
        var badTemplate = "{ \"x\": {{ x | json }}";

        var ex = Assert.ThrowsException<InvalidOperationException>(() => Render(badTemplate, input));

        StringAssert.Contains(ex.Message, "Template produced invalid JSON");
    }

    [TestMethod]
    public void SpecialCharacters_AreEscapedCorrectly()
    {
        var input = "{\"text\":\"café \\\"quoted\\\"\"}";
        var template = "{ \"text\": {{ text | json }} }";

        var output = Render(template, input);

        AssertJsonEquals("{\"text\":\"café \\\"quoted\\\"\"}", output);
    }
}
