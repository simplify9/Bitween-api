using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.Services.Mirror;

namespace SW.Bitween.UnitTests;

/// <summary>
/// How a rehearsed exchange is compared with prod's: faked values that are only copied read the same,
/// values worked out from them keep their look (check), and a changed ending, format, field or way of
/// writing a value is different. And how staging answers a rehearsed exchange's calls: with what prod's
/// partner answered in that same exchange.
/// </summary>
[TestClass]
public class MirrorRehearsalTests
{
    static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    static readonly MirrorSampleFile Kept = new("mirror/samples/a/x1/output", "out.json", "json", 10, null);

    static MirrorDiff Compare(string prod, string staging) => MirrorCompare.File("output", "What it sent", Kept, Bytes(prod), Bytes(staging));

    [TestMethod]
    public void The_same_file_however_it_is_laid_out_is_the_same()
    {
        var diff = Compare("""{"order":{"ref":"XKQ-1","lines":[{"sku":"A1"}]}}""", "{\n  \"order\": { \"lines\": [ { \"sku\": \"A1\" } ], \"ref\": \"XKQ-1\" }\n}");

        Assert.AreEqual(MirrorCompare.Same, diff.Level);
        Assert.AreEqual("JSON · 2 fields", diff.Prod);
    }

    [TestMethod]
    public void A_missing_field_a_value_written_another_way_and_a_type_change_are_different()
    {
        var diff = Compare("""{"ref":"XKQ-1","on":"2026-11-03","qty":3}""", """{"on":"03/11/2026","qty":"3"}""");

        Assert.AreEqual(MirrorCompare.Different, diff.Level);
        StringAssert.Contains(diff.Note, "1 field missing");
        StringAssert.Contains(diff.Note, "2 values written another way");
        var on = diff.Fields.Single(f => f.Path == "on");
        Assert.AreEqual(("\"2026-11-03\"", "\"03/11/2026\""), (on.Prod, on.Staging));
        Assert.AreEqual("Missing", diff.Fields.Single(f => f.Path == "ref").Staging);
    }

    [TestMethod]
    public void Only_text_that_keeps_its_look_is_a_check()
    {
        // A total worked out from faked amounts: other digits, the same look.
        var diff = Compare("""{"name":"Kara Doyle","total":"4821.37"}""", """{"name":"Kara Doyle","total":"9310.52"}""");

        Assert.AreEqual(MirrorCompare.Check, diff.Level);
        Assert.AreEqual(MirrorCompare.Check, diff.Fields.Single().Level);
        Assert.AreEqual(MirrorCompare.Look("2026-03-14"), MirrorCompare.Look("2031-07-22"));
        Assert.AreNotEqual(MirrorCompare.Look("Kara Doyle"), MirrorCompare.Look("KARA DOYLE"));
    }

    [TestMethod]
    public void A_shorter_list_is_said_once_not_once_per_field_of_its_items()
    {
        var diff = Compare("""{"lines":[{"sku":"A1","qty":1},{"sku":"B2","qty":2}]}""", """{"lines":[{"sku":"A1","qty":1}]}""");

        Assert.AreEqual(MirrorCompare.Different, diff.Level);
        var count = diff.Fields.Single();
        Assert.AreEqual(("lines", "2 items", "1 item"), (count.Path, count.Prod, count.Staging));
    }

    [TestMethod]
    public void Xml_csv_and_text_are_compared_in_their_own_way()
    {
        var xml = MirrorCompare.File("output", "x", Kept, Bytes("""<Order id="7"><Line>A</Line><Line>B</Line></Order>"""),
            Bytes("<Order id=\"7\">\n  <Line>A</Line>\n</Order>"));
        Assert.AreEqual(MirrorCompare.Different, xml.Level);
        Assert.AreEqual(("Order/Line", "2 items", "1 item"), (xml.Fields[0].Path, xml.Fields[0].Prod, xml.Fields[0].Staging));

        var csv = MirrorCompare.File("output", "x", Kept, Bytes("id;name\n1;Kara\n2;Lior\n"), Bytes("id;name\n1;Kara\n"));
        Assert.AreEqual(MirrorCompare.Different, csv.Level);
        Assert.AreEqual(("Rows", "3 rows", "2 rows"), (csv.Fields[0].Path, csv.Fields[0].Prod, csv.Fields[0].Staging));

        var format = MirrorCompare.File("output", "x", Kept, Bytes("""{"a":1}"""), Bytes("<a>1</a>"));
        Assert.AreEqual(MirrorCompare.Different, format.Level);
        Assert.AreEqual("Written as XML where prod's was JSON.", format.Note);
    }

