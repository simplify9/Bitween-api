using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.Services.Ai;

namespace SW.Bitween.UnitTests.Ai;

/// <summary>
/// Ask your exchanges: what the model may say becomes the Exchanges page's own filter, and nothing
/// the instance doesn't have gets through.
/// </summary>
[TestClass]
public class ExchangeQueryPromptTests
{
    static readonly QueryNames Names = new(
        [new QueryName(12, "SkyNet Express")],
        [new QueryName(40, "SkyNet orders out", "Internal, partner 12, information type 3")],
        [new QueryName(3, "Shipment order", "promotes orderNo")],
        ["orderNo"]);

    [TestMethod]
    public void A_filter_becomes_the_pages_url_parameters()
    {
        var query = ExchangeQueryPrompt.Parse("""
            { "status": "failed", "partnerIds": [12], "from": "2026-10-26T00:00", "latest": true,
              "properties": [{ "key": "ORDERNO", "text": "1001" }],
              "answer": "{count} failed SkyNet exchanges since Monday" }
            """, Names);

        Assert.AreEqual("status=failed&partnerId=12&prop=orderNo%3A1001&latest=1&from=2026-10-26T00%3A00", query.Query);
        Assert.AreEqual("{count} failed SkyNet exchanges since Monday", query.Answer);
        Assert.AreEqual(0, query.Notes.Count);
    }

    [TestMethod]
    public void What_the_instance_doesnt_have_is_dropped_and_said()
    {
        var query = ExchangeQueryPrompt.Parse("""
            { "status": "broken", "partnerIds": [12, 99], "properties": [{ "key": "awb" }], "range": "forever" }
            """, Names);

        Assert.AreEqual("partnerId=12", query.Query);
        CollectionAssert.AreEqual(new[]
        {
            "Status \"broken\" isn't one the page has.",
            "There is no partner with id 99.",
            "No information type promotes a property \"awb\".",
            "\"forever\" isn't a time range the page has.",
        }, new System.Collections.Generic.List<string>(query.Notes));
    }

    [TestMethod]
    public void Only_filters_that_are_set_are_turned_round()
    {
        var query = ExchangeQueryPrompt.Parse("""
            { "partnerIds": [12], "error": "timeout", "not": ["partner", "subscription", "constructor"] }
            """, Names);

        Assert.AreEqual("partnerId=12&error=timeout&not=partner", query.Query);
    }

    [TestMethod]
    public void A_range_wins_over_from_and_to()
    {
        var query = ExchangeQueryPrompt.Parse("""{ "range": "2H", "from": "2026-10-01T00:00" }""", Names);

        Assert.AreEqual("range=2h", query.Query);
    }

    [TestMethod]
    public void An_answer_that_isnt_json_is_a_note_not_a_filter()
    {
        var query = ExchangeQueryPrompt.Parse("Sorry, I can't help with that.", Names);

        Assert.AreEqual("", query.Query);
        Assert.AreEqual(1, query.Notes.Count);
    }

    [TestMethod]
    public void The_message_has_the_names_and_the_time_and_nothing_else()
    {
        var message = ExchangeQueryPrompt.Message("failed skynet since monday", new DateTime(2026, 10, 27, 14, 5, 0), "Asia/Amman", Names);

        StringAssert.Contains(message, "Now: 2026-10-27 14:05, Tuesday, in Asia/Amman.");
        StringAssert.Contains(message, "12: SkyNet Express");
        StringAssert.Contains(message, "40: SkyNet orders out (Internal, partner 12, information type 3)");
        StringAssert.Contains(message, "orderNo");
    }
}
