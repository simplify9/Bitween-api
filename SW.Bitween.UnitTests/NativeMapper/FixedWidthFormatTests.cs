using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SW.Bitween.NativeAdapters.Mapper;
using SW.Bitween.NativeAdapters.Mapper.Formats;
using SW.PrimitiveTypes;

namespace SW.Bitween.UnitTests.NativeMapper;

/// <summary>Fixed-width text: each field at a set position, read into rows and written back padded.</summary>
[TestClass]
public class FixedWidthFormatTests
{
    static FixedWidthOptions Layout() => new()
    {
        Columns =
        [
            new() { Name = "type", Start = 1, Length = 1 },
            new() { Name = "reference", Start = 2, Length = 8 },
            new() { Name = "amount", Start = 10, Length = 7, Align = "right", Pad = "0" },
        ],
    };

    static string Text(ValueNode node) => JToken.Parse(new JsonFormat().Write(node)).ToString(Formatting.None);

    [TestMethod]
    public void Lines_are_read_into_rows_at_their_positions()
    {
        var rows = new FixedWidthFormat(Layout()).Read("DSO-1    0001250\nDSO-22   0000099\n\n");
        Assert.AreEqual("""[{"type":"D","reference":"SO-1","amount":"0001250"},{"type":"D","reference":"SO-22","amount":"0000099"}]""",
            Text(rows));
    }

    [TestMethod]
    public void A_short_line_is_read_as_far_as_it_goes_and_header_lines_are_skipped()
    {
        var options = Layout();
        options.SkipLines = 1;
        var rows = new FixedWidthFormat(options).Read("HEADER LINE\nDSO-1\n");
        Assert.AreEqual("""[{"type":"D","reference":"SO-1","amount":""}]""", Text(rows));
    }

    [TestMethod]
    public void Values_are_written_padded_to_their_columns()
    {
        var rows = new FixedWidthFormat(new FixedWidthOptions()
        {
            Columns = Layout().Columns,
        }).Read("DSO-1    0001250\n");
        Assert.AreEqual("DSO-1    0001250\n", new FixedWidthFormat(Layout()).Write(rows));
    }

    [TestMethod]
    public void A_value_too_long_for_its_column_is_refused_not_cut()
    {
        var row = ValueNode.Object();
        row.Set("type", ValueNode.Value("D"));
        row.Set("reference", ValueNode.Value("A-VERY-LONG-REFERENCE"));
        var ex = Assert.ThrowsException<DocumentFormatException>(() => new FixedWidthFormat(Layout()).Write(row));
        StringAssert.Contains(ex.Message, "holds 8");
    }

    [TestMethod]
    public void A_field_the_layout_has_no_column_for_is_refused()
    {
        var row = ValueNode.Object();
        row.Set("customer", ValueNode.Value("acme"));
        Assert.ThrowsException<DocumentFormatException>(() => new FixedWidthFormat(Layout()).Write(row));
    }

    [TestMethod]
    public void Overlapping_columns_are_refused()
    {
        var options = new FixedWidthOptions { Columns = [new() { Name = "a", Start = 1, Length = 5 }, new() { Name = "b", Start = 4, Length = 2 }] };
        Assert.ThrowsException<DocumentFormatException>(() => new FixedWidthFormat(options).Read("12345"));
    }

    [TestMethod]
    public void The_mapper_maps_a_fixed_width_file_to_json()
    {
        var rules = new MappingRules
        {
            SourceFormat = "fixedwidth",
            TargetFormat = "json",
            SourceFixedWidth = Layout(),
            Root = new ListRule
            {
                Over = "",
                Fields = [new FieldRule { Target = ["ref"], From = new ValueSource { Kind = ValueSourceKind.Path, Path = "reference" } }],
            },
        };
        var mapper = new SW.Bitween.NativeAdapters.Mapper.NativeMapper();
        mapper.InitializeStartupValues(new Dictionary<string, string> { ["MappingRules"] = JsonConvert.SerializeObject(rules) });
        var output = mapper.Handle(new XchangeFile("DSO-1    0001250\n")).Result;
        Assert.AreEqual("SO-1", (string)JToken.Parse(output.Data)[0]!["ref"]!);
    }
}
