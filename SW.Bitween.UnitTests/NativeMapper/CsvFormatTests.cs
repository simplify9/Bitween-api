using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.NativeAdapters.Mapper;
using SW.Bitween.NativeAdapters.Mapper.Formats;

namespace SW.Bitween.UnitTests.NativeMapper;

/// <summary>
/// Reading delimited text into the neutral tree.
/// </summary>
/// <remarks>
/// The three shapes here are the three files a single client actually sends, with nothing
/// smoothed over: a comma file with a header, a pipe file with three record types and no header,
/// and a semicolon file with no header, blank lines between blocks and an accented name in it.
/// Between them they are the reason none of the delimiter, the header or the encoding is inferred.
/// </remarks>
[TestClass]
public class CsvFormatReadTests
{
    /// <summary>Comma, with a header naming seven columns.</summary>
    private const string Commas =
        """
        ShipmentNumber,Reference,TrackingCode,Date,Time,Comment1,Comment2
        6G61965126082,202493482,SHOR020,2026-09-14,08:29:49,,
        8G49824171336,202340914,SHOR020,2026-09-14,08:34:34,,
        """;

    /// <summary>Pipe, no header, three record types told apart by the first field.</summary>
    private const string Pipes =
        """
        H|FFSTAT|1|0||||||||||202609141313|1309981|N
        D|1309981172|OK|DELIVERY|0.100|KGM|1|||20260908FRACPKT03831|3800351262|202609141307|20260911|NTE|CDG|NTE||BRIAN MATIAS CASTRO PENA|GLOBAL LOGTICS NETWORK|||||||222998693|Clementine Sandri|
        D|1309981174|CC|AWAITING CONSIGNEE COLLECTION|0.100|KGM|1|||E824836443|4472825486|202609141305|20260911|MRS|CDG|MRS||CHRISTOPHER GERGES|GLOBAL LOGISTIC NETWORK|||||||222998693||
        T|9|1309981|
        """;

    /// <summary>Semicolon, no header, blank lines between blocks, an accented name.</summary>
    private const string Semicolons =
        "track;ZT6090386150DE;CDGSF1;SHTW001;20260914;123938;CDG;M+;;;TS;;;BEAUTRAIT Raphaël;ZT6090386150DE;;041800\n" +
        "\n" +
        "\n" +
        "track;ZT6090386205DE;CDGSF1;SHTW001;20260914;123938;CDG;M+;;;TS;;;BEAUTRAIT Raphaël;ZT6090386205DE;;041800\n";

    private static CsvFormat Format(string delimiter = ",", bool header = true) =>
        new(new CsvOptions { Delimiter = delimiter, HasHeader = header });

    /// <summary>The rows, as a rule walking the document would see them.</summary>
    private static ListNode Rows(string text, string delimiter = ",", bool header = true)
    {
        var read = Format(delimiter, header).Read(text);
        return read as ListNode ?? throw new AssertFailedException("a delimited file reads as a list");
    }

    /// <summary>The value at <paramref name="path"/> of one row, the way a rule reads it.</summary>
    private static object At(ValueNode row, string path) => Values.ResolveScalar(row, path);

    [TestMethod]
    public void A_file_is_a_list_of_rows()
    {
        // Not an object with a list inside it: the document *is* the list, which is what lets a
        // rule walk it with the same empty path a JSON root array already uses.
        var read = Format().Read(Commas);

        Assert.IsInstanceOfType<ListNode>(read);
        Assert.AreEqual(2, ((ListNode)read).Items.Count, "the header is not a row");
    }

    [TestMethod]
    public void A_header_names_the_columns()
    {
        var rows = Rows(Commas);

        Assert.AreEqual("6G61965126082", At(rows.Items[0], "ShipmentNumber"));
        Assert.AreEqual("SHOR020", At(rows.Items[0], "TrackingCode"));
        Assert.AreEqual("08:34:34", At(rows.Items[1], "Time"));
    }

    [TestMethod]
    public void An_empty_field_is_empty_rather_than_missing()
    {
        // `,,` at the end of every row of the real file. Present and empty is what the file says,
        // and it is different from a column that is not there at all.
        var rows = Rows(Commas);

        Assert.AreEqual("", At(rows.Items[0], "Comment1"));
        Assert.IsNull(At(rows.Items[0], "Comment3"), "a column the file does not have");
    }

