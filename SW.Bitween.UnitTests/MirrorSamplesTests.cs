using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.Services.Mirror;

namespace SW.Bitween.UnitTests;

/// <summary>
/// Which of prod's exchanges become samples, and what staging keeps of how they ended: the words its
/// setup names, kept wherever prod's data has them, and never a value of the data itself.
/// </summary>
[TestClass]
public class MirrorSamplesTests
{
    static JsonObject Row(string id, int minute, bool? status, bool responseBad = false, string retryFor = null) => new()
    {
        ["id"] = id,
        ["startedOn"] = $"2026-10-20T10:{minute:00}:00Z",
        ["status"] = status,
        ["responseBad"] = responseBad,
        ["retryFor"] = retryFor,
    };

    [TestMethod]
    public void Samples_are_mostly_what_worked_with_one_bad_answer_and_one_failure_never_a_retry_or_a_running_one()
    {
        var rows = new List<JsonObject>
        {
            Row("w1", 1, true), Row("w2", 2, true), Row("w3", 3, true), Row("w4", 4, true),
            Row("bad1", 5, true, responseBad: true), Row("bad2", 6, true, responseBad: true),
            Row("f1", 7, false), Row("f2", 8, false),
            Row("retry", 9, false, retryFor: "f1"), Row("running", 10, null),
        };

        var chosen = MirrorService.Choose(rows).Select(r => (string)r["id"]).ToList();

        CollectionAssert.AreEqual(new[] { "f2", "bad2", "w4", "w3", "w2" }, chosen);
    }

    [TestMethod]
    public void A_kind_that_is_short_is_topped_up_from_the_rest()
    {
        var chosen = MirrorService.Choose([Row("f1", 1, false), Row("f2", 2, false), Row("f3", 3, false), Row("w1", 4, true)]);

        Assert.AreEqual(4, chosen.Count);
    }

    [TestMethod]
    public void Of_prods_error_only_the_partners_status_and_the_kinds_of_error_are_kept()
    {
        var (status, types) = MirrorService.ErrorOf("500: InternalServerError: {\"message\":\"Order SO-1001 not found\"}");
        Assert.AreEqual(500, status);
        Assert.AreEqual(0, types.Count);

        (status, types) = MirrorService.ErrorOf(
            "System.Net.Http.HttpRequestException: Connection refused (sftp.acme.com:22) ---> System.Net.Sockets.SocketException (61): Connection refused" +
            "\n   at System.ThrowHelper.ThrowException(ExceptionResource resource)");
        Assert.IsNull(status);
        CollectionAssert.AreEqual(new[] { "HttpRequestException", "SocketException" }, types.ToList());

        Assert.AreEqual(0, MirrorService.ErrorOf(null).Types.Count);
    }

    [TestMethod]
    public void The_words_kept_are_the_setups_and_never_its_data()
    {
        var sections = new Dictionary<string, List<JsonObject>>
        {
            ["subscriptions"] =
            [
                (JsonObject)JsonNode.Parse("""
                    { "name": "Orders in", "receiver": { "properties": { "FilePattern": "ORD_*.csv" } },
                      "matchExpression": "status == 'SHIPPED'" }
                    """)!,
            ],
            ["partners"] = [(JsonObject)JsonNode.Parse("""{ "name": "Acme", "properties": { "account": "AC-778" } }""")!],
            ["globalValues"] = [(JsonObject)JsonNode.Parse("""{ "name": "countries", "values": { "JO": "Jordan" } }""")!],
        };

        var words = MirrorSetup.Words(sections);

        foreach (var kept in new[] { "SHIPPED", "ORD", "csv", "Orders in", "Acme", "JO", "countries" })
            Assert.IsTrue(words.Contains(kept), kept);
        foreach (var data in new[] { "AC-778", "AC", "778", "Jordan" })
            Assert.IsFalse(words.Contains(data), data);
    }
}
