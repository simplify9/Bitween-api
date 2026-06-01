using Microsoft.VisualStudio.TestTools.UnitTesting;
using static SW.Bitween.UnitTests.ScribanJsonTestHelper;

namespace SW.Bitween.UnitTests;

[TestClass]
public class ScribanJsonHelperRootMappingTests
{
    [TestMethod]
    public void SourceMapping_RenamesFlatFields()
    {
        var input = "{\"orderId\":\"123\",\"status\":\"pending\"}";
        var template = "{ \"id\": {{ orderId | json }}, \"state\": {{ status | json }} }";

        var output = Render(template, input);

        AssertJsonEquals("{\"id\":\"123\",\"state\":\"pending\"}", output);
    }

    [TestMethod]
    public void SourceMapping_ReadsNestedField()
    {
        var input = "{\"customer\":{\"address\":{\"city\":\"Amman\"}}}";
        var template = "{ \"city\": {{ customer.address.city | json }} }";

        var output = Render(template, input);

        AssertJsonEquals("{\"city\":\"Amman\"}", output);
    }

    [TestMethod]
    public void SourceMapping_ExpandsDottedOutputPath()
    {
        var input = "{\"city\":\"Amman\"}";
        var template = "{ \"shipping.address.city\": {{ city | json }} }";

        var output = Render(template, input);

        AssertJsonEquals("{\"shipping\":{\"address\":{\"city\":\"Amman\"}}}", output);
    }

    [TestMethod]
    public void SourceMapping_ResolvesPascalCaseKeyAsIs()
    {
        var input = "{\"CustomerId\":\"C-001\"}";
        var template = "{ \"id\": {{ CustomerId | json }} }";

        var output = Render(template, input);

        AssertJsonEquals("{\"id\":\"C-001\"}", output);
    }

    [TestMethod]
    public void SourceMapping_ResolvesLowercaseAliasForPascalCaseKey()
    {
        var input = "{\"CustomerId\":\"C-001\"}";
        var template = "{ \"id\": {{ customerId | json }} }";

        var output = Render(template, input);

        AssertJsonEquals("{\"id\":\"C-001\"}", output);
    }

    [TestMethod]
    public void SourceMapping_MissingVariableDoesNotThrow()
    {
        var input = "{\"x\":1}";
        var template = "{ \"value\": {{ missingField | json }} }";

        var output = Render(template, input);

        AssertJsonEquals("{\"value\":null}", output);
    }

    [TestMethod]
    public void FixedMapping_HandlesStringNumberBoolAndNullLiterals()
    {
        var input = "{\"ignored\":true}";
        var template = "{ \"s\": \"pending\", \"n\": 5, \"b\": true, \"x\": null }";

        var output = Render(template, input);

        AssertJsonEquals("{\"s\":\"pending\",\"n\":5,\"b\":true,\"x\":null}", output);
    }

    [TestMethod]
    public void FixedMapping_AllowsEmbeddingSourceVariableInsideString()
    {
        var input = "{\"orderId\":\"C-001\"}";
        var template = "{ \"label\": \"Order-{{ orderId }}\" }";

        var output = Render(template, input);

        AssertJsonEquals("{\"label\":\"Order-C-001\"}", output);
    }

    [TestMethod]
    public void FixedMapping_AllowsEmbeddingMultipleSourceVariablesInsideString()
    {
        var input = "{\"first\":\"John\",\"last\":\"Doe\"}";
        var template = "{ \"name\": \"{{ first }} {{ last }}\" }";

        var output = Render(template, input);

        AssertJsonEquals("{\"name\":\"John Doe\"}", output);
    }

    [TestMethod]
    public void PartnerMapping_MapsStringValue()
    {
        var input = "{\"__partner__\":{\"apiKey\":\"abc\"}}";
        var template = "{ \"apiKey\": {{ __partner__?.apiKey | json }} }";

        var output = Render(template, input);

        AssertJsonEquals("{\"apiKey\":\"abc\"}", output);
    }

    [TestMethod]
    public void PartnerMapping_MissingKeyReturnsNull()
    {
        var input = "{\"__partner__\":{\"apiKey\":\"abc\"}}";
        var template = "{ \"region\": {{ __partner__?.region | json }} }";

        var output = Render(template, input);

        AssertJsonEquals("{\"region\":null}", output);
    }

    [TestMethod]
    public void PartnerAndGlobalTypedTargets_AreRepresentedAsNullTemplateLiteral()
    {
        var input = "{\"__partner__\":{\"flag\":true},\"__globals__\":{\"S\":{\"K\":\"1\"}}}";
        var template = "{ \"partnerBoolTarget\": null, \"globalNumberTarget\": null }";

        var output = Render(template, input);

        AssertJsonEquals("{\"partnerBoolTarget\":null,\"globalNumberTarget\":null}", output);
    }

    [TestMethod]
    public void GlobalMapping_MapsStringValue()
    {
        var input = "{\"__globals__\":{\"regionSet\":{\"europe\":\"EU\"}}}";
        var template = "{ \"region\": {{ __globals__?.regionSet[\"europe\"] | json }} }";

        var output = Render(template, input);

        AssertJsonEquals("{\"region\":\"EU\"}", output);
    }

    [TestMethod]
    public void GlobalMapping_MissingKeyReturnsNull()
    {
        var input = "{\"__globals__\":{\"regionSet\":{\"europe\":\"EU\"}}}";
        var template = "{ \"region\": {{ __globals__?.regionSet[\"unknown\"] | json }} }";

        var output = Render(template, input);

        AssertJsonEquals("{\"region\":null}", output);
    }
}