    [TestMethod]
    public void Without_a_header_the_fields_are_numbered_from_one()
    {
        var rows = Rows(Pipes, "|", header: false);

        Assert.AreEqual(4, rows.Items.Count, "every line is a row when nothing is a header");
        Assert.AreEqual("H", At(rows.Items[0], "1"));
        Assert.AreEqual("D", At(rows.Items[1], "1"));
        Assert.AreEqual("1309981172", At(rows.Items[1], "2"));
        Assert.AreEqual("BRIAN MATIAS CASTRO PENA", At(rows.Items[1], "18"));
    }

    [TestMethod]
    public void Rows_of_different_record_types_keep_their_own_lengths()
    {
        // The reason this matters: the header record has 16 fields, a detail row 28 and the
        // trailer 4. Padding them to a common width would invent data; truncating would lose it.
        var rows = Rows(Pipes, "|", header: false);

        Assert.AreEqual("N", At(rows.Items[0], "16"));
        Assert.IsNull(At(rows.Items[0], "17"), "the header record has no 17th field");
        Assert.AreEqual("1309981", At(rows.Items[3], "3"));
        Assert.IsNull(At(rows.Items[3], "5"), "the trailer has four fields");
    }

    [TestMethod]
    public void A_leading_zero_survives()
    {
        // The whole reason nothing is allowed to decide a field looks like a number. 041800 as
        // 41800 is a reference the partner rejects, and nothing here would ever say why.
        var rows = Rows(Semicolons, ";", header: false);

        Assert.AreEqual("041800", At(rows.Items[0], "17"));
    }

    [TestMethod]
    public void A_decimal_keeps_the_scale_it_was_written_with()
    {
        var rows = Rows(Pipes, "|", header: false);

        Assert.AreEqual("0.100", At(rows.Items[1], "5"), "not 0.1");
    }

    [TestMethod]
    public void Blank_lines_between_records_are_not_rows()
    {
        // The semicolon file has runs of them between blocks. Read as rows they would each map to
        // an entry of nothing, and a partner would receive a file padded with empty records.
        var rows = Rows(Semicolons, ";", header: false);

        Assert.AreEqual(2, rows.Items.Count);
    }

    [TestMethod]
    public void An_accented_name_is_read_as_written()
    {
        var rows = Rows(Semicolons, ";", header: false);

        Assert.AreEqual("BEAUTRAIT Raphaël", At(rows.Items[0], "14"));
    }

    [TestMethod]
    public void A_byte_order_mark_is_not_part_of_the_first_column_name()
    {
        // Excel writes one. Left in place it becomes part of the first header name, so every rule
        // reading that column resolves to nothing while the editor shows a name that looks right.
        var rows = Rows("﻿" + Commas);

        Assert.AreEqual("6G61965126082", At(rows.Items[0], "ShipmentNumber"));
    }

    [TestMethod]
    public void A_value_holding_the_delimiter_is_one_field()
    {
        var rows = Rows(
            """
            sku,address,qty
            A1,"Flat 3, Rainbow St",2
            """);

        Assert.AreEqual("Flat 3, Rainbow St", At(rows.Items[0], "address"));
        Assert.AreEqual("2", At(rows.Items[0], "qty"), "the quantity has not shifted a column");
    }

    [TestMethod]
    public void A_value_holding_a_line_break_is_still_one_row()
    {
        // Two rows of data written across four lines of text. This is where a reader that splits
        // on newlines produces three broken rows and nothing complains.
        var rows = Rows("sku,address\nA1,\"Flat 3\nRainbow Street\nAmman\"\n");

        Assert.AreEqual(1, rows.Items.Count);
        Assert.AreEqual("Flat 3\nRainbow Street\nAmman", At(rows.Items[0], "address"));
    }

    [TestMethod]
    public void A_doubled_quote_is_one_quote()
    {
        var rows = Rows("sku,note\nA1,\"He said \"\"urgent\"\" twice\"\n");

        Assert.AreEqual("He said \"urgent\" twice", At(rows.Items[0], "note"));
    }

    [TestMethod]
    public void Whitespace_inside_a_field_is_kept()
    {
        // ` s ramanan` and a postcode with a leading space both turned up in real files. Trimming
        // is a decision about the partner's data, and not one to take on their behalf.
        var rows = Rows("sku,name\nA1, s ramanan \n");

        Assert.AreEqual(" s ramanan ", At(rows.Items[0], "name"));
    }

    [TestMethod]
    public void A_repeated_column_name_stays_reachable_by_position()
    {
        // Two columns cannot share a name — a row is built by setting keys on an object, so the
        // second would silently replace the first and a column would simply be gone.
        var rows = Rows("code,code,qty\nA1,B7,2");

        Assert.AreEqual("A1", At(rows.Items[0], "code"));
        Assert.AreEqual("B7", At(rows.Items[0], "2"), "the repeat falls back to its position");
        Assert.AreEqual("2", At(rows.Items[0], "qty"));
    }

