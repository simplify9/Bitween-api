using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.NativeAdapters;
using SW.Bitween.NativeAdapters.JsonMapper;

namespace SW.Bitween.UnitTests.PayloadParsing;

/// <summary>
/// Parses of a payload inside the Scriban helper, counted as production counts them. The pipeline's
/// counts per path are held by <c>ExchangeParseCountTests</c>; these are the paths it does not run:
/// the file-name and subject fill of the FTP and SMTP handlers.
/// </summary>
[TestClass]
public class PayloadParseCountTests
{
    private const string Payload = "{\"id\":\"A-1\",\"price\":1.10}";

    /// <summary>One parse to see that the payload is JSON, one to build the model. Finding 6, row 4.</summary>
    private const int FillParses = 2;

    [TestMethod]
    public void Filling_a_template_from_a_json_payload_parses_it_as_often_as_counted()
    {
        var tally = PayloadParseTally.Open();

        Assert.AreEqual("order-A-1.json", ScribanJsonHelper.FillIfJson("order-{{ id }}.json", Payload));

        Assert.AreEqual(FillParses, tally.Count);
    }

    [TestMethod]
    public void A_payload_that_is_not_json_is_not_parsed_to_fill_a_template()
    {
        var tally = PayloadParseTally.Open();

        Assert.AreEqual("order-{{ id }}.json", ScribanJsonHelper.FillIfJson("order-{{ id }}.json", "id,price\nA-1,1.10"));

        Assert.AreEqual(0, tally.Count);
    }

    /// <summary>The model, and the rendered output that is parsed to be tidied.</summary>
    [TestMethod]
    public void The_scriban_mapper_parses_its_input_once_and_its_output_once()
    {
        var tally = PayloadParseTally.Open();

        ScribanJsonHelper.Render("{ \"orderId\": {{ id | json }} }", Payload);

        Assert.AreEqual(2, tally.Count);
    }
}
