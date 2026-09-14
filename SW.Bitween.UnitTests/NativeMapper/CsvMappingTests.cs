using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using SW.Bitween.NativeAdapters.Mapper;
using SW.Bitween.NativeAdapters.Mapper.Formats;

namespace SW.Bitween.UnitTests.NativeMapper;

/// <summary>
/// A real client file, mapped end to end.
/// </summary>
/// <remarks>
/// <para>
/// These are the tests that decide whether the design claim holds: that delimited text needs
/// nothing from the engine, because a file <em>is</em> a list of rows and the mapper already walks
/// lists. Nothing here configures a CSV feature — the rules are the same ones a JSON root array
/// uses, and the only thing that changed is which reader produced the tree.
/// </para>
/// <para>
/// The second test is the important one. The pipe file holds three record types in one document,
/// and handling that was supposed to need no new feature: a list's existing "only some entries"
/// filter selects the detail rows and ignores the header and trailer. If that were wrong, the whole
/// shape of the plan would be wrong with it.
/// </para>
/// </remarks>
[TestClass]
public class CsvMappingTests
{
    /// <summary>The client's pipe file: an H record, three D records, a T record.</summary>
    private const string Tracking =
        "H|FFSTAT|1|0||||||||||202609141313|1309981|N\n" +
        "D|1309981172|OK|DELIVERY|0.100|KGM|1|||20260908FRACPKT03831|3800351262|202609141307|20260911|NTE|CDG|NTE||BRIAN MATIAS CASTRO PENA|GLOBAL LOGTICS NETWORK|||||||222998693|Clementine Sandri|\n" +
        "D|1309981174|CC|AWAITING CONSIGNEE COLLECTION|0.100|KGM|1|||E824836443|4472825486|202609141305|20260911|MRS|CDG|MRS||CHRISTOPHER GERGES|GLOBAL LOGISTIC NETWORK|||||||222998693||\n" +
        "D|1309981175|OK|DELIVERY|0.100|KGM|1|||20260908FRACPKT03852|4472869306|202609141309|20260911|GVA|CDG|GVA||AGASH RAMANAN|GLOBAL LOGISTIC NETWORK|||||||222998693|s ramanan|\n" +
        "T|9|1309981|\n";

    /// <summary>The client's comma file, which names its columns.</summary>
    private const string Movements =
        "ShipmentNumber,Reference,TrackingCode,Date,Time,Comment1,Comment2\n" +
        "6G61965126082,202493482,SHOR020,2026-09-14,08:29:49,,\n" +
        "8G49824171336,202340914,SHOR020,2026-09-14,08:34:34,,\n";

    private static ValueSource Path(string path) => new() { Kind = ValueSourceKind.Path, Path = path };

    private static FieldRule Field(string target, string path) =>
        new() { Target = [target], From = Path(path) };

    private static ValueSource Fixed(object? value) =>
        new() { Kind = ValueSourceKind.Fixed, Value = value };

    /// <summary>How many entries the enclosing list holds at the point the rule runs.</summary>
    private static ValueSource Counted() => new() { Kind = ValueSourceKind.Count };

    private static TransformRule Transform(string fn, params (string Name, object Value)[] args)
    {
        var rule = new TransformRule { Fn = fn };
        foreach (var (name, value) in args) rule.Args[name] = JToken.FromObject(value);
        return rule;
    }

    private static string Json(ValueNode tree) => new JsonFormat().Write(tree);

    /// <summary>Maps a delimited document with the given rules, and returns the output as JSON.</summary>
    private static JToken Run(MappingRules rules, string document, CsvOptions source)
    {
        var reader = new CsvFormat(source);
        return JToken.Parse(Json(DocumentMapper.Map(
            rules, reader.Read(document), MappingContext.Empty, reader.SingleValueIsAList)));
    }

    [TestMethod]
    public void A_file_with_a_header_maps_by_column_name()
    {
        // The whole output is the list, walking the document itself — the same two settings a JSON
        // bare array already uses, written before delimited text existed.
        var rules = new MappingRules
        {
            Root = new ListRule
            {
                Over = "",
                Fields =
                [
                    Field("shipment", "ShipmentNumber"),
                    Field("reference", "Reference"),
                    Field("at", "Time"),
                ],
            },
        };

        var output = Run(rules, Movements, new CsvOptions());

        Assert.AreEqual(2, output.Count());
        Assert.AreEqual("6G61965126082", output[0]?["shipment"]?.ToString());
        Assert.AreEqual("202340914", output[1]?["reference"]?.ToString());
        Assert.AreEqual("08:34:34", output[1]?["at"]?.ToString());
    }