    [TestMethod]
    public void A_field_past_the_end_of_the_header_is_still_readable()
    {
        var rows = Rows("sku,qty\nA1,2,surprise");

        Assert.AreEqual("surprise", At(rows.Items[0], "3"));
    }

    [TestMethod]
    public void A_file_of_nothing_but_a_header_is_no_rows_rather_than_an_error()
    {
        // A carrier with nothing to report sends exactly this, every morning.
        var rows = Rows("ShipmentNumber,Reference\n");

        Assert.AreEqual(0, rows.Items.Count);
    }

    [TestMethod]
    public void An_empty_document_is_refused()
    {
        var thrown = Assert.ThrowsException<DocumentFormatException>(() => Format().Read("   "));

        Assert.IsFalse(string.IsNullOrWhiteSpace(thrown.Message));
    }

    [TestMethod]
    public void The_content_type_says_what_it_is()
    {
        // The previous mapper left this unset and the gateway fell back to application/json, so a
        // partner was served a delimited file labelled as JSON.
        Assert.AreEqual("text/csv", Format().ContentType);
    }
}

/// <summary>
/// Writing the neutral tree back out as delimited text.
/// </summary>
[TestClass]
public class CsvFormatWriteTests
{
    private static CsvFormat Format(string delimiter = ",", bool header = true) =>
        new(new CsvOptions { Delimiter = delimiter, HasHeader = header });

    /// <summary>The document, with newlines normalised so an expectation holds on any platform.</summary>
    private static string Written(ValueNode tree, string delimiter = ",", bool header = true) =>
        Format(delimiter, header).Write(tree).Replace("\r\n", "\n");

    private static ObjectNode Obj(params (string Key, ValueNode Node)[] children)
    {
        var node = ValueNode.Object();
        foreach (var (key, child) in children) node.Set(key, child);
        return node;
    }

    private static ValueNode V(object? value) => ValueNode.Value(value);

    private static ListNode L(params ValueNode[] items)
    {
        var list = ValueNode.List();
        foreach (var item in items) list.Add(item);
        return list;
    }

    private static string Refuses(ValueNode tree)
    {
        var thrown = Assert.ThrowsException<DocumentFormatException>(() => Format().Write(tree));
        Assert.IsFalse(string.IsNullOrWhiteSpace(thrown.Message), "a refusal must explain itself");
        return thrown.Message;
    }

    [TestMethod]
    public void A_list_of_records_is_a_header_and_a_row_each()
    {
        var written = Written(L(
            Obj(("sku", V("A1")), ("qty", V(2m))),
            Obj(("sku", V("B7")), ("qty", V(5m)))));

        Assert.AreEqual("sku,qty\nA1,2\nB7,5\n", written);
    }

    [TestMethod]
    public void An_object_is_a_single_row()
    {
        // A mapping whose output is one record has no reason to be made a list of one just to
        // reach this format.
        Assert.AreEqual("sku,qty\nA1,2\n", Written(Obj(("sku", V("A1")), ("qty", V(2m)))));
    }

    [TestMethod]
    public void A_nested_field_becomes_a_dotted_column()
    {
        // The editor already shows the path this way, and the output-field name box splits what is
        // typed into it on dots — so this is the only way such a column can exist at all.
        var written = Written(L(Obj(
            ("orderId", V("A1")),
            ("destination", Obj(("city", V("Amman")), ("country", V("JO")))))));

        Assert.AreEqual("orderId,destination.city,destination.country\nA1,Amman,JO\n", written);
    }

    [TestMethod]
    public void Columns_are_every_column_any_row_has()
    {
        // Rows can differ: entries written into a list carry their own rules. Taking the first
        // row's columns would drop the rest without a word.
        var written = Written(L(
            Obj(("sku", V("A1"))),
            Obj(("sku", V("B7")), ("note", V("gift")))));

        Assert.AreEqual("sku,note\nA1,\nB7,gift\n", written);
    }

    [TestMethod]
    public void A_column_a_row_lacks_is_written_empty_rather_than_skipped()
    {
        var written = Written(L(
            Obj(("a", V("1")), ("b", V("2")), ("c", V("3"))),
            Obj(("a", V("9")), ("c", V("8")))));

        // Not `9,8`, which would put 8 under b and shift everything after it.
        Assert.AreEqual("a,b,c\n1,2,3\n9,,8\n", written);
    }

