using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.NativeAdapters.RebexPop3Receiver;

namespace SW.Bitween.UnitTests;

/// <summary>
/// Requires a real Rebex license via the Bitween:RebexLicenseKey configuration value
/// (settable through the Bitween__RebexLicenseKey environment variable).
/// Tests report Inconclusive (not Failed) when it's not set, so CI/dev machines
/// without a license don't fail the build.
/// </summary>
[TestClass]
public class NativeRebexPop3ReceiverTests
{
    private static readonly string LicenseKey = new ConfigurationBuilder()
        .AddEnvironmentVariables()
        .Build()
        .GetSection(BitweenOptions.ConfigurationSection)[nameof(BitweenOptions.RebexLicenseKey)];

    [TestInitialize]
    public void SkipIfNoLicense()
    {
        if (string.IsNullOrEmpty(LicenseKey))
            Assert.Inconclusive($"{BitweenOptions.ConfigurationSection}:{nameof(BitweenOptions.RebexLicenseKey)} is not set.");
    }

    private static Dictionary<string, string> Settings(string encoding = "utf8", int batchSize = 50) => new()
    {
        ["Host"] = "127.0.0.1",
        ["Username"] = "user",
        ["Password"] = "pass",
        ["ResponseEncoding"] = encoding,
        ["BatchSize"] = batchSize.ToString()
    };

    private static NativeRebexPop3Receiver CreateReceiver(FakePop3Server server, string encoding = "utf8", int batchSize = 50)
    {
        var receiver = new NativeRebexPop3Receiver(LicenseKey) { Port = server.Port, UseSsl = false };
        receiver.InitializeStartupValues(Settings(encoding, batchSize));
        return receiver;
    }

    [TestMethod]
    public async Task ListFiles_ReturnsOneEntryPerMessage()
    {
        using var server = new FakePop3Server(new[] { Pop3TestMessages.Plain, Pop3TestMessages.WithAttachment });
        var receiver = CreateReceiver(server);

        await receiver.Initialize();
        var files = (await receiver.ListFiles()).ToList();
        await receiver.Finalize();

        Assert.AreEqual(2, files.Count);
    }

    [TestMethod]
    public async Task ListFiles_RespectsBatchSize()
    {
        using var server = new FakePop3Server(new[]
            { Pop3TestMessages.Plain, Pop3TestMessages.WithAttachment, Pop3TestMessages.Plain });
        var receiver = CreateReceiver(server, batchSize: 2);

        await receiver.Initialize();
        var files = (await receiver.ListFiles()).ToList();
        await receiver.Finalize();

        Assert.AreEqual(2, files.Count);
    }

    [TestMethod]
    public async Task GetFile_WithoutAttachment_ReturnsBodyText()
    {
        using var server = new FakePop3Server(new[] { Pop3TestMessages.Plain });
        var receiver = CreateReceiver(server);

        await receiver.Initialize();
        var files = (await receiver.ListFiles()).ToList();
        var file = await receiver.GetFile(files[0]);
        await receiver.Finalize();

        StringAssert.Contains(file.Data, "Hello, this is the body text.");
        Assert.AreEqual("Plain Message", file.Filename);
    }

    [TestMethod]
    public async Task GetFile_WithAttachment_ReturnsAttachmentAsUtf8()
    {
        using var server = new FakePop3Server(new[] { Pop3TestMessages.WithAttachment });
        var receiver = CreateReceiver(server, "utf8");

        await receiver.Initialize();
        var files = (await receiver.ListFiles()).ToList();
        var file = await receiver.GetFile(files[0]);
        await receiver.Finalize();

        Assert.AreEqual(Pop3TestMessages.AttachmentDecoded, file.Data);
    }

    [TestMethod]
    public async Task GetFile_WithAttachment_ReturnsAttachmentAsBase64()
    {
        using var server = new FakePop3Server(new[] { Pop3TestMessages.WithAttachment });
        var receiver = CreateReceiver(server, "base64");

        await receiver.Initialize();
        var files = (await receiver.ListFiles()).ToList();
        var file = await receiver.GetFile(files[0]);
        await receiver.Finalize();

        Assert.AreEqual(Convert.ToBase64String(Encoding.UTF8.GetBytes(Pop3TestMessages.AttachmentDecoded)), file.Data);
    }

    [TestMethod]
    public async Task GetFile_WithUnknownEncoding_Throws()
    {
        using var server = new FakePop3Server(new[] { Pop3TestMessages.WithAttachment });
        var receiver = CreateReceiver(server, "unknown-encoding");

        await receiver.Initialize();
        var files = (await receiver.ListFiles()).ToList();

        await Assert.ThrowsExceptionAsync<ArgumentException>(() => receiver.GetFile(files[0]));
        await receiver.Finalize();
    }

    [TestMethod]
    public async Task DeleteFile_MarksMessageDeletedOnServer()
    {
        using var server = new FakePop3Server(new[] { Pop3TestMessages.Plain });
        var receiver = CreateReceiver(server);

        await receiver.Initialize();
        var files = (await receiver.ListFiles()).ToList();
        await receiver.DeleteFile(files[0]);
        await receiver.Finalize();

        CollectionAssert.Contains(server.DeletedMessageNumbers.ToList(), 1);
    }
}
