using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.NativeAdapters.FileTransfers;
using SW.PrimitiveTypes;

namespace SW.Bitween.UnitTests;

/// <summary>
/// What the FTP and SFTP upload handlers call the file. Every message through one gateway carries the
/// same name, so without a name of its own each upload replaced the last; FileName gives each its own.
/// </summary>
[TestClass]
public class FileUploadNamingTests
{
    static readonly DateTime Now = new(2026, 10, 11, 18, 30, 5, 123, DateTimeKind.Utc);

    static string Name(string data, string? filename, string? fileName = null, string? prefix = null, string encoding = "utf8") =>
        FileUpload<SftpUploadSettings>.NameFor(new XchangeFile(data, filename),
            new SftpUploadSettings { FileName = fileName, FileNamePrefix = prefix, DataEncoding = encoding }, Now);

    const string Order = """{"labId":"f3-1","order":{"id":"MKT-42"}}""";

    [TestMethod]
    public void Without_a_file_name_setting_nothing_changes()
    {
        Assert.AreEqual("acme-bulk-orders.json", Name(Order, "acme-bulk-orders.json"));
        Assert.AreEqual("bulk_acme-bulk-orders.json", Name(Order, "acme-bulk-orders.json", prefix: "bulk"));
        Assert.AreEqual("20261011183005123", Name(Order, null));
    }

    [TestMethod]
    public void A_file_name_setting_is_filled_from_the_message_so_each_message_gets_its_own_file()
    {
        Assert.AreEqual("order-f3-1.json", Name(Order, "acme-bulk-orders.json", "order-{{ labId }}.json"));
        Assert.AreEqual("bulk_MKT-42.json", Name(Order, "acme-bulk-orders.json", "{{ order.id }}.json", prefix: "bulk"));
        Assert.AreNotEqual(
            Name(Order, "acme-bulk-orders.json", "order-{{ labId }}.json"),
            Name(Order.Replace("f3-1", "f3-2"), "acme-bulk-orders.json", "order-{{ labId }}.json"));
    }

    [TestMethod]
    public void A_name_that_comes_out_empty_or_reaches_outside_the_folder_is_made_safe()
    {
        // A field the message doesn't have leaves nothing, and the message's own name is used.
        Assert.AreEqual("acme-bulk-orders.json", Name(Order, "acme-bulk-orders.json", "{{ missing }}"));
        // Only the last segment of a path is kept, so a template can't write elsewhere.
        Assert.AreEqual("passwd", Name("""{"x":"../../etc/passwd"}""", "a.json", "{{ x }}"));
    }

    [TestMethod]
    public void A_message_that_isnt_JSON_takes_the_setting_as_written()
    {
        Assert.AreEqual("daily.csv", Name("a,b\n1,2", "upload.csv", "daily.csv"));
        Assert.AreEqual("export-{{ id }}.bin", Name("AQID", "x.bin", "export-{{ id }}.bin", encoding: "base64"));
    }
}
