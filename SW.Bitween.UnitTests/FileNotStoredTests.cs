using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.UnitTests;

/// <summary>
/// A delivered exchange whose output or response file could not be stored: the result stays a success,
/// names no file, and says why in its warning.
/// </summary>
[TestClass]
public class FileNotStoredTests
{
    private static XchangeResult Delivered() =>
        new("x1", null, new XchangeFile("{\"mapped\":1}", "mapped.json") { ContentType = "application/json" },
            new XchangeFile("{\"ok\":1}", "reply.json", badData: true) { ContentType = "application/json" });

    [TestMethod]
    public void An_output_not_stored_names_no_output_file_and_keeps_the_response()
    {
        var result = Delivered();
        result.FileNotStored(XchangeFileType.Output, "storage refused the request");

        Assert.IsTrue(result.Success);
        Assert.IsNull(result.Exception);
        Assert.AreEqual(0, result.OutputSize);
        Assert.IsNull(result.OutputName);
        Assert.IsNull(result.OutputHash);
        // What the mapper produced, not where it is: kept.
        Assert.AreEqual("application/json", result.OutputContentType);
        Assert.AreEqual("{\"ok\":1}".Length, result.ResponseSize);
        Assert.AreEqual("Delivered; the output file could not be stored: storage refused the request", result.Warning);
        Assert.AreEqual("storage refused the request", result.NotStoredReason(XchangeFileType.Output));
        Assert.IsNull(result.NotStoredReason(XchangeFileType.Response));
    }

    [TestMethod]
    public void A_response_not_stored_names_no_response_file_and_keeps_its_bad_flag()
    {
        var result = Delivered();
        result.FileNotStored(XchangeFileType.Response, "timeout");

        Assert.IsTrue(result.Success);
        Assert.AreEqual(0, result.ResponseSize);
        Assert.IsNull(result.ResponseName);
        Assert.IsNull(result.ResponseHash);
        Assert.IsTrue(result.ResponseBad);
        Assert.AreEqual("application/json", result.ResponseContentType);
        Assert.AreEqual("{\"mapped\":1}".Length, result.OutputSize);
        Assert.AreEqual("Delivered; the response file could not be stored: timeout", result.Warning);
        Assert.IsNull(result.NotStoredReason(XchangeFileType.Output));
    }

    [TestMethod]
    public void Both_not_stored_make_one_line_each_within_the_column()
    {
        var result = Delivered();
        result.FileNotStored(XchangeFileType.Output, new string('o', 5000));
        result.FileNotStored(XchangeFileType.Response, "timeout");

        Assert.AreEqual(0, result.OutputSize);
        Assert.AreEqual(0, result.ResponseSize);
        Assert.IsTrue(result.Warning.Length <= XchangeResult.WarningMaxLength);
        Assert.AreEqual(2, result.Warning.Split('\n').Length);
        Assert.AreEqual("timeout", result.NotStoredReason(XchangeFileType.Response));
        StringAssert.StartsWith(result.NotStoredReason(XchangeFileType.Output), "ooo");
    }

    [TestMethod]
    public void A_result_without_a_warning_has_nothing_missing()
    {
        var result = Delivered();
        Assert.IsNull(result.Warning);
        Assert.IsNull(result.NotStoredReason(XchangeFileType.Output));
        Assert.IsNull(XchangeResult.NotStoredReason(null, XchangeFileType.Response));
    }
}
