using System;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.NativeAdapters.S3UploadHandler;

namespace SW.Bitween.UnitTests;

/// <summary>What the S3 and Azure Blob upload handlers write: the text as it is, or base64 decoded into bytes.</summary>
[TestClass]
public class UploadDataEncodingTests
{
    [TestMethod]
    public void Text_is_written_as_utf8_unless_asked_otherwise()
    {
        CollectionAssert.AreEqual(Encoding.UTF8.GetBytes("Raphaël"), NativeS3UploadHandler.Bytes("Raphaël", "utf8"));
        CollectionAssert.AreEqual(Encoding.UTF8.GetBytes("x"), NativeS3UploadHandler.Bytes("x", null));
    }

    [TestMethod]
    public void Base64_is_written_as_the_bytes_it_stands_for()
    {
        var bytes = new byte[] { 0x50, 0x4b, 0x03, 0x04 };
        CollectionAssert.AreEqual(bytes, NativeS3UploadHandler.Bytes(Convert.ToBase64String(bytes), "BASE64"));
    }

    [TestMethod]
    public void An_unknown_encoding_is_refused() =>
        Assert.ThrowsException<ArgumentException>(() => NativeS3UploadHandler.Bytes("x", "latin1"));
}
