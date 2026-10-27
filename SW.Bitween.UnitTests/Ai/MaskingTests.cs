using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.Services.Ai;

namespace SW.Bitween.UnitTests.Ai;

/// <summary>
/// What "Explain this failure" sends: structure and wording, never a value. Each test names a value
/// and checks it can't be found in what would go to the provider.
/// </summary>
[TestClass]
public class MaskingTests
{
    [TestMethod]
    public void A_json_document_keeps_its_keys_and_types_and_loses_its_values()
    {
        var masked = new Masking().Document("""
            { "customer": { "name": "Lina Haddad", "vip": true, "balance": 1200.5, "note": "", "fax": null } }
            """, "application/json")!;

        StringAssert.Contains(masked, "\"name\": \"<text>\"");
        StringAssert.Contains(masked, "\"vip\": \"<true|false>\"");
        StringAssert.Contains(masked, "\"balance\": \"<number>\"");
        StringAssert.Contains(masked, "\"note\": \"\"");
        StringAssert.Contains(masked, "\"fax\": null");
        Assert.IsFalse(masked.Contains("Lina") || masked.Contains("1200"));
    }

    [TestMethod]
    public void An_xml_document_is_masked_attributes_and_all()
    {
        var masked = new Masking().Document("""<order id="SO-77"><buyer>Atlas Trading</buyer></order>""", "text/xml")!;

        StringAssert.Contains(masked, "<buyer>&lt;text&gt;</buyer>");
        Assert.IsFalse(masked.Contains("SO-77") || masked.Contains("Atlas"));
    }

    [TestMethod]
    public void A_long_list_shows_its_first_entries_and_how_many_more()
    {
        var masked = new Masking().Document("""{ "lines": [ {"sku":"A1"}, {"sku":"A2"}, {"sku":"A3"}, {"sku":"A4"}, {"sku":"A5"} ] }""", null)!;

        StringAssert.Contains(masked, "<2 more entries like these>");
        Assert.IsFalse(masked.Contains("A5"));
    }

    /// <summary>Nothing is sent of a document that can't be masked; guessing would send it whole.</summary>
    [TestMethod]
    public void A_document_that_isnt_json_xml_or_csv_isnt_sent_at_all()
    {
        Assert.IsNull(new Masking().Document("Dear Lina, your parcel 4471 is late.", "text/plain"));
    }

    /// <summary>An HTTP adapter puts the partner's response body in the error text.</summary>
    [TestMethod]
    public void A_response_body_inside_an_error_is_masked_as_a_document()
    {
        var masked = new Masking().Error("""
            400: BadRequest: {"error":"Customer Lina Haddad has no account","code":"ACC_MISSING"}
               at SW.Bitween.NativeAdapters.NativeHttpHandler.Handle() in NativeHttpHandler.cs:line 170
            """);

        StringAssert.Contains(masked, "400: BadRequest:");
        StringAssert.Contains(masked, "\"error\": \"<text>\"");
        StringAssert.Contains(masked, "NativeHttpHandler.Handle()");
        Assert.IsFalse(masked.Contains("Lina") || masked.Contains("ACC_MISSING"));
    }

    [TestMethod]
    public void An_error_loses_document_values_quotes_emails_and_long_numbers()
    {
        var masking = new Masking();
        masking.Document("""{ "ref": "SO-5001", "city": "Amman" }""", "application/json");

        var masked = masking.Error(
            "Could not deliver SO-5001 to Amman for ops@acme.example: value 'blue-77' is not allowed, card 4111222233334444, see https://p.example/x?token=abc");

        foreach (var leaked in new[] { "SO-5001", "Amman", "ops@acme.example", "blue-77", "4111222233334444", "token=abc" })
            Assert.IsFalse(masked.Contains(leaked), $"'{leaked}' was not masked: {masked}");
        StringAssert.Contains(masked, "Could not deliver");
        StringAssert.Contains(masked, "is not allowed");
    }

    /// <summary>A frame keeps its file and line; the folders above it describe the server, not the failure.</summary>
    [TestMethod]
    public void Stack_frames_keep_their_file_but_not_the_servers_folders()
    {
        var masked = new Masking().Error("""
            System.UriFormatException: Invalid URI
               at SW.Bitween.NativeAdapters.DynamicHttpProxy.GetClient(String fullUrl) in /Users/someone/Work/Bitween-api/SW.Bitween.NativeAdapters/HttpHandler/DynamicHttpProxy.cs:line 22
               at SW.Bitween.XchangeService.Process(XchangeMessage message) in C:\agent\_work\1\s\SW.Bitween.Api\Services\XchangeService.cs:line 703
            """);

        StringAssert.Contains(masked, "GetClient(String fullUrl) in DynamicHttpProxy.cs:line 22");
        StringAssert.Contains(masked, "in XchangeService.cs:line 703");
        Assert.IsFalse(masked.Contains("/Users/someone") || masked.Contains("_work"), masked);
    }

    [TestMethod]
    public void The_answer_is_read_as_json_or_kept_as_prose()
    {
        var json = FailurePrompt.Parse("""```json {"summary":"S","cause":"C","fix":"F"} ```""");
        Assert.AreEqual(("S", "C", "F"), (json.Summary, json.Cause, json.Fix));

        var prose = FailurePrompt.Parse("The partner timed out; retry.");
        Assert.AreEqual(("The partner timed out; retry.", "", ""), (prose.Summary, prose.Cause, prose.Fix));
    }
}