    [TestMethod]
    public void Record_types_are_separated_by_the_filter_a_list_already_has()
    {
        // The claim: three record types in one file need no new feature. Field 1 says which kind of
        // record a line is, and "only some entries" is exactly the tool for that.
        var rules = new MappingRules
        {
            Root = new ListRule
            {
                Over = "",
                Where = new FilterRule
                {
                    Field = "1",
                    Operator = FilterOperator.Equal,
                    Value = "D",
                },
                Fields =
                [
                    Field("tracking", "2"),
                    Field("status", "3"),
                    Field("consignee", "18"),
                    Field("weight", "5"),
                ],
            },
        };

        var output = Run(rules, Tracking, new CsvOptions { Delimiter = "|", HasHeader = false });

        // Three detail rows. The H record and the T record are gone, and nothing had to know that
        // they existed.
        Assert.AreEqual(3, output.Count());
        Assert.AreEqual("1309981172", output[0]?["tracking"]?.ToString());
        Assert.AreEqual("CC", output[1]?["status"]?.ToString());
        Assert.AreEqual("AGASH RAMANAN", output[2]?["consignee"]?.ToString());

        // Still text, still exactly as the partner wrote it.
        Assert.AreEqual("0.100", output[0]?["weight"]?.ToString());
    }

    [TestMethod]
    public void A_field_can_be_made_a_real_number_where_the_output_wants_one()
    {
        // Reading leaves every cell as text on purpose. Converting is a decision the rule carries,
        // so it happens where someone asked for it and nowhere else.
        var rules = new MappingRules
        {
            Root = new ListRule
            {
                Over = "",
                Where = new FilterRule { Field = "1", Operator = FilterOperator.Equal, Value = "D" },
                Fields =
                [
                    new FieldRule { Target = ["weight"], From = Path("5"), Type = ValueType.Number },
                    // Left alone beside it: a reference that lost its leading characters would be
                    // rejected by the partner, and this is the same field in the same row.
                    Field("reference", "10"),
                ],
            },
        };

        var output = Run(rules, Tracking, new CsvOptions { Delimiter = "|", HasHeader = false });

        Assert.AreEqual(JTokenType.Float, output[0]?["weight"]?.Type);
        Assert.AreEqual(0.100m, output[0]?["weight"]?.Value<decimal>());
        Assert.AreEqual("20260908FRACPKT03831", output[0]?["reference"]?.ToString());
    }

    [TestMethod]
    public void A_delimited_file_can_be_mapped_into_another_one()
    {
        // The pipe file becoming the comma file, which is a mapping like any other — one format's
        // reader and another's writer, even when they are the same format configured differently.
        var rules = new MappingRules
        {
            Root = new ListRule
            {
                Over = "",
                Where = new FilterRule { Field = "1", Operator = FilterOperator.Equal, Value = "D" },
                Fields = [Field("ShipmentNumber", "11"), Field("TrackingCode", "3")],
            },
        };

        var reader = new CsvFormat(new CsvOptions { Delimiter = "|", HasHeader = false });
        var writer = new CsvFormat(new CsvOptions());

        var written = writer
            .Write(DocumentMapper.Map(rules, reader.Read(Tracking), MappingContext.Empty))
            .Replace("\r\n", "\n");

        Assert.AreEqual(
            "ShipmentNumber,TrackingCode\n" +
            "3800351262,OK\n" +
            "4472825486,CC\n" +
            "4472869306,OK\n",
            written);
    }

    [TestMethod]
    public void A_nested_rule_lands_in_a_dotted_column()
    {
        // Decided rather than discovered: a row is flat, so a nested target has to become a column
        // name. The editor already shows the path this way.
        var rules = new MappingRules
        {
            Root = new ListRule
            {
                Over = "",
                Where = new FilterRule { Field = "1", Operator = FilterOperator.Equal, Value = "D" },
                Fields =
                [
                    new FieldRule { Target = ["destination", "city"], From = Path("16") },
                    new FieldRule { Target = ["destination", "hub"], From = Path("15") },
                ],
            },
        };

        var reader = new CsvFormat(new CsvOptions { Delimiter = "|", HasHeader = false });
        var written = new CsvFormat(new CsvOptions())
            .Write(DocumentMapper.Map(rules, reader.Read(Tracking), MappingContext.Empty))
            .Replace("\r\n", "\n");

        StringAssert.StartsWith(written, "destination.city,destination.hub\n");
        StringAssert.Contains(written, "NTE,CDG\n");
    }

