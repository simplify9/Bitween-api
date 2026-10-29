extern alias OpenXml;

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenXml::DocumentFormat.OpenXml;
using OpenXml::DocumentFormat.OpenXml.Packaging;
using OpenXml::DocumentFormat.OpenXml.Spreadsheet;
using SW.Bitween.NativeAdapters.Mapper;
using SW.Bitween.NativeAdapters.Mapper.Formats;
using SW.PrimitiveTypes;

namespace SW.Bitween.UnitTests.NativeMapper;

/// <summary>Excel workbooks (.xlsx), carried as base64: one sheet's rows in, a workbook out.</summary>
[TestClass]
public class ExcelFormatTests
{
    static string Json(ValueNode node) => JToken.Parse(new JsonFormat().Write(node)).ToString(Formatting.None);

    /// <summary>
    /// A workbook as Excel itself saves one: text in the shared string table, a number, and a date
    /// stored as a serial number with a date format on its cell.
    /// </summary>
    static string WorkbookAsExcelSavesIt(string sheetName = "Orders")
    {
        using var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var book = document.AddWorkbookPart();
            book.Workbook = new Workbook();

            var strings = book.AddNewPart<SharedStringTablePart>();
            strings.SharedStringTable = new SharedStringTable(
                new SharedStringItem(new Text("Reference")), new SharedStringItem(new Text("Amount")),
                new SharedStringItem(new Text("Ordered")), new SharedStringItem(new Text("041800")));

            var styles = book.AddNewPart<WorkbookStylesPart>();
            styles.Stylesheet = new Stylesheet(
                new Fonts(new Font()), new Fills(new Fill()), new Borders(new Border()),
                new CellFormats(new CellFormat(), new CellFormat { NumberFormatId = 14, ApplyNumberFormat = true }));

            var part = book.AddNewPart<WorksheetPart>();
            part.Worksheet = new Worksheet(new SheetData(
                new Row(Shared("A1", 0), Shared("B1", 1), Shared("C1", 2)) { RowIndex = 1 },
                new Row(Shared("A2", 3), Number("B2", "12.5"), new Cell { CellReference = "C2", StyleIndex = 1, CellValue = new CellValue("46307") }) { RowIndex = 2 }));

            book.Workbook.AppendChild(new Sheets(new Sheet { Id = book.GetIdOfPart(part), SheetId = 1, Name = sheetName }));
            book.Workbook.Save();
        }
        return Convert.ToBase64String(stream.ToArray());

        static Cell Shared(string reference, int index) =>
            new() { CellReference = reference, DataType = CellValues.SharedString, CellValue = new CellValue(index.ToString()) };
        static Cell Number(string reference, string value) => new() { CellReference = reference, CellValue = new CellValue(value) };
    }

    [TestMethod]
    public void A_workbook_as_Excel_saves_it_is_read_as_text_with_dates_in_ISO()
    {
        var rows = new ExcelFormat(null).Read(WorkbookAsExcelSavesIt());
        Assert.AreEqual("""[{"Reference":"041800","Amount":"12.5","Ordered":"2026-10-12"}]""", Json(rows));
    }

    [TestMethod]
    public void A_sheet_is_found_by_name_and_a_missing_one_says_what_there_is()
    {
        var workbook = WorkbookAsExcelSavesIt("Orders");
        Assert.AreEqual(1, ((ListNode)new ExcelFormat(new ExcelOptions { Sheet = "orders" }).Read(workbook)).Items.Count);
        var ex = Assert.ThrowsException<DocumentFormatException>(() => new ExcelFormat(new ExcelOptions { Sheet = "Invoices" }).Read(workbook));
        StringAssert.Contains(ex.Message, "Orders");
    }

    [TestMethod]
    public void Text_that_isnt_base64_says_how_to_send_a_workbook()
    {
        var ex = Assert.ThrowsException<DocumentFormatException>(() => new ExcelFormat(null).Read("Reference,Amount"));
        StringAssert.Contains(ex.Message, "base64");
    }

    [TestMethod]
    public void A_written_workbook_reads_back_the_same_and_keeps_leading_zeros()
    {
        var row = ValueNode.Object();
        row.Set("reference", ValueNode.Value("041800"));
        row.Set("amount", ValueNode.Value(12.5m));
        row.Set("paid", ValueNode.Value(true));
        var list = ValueNode.List();
        list.Add(row);

        var written = new ExcelFormat(new ExcelOptions { Sheet = "Out" }).Write(list);

        Assert.AreEqual("""[{"reference":"041800","amount":"12.5","paid":"true"}]""",
            Json(new ExcelFormat(new ExcelOptions { Sheet = "Out" }).Read(written)));
    }

    [TestMethod]
    public void The_mapper_maps_a_workbook_to_json()
    {
        var rules = new MappingRules
        {
            SourceFormat = "excel",
            TargetFormat = "json",
            Root = new ListRule
            {
                Over = "",
                Fields = [new FieldRule { Target = ["ref"], From = new ValueSource { Kind = ValueSourceKind.Path, Path = "Reference" } }],
            },
        };
        var mapper = new SW.Bitween.NativeAdapters.Mapper.NativeMapper();
        mapper.InitializeStartupValues(new Dictionary<string, string> { ["MappingRules"] = JsonConvert.SerializeObject(rules) });
        var output = mapper.Handle(new XchangeFile(WorkbookAsExcelSavesIt())).Result;
        Assert.AreEqual("041800", (string)JToken.Parse(output.Data)[0]!["ref"]!);
    }

    [TestMethod]
    [DataRow("yyyy-mm-dd", true)]
    [DataRow("dd/mm/yyyy hh:mm", true)]
    [DataRow("#,##0.00", false)]
    [DataRow("\"Day\" 0", false)]
    [DataRow("[Red]0.00", false)]
    public void Date_formats_are_told_from_number_formats(string code, bool isDate) =>
        Assert.AreEqual(isDate, ExcelFormat.IsDateCode(code));
}
