using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.NativeAdapters.Pop3Receiver;

namespace SW.Bitween.UnitTests;

[TestClass]
public class NativePop3ReceiverTests
{
    private static Dictionary<string, string> Settings(string encoding = "utf8", int batchSize = 50) => new()
    {
        ["Host"] = "127.0.0.1",
        ["Username"] = "user",
        ["Password"] = "pass",
        ["ResponseEncoding"] = encoding,
        ["BatchSize"] = batchSize.ToString()
    };

    private static NativePop3Receiver CreateReceiver(FakePop3Server server, string encoding = "utf8", int batchSize = 50)
    {
        var receiver = new NativePop3Receiver { Port = server.Port, UseSsl = false };
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

    static System.Security.Cryptography.X509Certificates.X509Certificate2 SelfSigned()
    {
        using var key = System.Security.Cryptography.RSA.Create(2048);
        var request = new System.Security.Cryptography.X509Certificates.CertificateRequest(
            "CN=mail.lab.local", key, System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1);
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        // Exported and read back, so the server can use its private key on every platform.
        return System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12(cert.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pfx), null);
    }

    static NativePop3Receiver Pinned(FakePop3Server server, string? thumbprint)
    {
        var receiver = new NativePop3Receiver();
        var settings = Settings();
        settings["Port"] = server.Port.ToString();
        if (thumbprint != null) settings["CertificateThumbprint"] = thumbprint;
        receiver.InitializeStartupValues(settings);
        return receiver;
    }

    [TestMethod]
    public async Task A_pinned_certificate_is_trusted_though_self_signed()
    {
        using var cert = SelfSigned();
        using var server = new FakePop3Server(new[] { Pop3TestMessages.Plain }, tls: cert);
        var thumbprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(cert.RawData));
        var receiver = Pinned(server, "SHA256:" + thumbprint.ToLowerInvariant());

        await receiver.Initialize();
        Assert.AreEqual(1, (await receiver.ListFiles()).Count());
        await receiver.Finalize();
    }

    [TestMethod]
    public async Task Another_certificate_than_the_pinned_one_is_refused_before_the_password_goes()
    {
        using var cert = SelfSigned();
        using var server = new FakePop3Server(new[] { Pop3TestMessages.Plain }, tls: cert);
        var receiver = Pinned(server, new string('A', 64));

        var refused = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => receiver.Initialize());
        StringAssert.Contains(refused.Message, "not the one configured");
        Assert.IsFalse(server.PasswordSent);
    }

    [TestMethod]
    public async Task Without_a_pin_a_self_signed_certificate_is_refused_as_before()
    {
        using var cert = SelfSigned();
        using var server = new FakePop3Server(new[] { Pop3TestMessages.Plain }, tls: cert);
        var receiver = Pinned(server, null);

        await Assert.ThrowsExceptionAsync<MailKit.Security.SslHandshakeException>(() => receiver.Initialize());
        Assert.IsFalse(server.PasswordSent);
    }
}