    [TestMethod]
    public void The_rules_never_mention_a_format_so_the_writer_alone_decides()
    {
        // What "every pair of formats works" actually rests on. The tree below is built once and
        // handed to two different writers; nothing in the rules knows which one is coming.
        var fields = new List<FieldRule> { Field("tracking", "2"), Field("status", "3") };
        var filter = new FilterRule { Field = "1", Operator = FilterOperator.Equal, Value = "D" };
        var reader = new CsvFormat(new CsvOptions { Delimiter = "|", HasHeader = false });
        var source = reader.Read(Tracking);

        var asList = DocumentMapper.Map(
            new MappingRules { Root = new ListRule { Over = "", Where = filter, Fields = fields } },
            source, MappingContext.Empty);

        Assert.AreEqual("1309981172", JToken.Parse(Json(asList))[0]?["tracking"]?.ToString());
        StringAssert.Contains(new CsvFormat(new CsvOptions()).Write(asList), "1309981172");

        // XML is the one target that cannot take a list at the top, because a document has exactly
        // one root element and no way to repeat it. That is a fact about XML rather than anything
        // to do with where the rows came from, and it is refused by name rather than producing a
        // document the partner's parser rejects.
        var refused = Assert.ThrowsException<DocumentFormatException>(
            () => new XmlFormat().Write(asList));
        StringAssert.Contains(refused.Message, "one root element");

        // Named the list instead, and the same rows reach XML too.
        var wrapped = DocumentMapper.Map(
            new MappingRules
            {
                Lists = [new ListRule { Over = "", Where = filter, Fields = fields, Target = ["movements", "movement"] }],
            },
            source, MappingContext.Empty);

        StringAssert.Contains(new XmlFormat().Write(wrapped), "<tracking>1309981172</tracking>");
    }

    [TestMethod]
    public void The_whole_file_can_be_produced_header_rows_and_trailer()
    {
        // The end of it: reading the carrier's file was never the hard half. This writes one —
        // an H line, a D line per shipment, and a T line carrying how many there were.
        var rules = new MappingRules
        {
            Root = new ListRule
            {
                Over = "",
                Where = new FilterRule { Field = "1", Operator = FilterOperator.Equal, Value = "D" },
                // The header line, written before anything is walked.
                Fixed =
                [
                    new ListEntry
                    {
                        Fields =
                        [
                            new FieldRule { Target = ["1"], From = Fixed("H") },
                            new FieldRule { Target = ["2"], From = Fixed("FFSTAT") },
                        ],
                    },
                ],
                Fields = [Field("1", "1"), Field("2", "2"), Field("3", "3")],
                // The trailer, written after them — and the only place the count is known.
                After =
                [
                    new ListEntry
                    {
                        Fields =
                        [
                            new FieldRule { Target = ["1"], From = Fixed("T") },
                            new FieldRule { Target = ["2"], From = Counted() },
                        ],
                    },
                ],
            },
        };

        var reader = new CsvFormat(new CsvOptions { Delimiter = "|", HasHeader = false });
        var written = new CsvFormat(new CsvOptions { Delimiter = "|", HasHeader = false })
            .Write(DocumentMapper.Map(rules, reader.Read(Tracking), MappingContext.Empty))
            .Replace("\r\n", "\n");

        // Three detail rows, so the trailer says 3. The header line is not counted: a trailer
        // states how many records there are, and the line announcing the file is not one.
        Assert.AreEqual(
            "H|FFSTAT|\n" +
            "D|1309981172|OK\n" +
            "D|1309981174|CC\n" +
            "D|1309981175|OK\n" +
            "T|3|\n",
            written);
    }