    [TestMethod]
    public void A_value_holding_the_delimiter_is_quoted()
    {
        var written = Written(L(Obj(("sku", V("A1")), ("address", V("Flat 3, Rainbow St")))));

        Assert.AreEqual("sku,address\nA1,\"Flat 3, Rainbow St\"\n", written);
    }

    [TestMethod]
    public void A_value_holding_a_quote_has_it_doubled()
    {
        var written = Written(L(Obj(("note", V("He said \"urgent\" twice")))));

        Assert.AreEqual("note\n\"He said \"\"urgent\"\" twice\"\n", written);
    }

    [TestMethod]
    public void A_value_holding_a_line_break_is_quoted()
    {
        var written = Written(L(Obj(("address", V("Flat 3\nAmman")))));

        Assert.AreEqual("address\n\"Flat 3\nAmman\"\n", written);
    }

    [TestMethod]
    public void The_delimiter_is_whatever_was_configured()
    {
        var tree = L(Obj(("a", V("1")), ("b", V("2"))));

        Assert.AreEqual("a;b\n1;2\n", Written(tree, ";"));
        Assert.AreEqual("a|b\n1|2\n", Written(tree, "|"));
        Assert.AreEqual("a\tb\n1\t2\n", Written(tree, "\t"));
    }

    [TestMethod]
    public void Without_a_header_only_the_rows_are_written()
    {
        var written = Written(L(Obj(("1", V("D")), ("2", V("1309981172")))), "|", header: false);

        Assert.AreEqual("D|1309981172\n", written);
    }

    [TestMethod]
    public void A_whole_number_has_no_decimal_point()
    {
        Assert.AreEqual("qty\n2\n", Written(L(Obj(("qty", V(2m))))));
    }

    [TestMethod]
    public void A_decimal_keeps_its_scale_and_uses_a_point()
    {
        // A comma here would become an extra column in a comma-delimited file.
        Assert.AreEqual("weight\n0.100\n", Written(L(Obj(("weight", V(0.100m))))));
    }

    [TestMethod]
    public void Null_is_an_empty_field()
    {
        Assert.AreEqual("a,b\n1,\n", Written(L(Obj(("a", V("1")), ("b", V(null))))));
    }

    [TestMethod]
    public void A_boolean_is_lower_case()
    {
        Assert.AreEqual("ok\ntrue\n", Written(L(Obj(("ok", V(true))))));
    }

    [TestMethod]
    public void A_list_of_plain_values_is_one_column()
    {
        // A file of tracking numbers and nothing else. There is no name to take, so the column
        // takes the name any column has when nothing names it.
        Assert.AreEqual("1\nA1\nB7\n", Written(L(V("A1"), V("B7"))));
    }

    [TestMethod]
    public void A_list_inside_a_row_is_refused_with_a_reason()
    {
        var message = Refuses(L(Obj(("sku", V("A1")), ("tags", L(V("cold"), V("fragile"))))));

        StringAssert.Contains(message, "tags");
    }

    [TestMethod]
    public void A_single_value_output_is_refused()
    {
        var message = Refuses(V("just this"));

        StringAssert.Contains(message, "list or an object");
    }
}

/// <summary>
/// Reading a file and writing it back, which is the one test that holds both halves to each other.
/// </summary>
[TestClass]
public class CsvRoundTripTests
{
    [TestMethod]
    public void A_comma_file_survives_being_read_and_written()
    {
        const string original =
            "ShipmentNumber,Reference,Comment1\r\n" +
            "6G61965126082,202493482,\r\n" +
            "8G49824171336,202340914,\"a, comment\"\r\n";

        var format = new CsvFormat(new CsvOptions());
        var written = format.Write(format.Read(original));

        Assert.AreEqual(original, written);
    }

    [TestMethod]
    public void A_pipe_file_with_no_header_survives_too()
    {
        // Ragged on purpose: three records of three different widths, which is the real file.
        const string original =
            "H|FFSTAT|1\r\n" +
            "D|1309981172|OK|DELIVERY\r\n" +
            "T|9|1309981\r\n";

        var format = new CsvFormat(new CsvOptions { Delimiter = "|", HasHeader = false });
        var written = format.Write(format.Read(original));

        // Not identical: every row is written to the widest shape, because a column missing from a
        // row has to be written empty or every field after it lands in the wrong column. Reading it
        // back gives the same values, which is what a mapping actually depends on.
        Assert.AreEqual(
            "H|FFSTAT|1|\r\n" +
            "D|1309981172|OK|DELIVERY\r\n" +
            "T|9|1309981|\r\n",
            written);
    }
}
