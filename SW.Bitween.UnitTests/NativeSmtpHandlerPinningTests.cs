using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.NativeAdapters.SmtpHandler;
using SW.PrimitiveTypes;

namespace SW.Bitween.UnitTests;

/// <summary>
/// The SMTP handler and a relay with its own certificate: trusted when that certificate is pinned,
/// refused before anything is sent when another is presented, and refused as before when nothing is pinned.
/// </summary>
[TestClass]
public class NativeSmtpHandlerPinningTests
{
    /// <summary>Just enough SMTP with STARTTLS to take one message.</summary>
    sealed class FakeSmtpServer : IDisposable
    {
        readonly TcpListener listener = new(IPAddress.Loopback, 0);
        readonly X509Certificate2 certificate;
        readonly CancellationTokenSource stop = new();

        public int Port { get; }
        public bool PasswordSent { get; private set; }
        public int Delivered { get; private set; }

        public FakeSmtpServer(X509Certificate2 certificate)
        {
            this.certificate = certificate;
            listener.Start();
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            _ = Accept();
        }

        async Task Accept()
        {
            try
            {
                while (!stop.IsCancellationRequested)
                    _ = Serve(await listener.AcceptTcpClientAsync(stop.Token));
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException) { }
        }

        async Task Serve(TcpClient client)
        {
            using var _ = client;
            Stream stream = client.GetStream();
            var (reader, writer) = Open(stream);
            await writer.WriteLineAsync("220 fake ESMTP");
            try
            {
                while (await reader.ReadLineAsync() is { } line)
                {
                    var command = line.Split(' ')[0].ToUpperInvariant();
                    switch (command)
                    {
                        case "EHLO":
                            await writer.WriteLineAsync(stream is SslStream ? "250-fake\r\n250 AUTH PLAIN" : "250-fake\r\n250 STARTTLS");
                            break;
                        case "STARTTLS":
                            await writer.WriteLineAsync("220 go ahead");
                            var ssl = new SslStream(stream, leaveInnerStreamOpen: true);
                            await ssl.AuthenticateAsServerAsync(certificate);
                            stream = ssl;
                            (reader, writer) = Open(stream);
                            break;
                        case "AUTH":
                            PasswordSent = true;
                            if (line.Split(' ').Length < 3)
                            {
                                await writer.WriteLineAsync("334 ");
                                await reader.ReadLineAsync();
                            }
                            await writer.WriteLineAsync("235 ok");
                            break;
                        case "DATA":
                            await writer.WriteLineAsync("354 go");
                            while (await reader.ReadLineAsync() is { } data && data != ".") { }
                            Delivered++;
                            await writer.WriteLineAsync("250 queued");
                            break;
                        case "QUIT":
                            await writer.WriteLineAsync("221 bye");
                            return;
                        default:
                            await writer.WriteLineAsync("250 ok");
                            break;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or System.Security.Authentication.AuthenticationException) { }
        }

        static (StreamReader, StreamWriter) Open(Stream stream) =>
            (new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true),
             new StreamWriter(stream, Encoding.ASCII, 1024, leaveOpen: true) { NewLine = "\r\n", AutoFlush = true });

        public void Dispose()
        {
            stop.Cancel();
            listener.Stop();
        }
    }

    static X509Certificate2 SelfSigned()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=relay.lab.local", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        return X509CertificateLoader.LoadPkcs12(cert.Export(X509ContentType.Pfx), null);
    }

    static NativeSmtpHandler Handler(int port, string? thumbprint)
    {
        var handler = new NativeSmtpHandler();
        var settings = new Dictionary<string, string>
        {
            ["Host"] = "127.0.0.1",
            ["Port"] = port.ToString(),
            ["Password"] = "relay-password",
            ["From"] = "bitween@acme.lab",
            ["To"] = "ops@acme.lab",
            ["Subject"] = "Order {{ orderId }}",
            ["Body"] = "Order {{ orderId }} failed.",
        };
        if (thumbprint != null) settings["CertificateThumbprint"] = thumbprint;
        handler.InitializeStartupValues(settings);
        return handler;
    }

    static readonly XchangeFile Payload = new("{\"orderId\":\"MKT-1\"}");

    [TestMethod]
    public async Task A_pinned_certificate_is_trusted_though_self_signed()
    {
        using var cert = SelfSigned();
        using var server = new FakeSmtpServer(cert);

        await Handler(server.Port, Convert.ToHexString(SHA256.HashData(cert.RawData))).Handle(Payload);

        Assert.IsTrue(server.PasswordSent);
        Assert.AreEqual(1, server.Delivered);
    }

    [TestMethod]
    public async Task Another_certificate_than_the_pinned_one_is_refused_before_anything_is_sent()
    {
        using var cert = SelfSigned();
        using var server = new FakeSmtpServer(cert);

        await Assert.ThrowsExceptionAsync<MailKit.Security.SslHandshakeException>(() => Handler(server.Port, new string('B', 64)).Handle(Payload));

        Assert.IsFalse(server.PasswordSent);
        Assert.AreEqual(0, server.Delivered);
    }

    [TestMethod]
    public async Task Without_a_pin_a_self_signed_certificate_is_refused_as_before()
    {
        using var cert = SelfSigned();
        using var server = new FakeSmtpServer(cert);

        await Assert.ThrowsExceptionAsync<MailKit.Security.SslHandshakeException>(() => Handler(server.Port, null).Handle(Payload));

        Assert.IsFalse(server.PasswordSent);
    }
}