    [TestMethod]
    public void The_count_does_not_move_when_a_header_line_is_added()
    {
        // The reason it counts rows rather than entries. Building the trailer first and adding
        // the header afterwards is the ordinary way round, and the number must not shift under
        // it — nothing on screen would say that it had.
        var trailer = new ListEntry
        {
            Fields = [new FieldRule { Target = ["count"], From = Counted() }],
        };

        ListRule ListWith(List<ListEntry> before) => new()
        {
            Over = "",
            Where = new FilterRule { Field = "1", Operator = FilterOperator.Equal, Value = "D" },
            Fixed = before,
            Fields = [Field("1", "1")],
            After = [trailer],
        };

        var csv = new CsvOptions { Delimiter = "|", HasHeader = false };
        var withoutHeader = Run(new MappingRules { Root = ListWith([]) }, Tracking, csv);
        var withHeader = Run(
            new MappingRules
            {
                Root = ListWith([new ListEntry { Fields = [new FieldRule { Target = ["1"], From = Fixed("H") }] }]),
            },
            Tracking, csv);

        Assert.AreEqual(3, withoutHeader.Last()?["count"]?.Value<int>());
        Assert.AreEqual(3, withHeader.Last()?["count"]?.Value<int>(), "adding a header moved nothing");
    }

    [TestMethod]
    public void The_count_is_the_same_number_wherever_it_is_read()
    {
        // So a format that puts its record count in the header rather than the trailer needs
        // nothing special, and a row can carry "1 of 3" without the numbers disagreeing.
        var rules = new MappingRules
        {
            Root = new ListRule
            {
                Over = "",
                Where = new FilterRule { Field = "1", Operator = FilterOperator.Equal, Value = "D" },
                Fixed = [new ListEntry { Fields = [new FieldRule { Target = ["count"], From = Counted() }] }],
                Fields = [new FieldRule { Target = ["count"], From = Counted() }],
                After = [new ListEntry { Fields = [new FieldRule { Target = ["count"], From = Counted() }] }],
            },
        };

        var output = Run(rules, Tracking, new CsvOptions { Delimiter = "|", HasHeader = false });

        foreach (var row in output)
            Assert.AreEqual(3, row["count"]?.Value<int>());
    }

    [TestMethod]
    public void A_trailer_is_still_written_when_the_source_list_is_empty()
    {
        // A carrier with nothing to report still sends a file, and the partner still expects to
        // be told the count is zero rather than to receive no trailer at all.
        var rules = new MappingRules
        {
            Root = new ListRule
            {
                Over = "",
                Where = new FilterRule { Field = "1", Operator = FilterOperator.Equal, Value = "NONE" },
                Fields = [Field("tracking", "2")],
                After =
                [
                    new ListEntry
                    {
                        Fields = [new FieldRule { Target = ["count"], From = Counted() }],
                    },
                ],
            },
        };

        var output = Run(rules, Tracking, new CsvOptions { Delimiter = "|", HasHeader = false });

        Assert.AreEqual(1, output.Count(), "the trailer, and nothing else");
        Assert.AreEqual(0, output[0]?["count"]?.Value<int>());
    }

    [TestMethod]
    public void Counting_outside_a_list_is_refused_rather_than_answered_with_zero()
    {
        // Zero is a number a partner would act on. There is no list here, so the honest answer
        // is that the rule does not mean anything rather than that the answer is none.
        var rules = new MappingRules
        {
            Fields = [new FieldRule { Target = ["total"], From = Counted() }],
        };

        var thrown = Assert.ThrowsException<MappingFailedException>(() => DocumentMapper.Map(
            rules,
            new CsvFormat(new CsvOptions { Delimiter = "|", HasHeader = false }).Read(Tracking),
            MappingContext.Empty));

        StringAssert.Contains(thrown.Errors[0].Reason, "inside a list");
    }

    [TestMethod]
    public void A_written_file_can_carry_the_mark_Excel_needs()
    {
        // Without it `BEAUTRAIT Raphaël` opens in Excel as mangled text, and nobody downstream
        // can put that right afterwards.
        var tree = ValueNode.List();
        var row = ValueNode.Object();
        row.Set("name", ValueNode.Value("BEAUTRAIT Raphaël"));
        tree.Add(row);

        var plain = new CsvFormat(new CsvOptions()).Write(tree);
        var marked = new CsvFormat(new CsvOptions { ByteOrderMark = true }).Write(tree);

        Assert.IsFalse(plain.StartsWith('\ufeff'), "off unless asked for");
        Assert.IsTrue(marked.StartsWith('\ufeff'));

        // And reading strips it again, so a file we produce and read back is unchanged by it.
        Assert.AreEqual(
            "BEAUTRAIT Raphaël",
            Values.ResolveScalar(
                ((ListNode)new CsvFormat(new CsvOptions()).Read(marked)).Items[0], "name"));
    }
}