    [TestMethod]
    public void A_file_only_one_side_has_or_prod_didnt_keep_says_so()
    {
        Assert.AreEqual(MirrorCompare.Different, MirrorCompare.File("output", "x", Kept, Bytes("{}"), null).Level);
        Assert.AreEqual(MirrorCompare.Different, MirrorCompare.File("output", "x", null, null, Bytes("{}")).Level);
        Assert.IsNull(MirrorCompare.File("output", "x", null, null, null));
        var notKept = MirrorCompare.File("output", "x", new MirrorSampleFile(null, "a.pdf", "pdf", 0, "PDFs can't be faked."), null, Bytes("%PDF-1.4"));
        Assert.AreEqual(MirrorCompare.Info, notKept.Level);
        StringAssert.Contains(notKept.Note, "PDFs can't be faked.");
    }

    static MirrorSample Sample(string outcome, int? status = null, params string[] errors) =>
        new("x1", DateTime.UtcNow, outcome, status, errors, null, null, null);

    [TestMethod]
    public void How_it_ended_counts_and_what_it_used_only_explains()
    {
        var worse = MirrorCompare.Result(Sample("success"), "failed", null, ["JsonException"]);
        Assert.AreEqual(MirrorCompare.Different, worse.Level);
        Assert.AreEqual(("Worked", "Failed · JsonException"), (worse.Prod, worse.Staging));

        // Prod's call never got through, and the rehearsal replays that: the same failure.
        Assert.AreEqual(MirrorCompare.Same, MirrorCompare.Result(Sample("failed", null, "HttpRequestException", "SocketException"), "failed", null, ["HttpRequestException"]).Level);
        Assert.AreEqual(MirrorCompare.Check, MirrorCompare.Result(Sample("failed", null, "JsonException"), "failed", null, ["NullReferenceException"]).Level);
        Assert.AreEqual(MirrorCompare.Same, MirrorCompare.Result(Sample("failed", 500), "failed", 500, []).Level);

        var used = new MirrorDiff("used", "Handler", MirrorCompare.Info, "acme 1.4", "acme 1.5");
        Assert.AreEqual(MirrorCompare.Same, MirrorCompare.Verdict([MirrorCompare.Result(Sample("success"), "success", null, []), used]));
    }

    static readonly BitweenOptions Mirror = new() { Mirror = { Target = true } };

    [TestMethod]
    public void Only_a_rehearsal_with_an_open_key_on_a_mirror_runs_as_rehearsal_traffic()
    {
        var correlation = MirrorTraffic.NewCorrelation();
        var key = MirrorTraffic.OpenKey();
        var headers = new HeaderDictionary { [MirrorTraffic.Header] = $"{key} {correlation}" };

        Assert.AreEqual(correlation, MirrorTraffic.CorrelationFor(Mirror, headers));
        Assert.IsNull(MirrorTraffic.CorrelationFor(new BitweenOptions(), headers), "not a mirror");
        Assert.IsNull(MirrorTraffic.CorrelationFor(Mirror, new HeaderDictionary { [MirrorTraffic.Header] = $"guessed {correlation}" }));
        Assert.IsNull(MirrorTraffic.CorrelationFor(Mirror, new HeaderDictionary { [MirrorTraffic.Header] = $"{key} 12345" }), "only a rehearsal's own correlation");
        MirrorTraffic.CloseKey(key);
        Assert.IsNull(MirrorTraffic.CorrelationFor(Mirror, headers), "the rehearsal ended");

        Assert.IsTrue(MirrorTraffic.IsRehearsal(Mirror, correlation));
        Assert.IsFalse(MirrorTraffic.IsRehearsal(new BitweenOptions(), correlation));
        Assert.IsFalse(MirrorTraffic.IsRehearsal(Mirror, Guid.NewGuid().ToString("N")));
    }
}
