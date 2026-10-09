using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using SW.Bitween.NativeAdapters.FileTransfers;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.TransportTests;

/// <summary>
/// The SFTP adapters, built on SSH.NET, against a real SFTP server. They need no license, so unlike
/// the Rebex adapters' tests these always run.
/// </summary>
public class OpenSftpTests(SftpFixture sftp) : IClassFixture<SftpFixture>
{
    Dictionary<string, string> Settings(params (string Key, string Value)[] more)
    {
        var settings = new Dictionary<string, string>
        {
            ["Host"] = sftp.Host,
            ["Port"] = sftp.Port.ToString(),
            ["Username"] = SftpFixture.User,
            ["Password"] = SftpFixture.Password,
        };
        foreach (var (key, value) in more) settings[key] = value;
        return settings;
    }

    [Fact]
    public async Task The_upload_handler_puts_the_file_in_the_target_folder_with_its_prefix()
    {
        await Adapters.Create<NativeSftpUploadHandler>(Settings(("TargetPath", "outbox"), ("FileNamePrefix", "acme")))
            .Handle(new XchangeFile("{\"order\":1}", "order-1.json"));

        Assert.Contains("acme_order-1.json", await sftp.ListAsync("outbox"));
        Assert.Equal("{\"order\":1}", await sftp.ReadAsync("outbox/acme_order-1.json"));
    }

    [Fact]
    public async Task A_file_name_from_the_message_cannot_climb_out_of_the_target_folder()
    {
        await Adapters.Create<NativeSftpUploadHandler>(Settings(("TargetPath", "outbox")))
            .Handle(new XchangeFile("climbing", "../../inbox/escaped.txt"));

        Assert.Contains("escaped.txt", await sftp.ListAsync("outbox"));
        Assert.DoesNotContain("escaped.txt", await sftp.ListAsync("inbox"));
    }

    [Fact]
    public async Task The_receiver_reads_the_folder_and_moves_each_file_once_received()
    {
        var name = $"r{Guid.NewGuid():N}.json"[..12];
        await sftp.PutAsync($"inbox/{name}", "{\"n\":1}");
        var receiver = Adapters.Create<NativeSftpReceiver>(Settings(("TargetPath", "inbox"), ("DeleteMovesFileTo", "../done")));
        await receiver.Initialize();

        Assert.Contains(name, await receiver.ListFiles());
        Assert.Equal("{\"n\":1}", (await receiver.GetFile(name)).Data);
        await receiver.DeleteFile(name);
        // Already moved — a retried run — is skipped quietly.
        await receiver.DeleteFile(name);
        await receiver.Finalize();

        Assert.DoesNotContain(name, await sftp.ListAsync("inbox"));
        Assert.Contains(name, await sftp.ListAsync("done"));
    }

    [Fact]
    public async Task A_file_still_being_written_is_left_for_the_next_run()
    {
        var name = $"w{Guid.NewGuid():N}.json"[..12];
        await sftp.PutAsync($"inbox/{name}", "half");
        var receiver = Adapters.Create<NativeSftpReceiver>(Settings(("TargetPath", "inbox"), ("MinimumFileAgeSeconds", "300")));
        await receiver.Initialize();

        Assert.DoesNotContain(name, await receiver.ListFiles());
        await receiver.Finalize();
    }

    [Fact]
    public async Task A_PEM_key_pasted_onto_one_line_signs_in_and_the_pinned_host_key_is_accepted()
    {
        var receiver = Adapters.Create<NativeSftpReceiver>(Settings(("Password", ""),
            ("PrivateKey", sftp.PrivateKey.ReplaceLineEndings(" ")),
            ("HostKeyFingerprint", sftp.HostKeyFingerprint), ("TargetPath", "inbox")));

        await receiver.Initialize();
        await receiver.ListFiles();
        await receiver.Finalize();
    }

    [Fact]
    public async Task An_ed25519_key_in_the_OpenSSH_format_signs_in()
    {
        var receiver = Adapters.Create<NativeSftpReceiver>(Settings(("Password", ""),
            ("PrivateKey", sftp.OpenSshPrivateKey), ("TargetPath", "inbox")));

        await receiver.Initialize();
        await receiver.ListFiles();
        await receiver.Finalize();
    }

    [Fact]
    public async Task A_server_whose_host_key_is_not_the_configured_one_gets_no_credentials()
    {
        var receiver = Adapters.Create<NativeSftpReceiver>(Settings(("TargetPath", "inbox"),
            ("HostKeyFingerprint", "SHA256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")));

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => receiver.Initialize());
        Assert.Contains("Refusing to send credentials", refused.Message);
    }

    [Fact]
    public async Task A_wrong_password_and_neither_password_nor_key_are_refused()
    {
        await Assert.ThrowsAnyAsync<Exception>(() =>
            Adapters.Create<NativeSftpReceiver>(Settings(("Password", "wrong"))).Initialize());

        var nothing = await Assert.ThrowsAsync<ArgumentException>(() =>
            Adapters.Create<NativeSftpReceiver>(Settings(("Password", ""))).Initialize());
        Assert.Contains("password or a private key", nothing.Message);
    }
}

/// <summary>A real FTP server: plain, explicit FTPS or implicit FTPS, with a self-signed certificate.</summary>
public abstract class FtpServer : IAsyncLifetime
{
    public const string User = "partner";
    public const string Password = "partner-pass";

    readonly string certificateDirectory = Path.Combine(Path.GetTempPath(), $"ftps-{Guid.NewGuid():N}");
    protected abstract string Mode { get; }
    protected abstract int FirstPassivePort { get; }
    int ControlPort => Mode == "implicit" ? 990 : 21;

    public IContainer Container { get; private set; } = null!;
    public string Host => "127.0.0.1";
    public int Port => Container.GetMappedPublicPort(ControlPort);

    /// <summary>The server certificate's SHA-256 thumbprint, as a partner would hand it over.</summary>
    public string CertificateThumbprint { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var builder = new ContainerBuilder()
            .WithImage("delfer/alpine-ftp-server:latest")
            .WithEnvironment("USERS", $"{User}|{Password}|/home/{User}|1001")
            .WithPortBinding(ControlPort, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(ControlPort));
        // Passive mode tells the client which port to open next, so those ports are published as
        // they are, not remapped.
        for (var port = FirstPassivePort; port <= FirstPassivePort + 4; port++)
            builder = builder.WithPortBinding(port, port);

        var passive = $"-opasv_min_port={FirstPassivePort} -opasv_max_port={FirstPassivePort + 4} -opasv_address=127.0.0.1";
        if (Mode == "none")
        {
            builder = builder
                .WithEnvironment("ADDRESS", "127.0.0.1")
                .WithEnvironment("MIN_PORT", FirstPassivePort.ToString())
                .WithEnvironment("MAX_PORT", (FirstPassivePort + 4).ToString());
        }
        else
        {
            Directory.CreateDirectory(certificateDirectory);
            using (var openssl = System.Diagnostics.Process.Start("openssl", ["req", "-x509", "-newkey", "rsa:2048", "-nodes",
                       "-keyout", Path.Combine(certificateDirectory, "key.pem"), "-out", Path.Combine(certificateDirectory, "cert.pem"),
                       "-days", "2", "-subj", "/CN=ftp.partner.test"]))
                await openssl!.WaitForExitAsync();
            var certificate = X509Certificate2.CreateFromPem(await File.ReadAllTextAsync(Path.Combine(certificateDirectory, "cert.pem")));
            CertificateThumbprint = Convert.ToHexString(SHA256.HashData(certificate.RawData));

            var tls = "-orsa_cert_file=/etc/ftps/cert.pem -orsa_private_key_file=/etc/ftps/key.pem -ossl_enable=YES " +
                      "-oforce_local_data_ssl=YES -oforce_local_logins_ssl=YES -ossl_tlsv1=NO -ossl_sslv2=NO -ossl_sslv3=NO " +
                      "-orequire_ssl_reuse=NO";
            var implicitTls = Mode == "implicit" ? "-oimplicit_ssl=YES -olisten_port=990" : "";
            builder = builder
                .WithResourceMapping(new FileInfo(Path.Combine(certificateDirectory, "cert.pem")), "/etc/ftps/")
                .WithResourceMapping(new FileInfo(Path.Combine(certificateDirectory, "key.pem")), "/etc/ftps/")
                // The image creates the users, then runs this in place of its own vsftpd line.
                // vsftpd forks into the background (its config says so), so the shell stays as the main process.
                .WithCommand("sh", "-c", $"vsftpd {passive} {tls} {implicitTls} /etc/vsftpd/vsftpd.conf && exec sleep infinity");
        }

        Container = builder.Build();
        await Container.StartAsync();
        await Container.ExecAsync(["sh", "-c", $"mkdir -p /home/{User}/inbox /home/{User}/outbox /home/{User}/done && chown -R 1001 /home/{User}"]);
    }

    public async Task<string> ListAsync(string folder) =>
        (await Container.ExecAsync(["ls", "-1", $"/home/{User}/{folder}"])).Stdout;

    public async Task PutAsync(string path, string text) =>
        await Container.ExecAsync(["sh", "-c", $"printf '%s' '{text}' > /home/{User}/{path} && chown 1001 /home/{User}/{path}"]);

    public async Task<string> ReadAsync(string path) =>
        (await Container.ExecAsync(["cat", $"/home/{User}/{path}"])).Stdout;

    public Dictionary<string, string> Settings(params (string Key, string Value)[] more)
    {
        var settings = new Dictionary<string, string>
        {
            ["Host"] = Host,
            ["Port"] = Port.ToString(),
            ["Username"] = User,
            ["Password"] = Password,
            ["Encryption"] = Mode,
        };
        if (CertificateThumbprint != null) settings["CertificateThumbprint"] = CertificateThumbprint;
        foreach (var (key, value) in more) settings[key] = value;
        return settings;
    }

    public async Task DisposeAsync()
    {
        if (Container != null) await Container.DisposeAsync();
        try { Directory.Delete(certificateDirectory, true); } catch { }
    }
}

public sealed class PlainFtpServer : FtpServer
{
    protected override string Mode => "none";
    protected override int FirstPassivePort => 21100;
}

public sealed class ExplicitFtpsServer : FtpServer
{
    protected override string Mode => "explicit";
    protected override int FirstPassivePort => 21120;
}

public sealed class ImplicitFtpsServer : FtpServer
{
    protected override string Mode => "implicit";
    protected override int FirstPassivePort => 21140;
}

/// <summary>The FTP adapters, built on FluentFTP, against a real plain FTP server.</summary>
public class OpenFtpTests(PlainFtpServer ftp) : IClassFixture<PlainFtpServer>
{
    const string Home = $"/home/{FtpServer.User}";

    [Fact]
    public async Task The_upload_handler_puts_the_file_in_the_target_folder_decoding_base64()
    {
        await Adapters.Create<NativeFtpUploadHandler>(ftp.Settings(("TargetPath", $"{Home}/outbox"), ("DataEncoding", "base64")))
            .Handle(new XchangeFile(Convert.ToBase64String("binary-ish"u8.ToArray()), "doc.bin"));

        Assert.Contains("doc.bin", await ftp.ListAsync("outbox"));
        Assert.Equal("binary-ish", await ftp.ReadAsync("outbox/doc.bin"));
    }

    [Fact]
    public async Task The_receiver_lists_in_batches_reads_and_deletes_each_file()
    {
        for (var i = 0; i < 3; i++) await ftp.PutAsync($"inbox/f{i}.txt", $"file {i}");
        var receiver = Adapters.Create<NativeFtpReceiver>(ftp.Settings(("TargetPath", $"{Home}/inbox"), ("BatchSize", "2")));
        await receiver.Initialize();

        var batch = (await receiver.ListFiles()).ToList();
        Assert.Equal(2, batch.Count);
        foreach (var name in batch)
        {
            Assert.StartsWith("file ", (await receiver.GetFile(name)).Data);
            await receiver.DeleteFile(name);
        }
        await receiver.Finalize();

        Assert.Single((await ftp.ListAsync("inbox")).Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public async Task The_receiver_moves_a_received_file_when_told_where()
    {
        await ftp.PutAsync("inbox/move-me.txt", "moving");
        var receiver = Adapters.Create<NativeFtpReceiver>(ftp.Settings(("TargetPath", $"{Home}/inbox"), ("DeleteMovesFileTo", $"{Home}/done")));
        await receiver.Initialize();

        await receiver.DeleteFile("move-me.txt");
        await receiver.Finalize();

        Assert.Contains("move-me.txt", await ftp.ListAsync("done"));
        Assert.DoesNotContain("move-me.txt", await ftp.ListAsync("inbox"));
    }

    [Fact]
    public async Task An_unknown_encryption_mode_is_named_in_the_failure()
    {
        var failure = await Assert.ThrowsAsync<ArgumentException>(() =>
            Adapters.Create<NativeFtpReceiver>(ftp.Settings(("Encryption", "ssl"))).Initialize());
        Assert.Contains("'ssl'", failure.Message);
    }
}

/// <summary>
/// FTPS with TLS negotiated on the FTP connection (AUTH TLS), against a real server that requires
/// it and holds a self-signed certificate — as many partners' servers do.
/// </summary>
public class ExplicitFtpsTests(ExplicitFtpsServer ftps) : IClassFixture<ExplicitFtpsServer>
{
    const string Home = $"/home/{FtpServer.User}";

    [Fact]
    public async Task With_the_certificate_pinned_files_are_uploaded_and_picked_up_over_TLS()
    {
        await Adapters.Create<NativeFtpUploadHandler>(ftps.Settings(("TargetPath", $"{Home}/outbox")))
            .Handle(new XchangeFile("over tls", "secure.txt"));
        Assert.Equal("over tls", await ftps.ReadAsync("outbox/secure.txt"));

        await ftps.PutAsync("inbox/in.txt", "incoming");
        var receiver = Adapters.Create<NativeFtpReceiver>(ftps.Settings(("TargetPath", $"{Home}/inbox")));
        await receiver.Initialize();
        Assert.Contains("in.txt", await receiver.ListFiles());
        Assert.Equal("incoming", (await receiver.GetFile("in.txt")).Data);
        await receiver.DeleteFile("in.txt");
        await receiver.Finalize();
        Assert.DoesNotContain("in.txt", await ftps.ListAsync("inbox"));
    }

    [Fact]
    public async Task The_thumbprint_may_be_written_with_colons_and_in_lower_case()
    {
        var spaced = string.Join(":", ftps.CertificateThumbprint.Chunk(2).Select(c => new string(c))).ToLowerInvariant();
        var receiver = Adapters.Create<NativeFtpReceiver>(ftps.Settings(("CertificateThumbprint", spaced)));

        await receiver.Initialize();
        await receiver.Finalize();
    }

    [Fact]
    public async Task A_self_signed_certificate_that_is_not_pinned_is_refused()
    {
        var receiver = Adapters.Create<NativeFtpReceiver>(ftps.Settings(("CertificateThumbprint", "")));
        await Assert.ThrowsAnyAsync<Exception>(() => receiver.Initialize());
    }

    [Fact]
    public async Task A_certificate_other_than_the_pinned_one_gets_no_credentials()
    {
        var receiver = Adapters.Create<NativeFtpReceiver>(ftps.Settings(("CertificateThumbprint", new string('A', 64))));

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => receiver.Initialize());
        Assert.Contains("Refusing to send credentials", refused.Message);
    }

    [Fact]
    public async Task Plain_FTP_is_refused_by_a_server_that_requires_TLS()
    {
        var receiver = Adapters.Create<NativeFtpReceiver>(ftps.Settings(("Encryption", "none")));
        await Assert.ThrowsAnyAsync<Exception>(() => receiver.Initialize());
    }
}

/// <summary>FTPS that is TLS from the first byte, on its own port, against a real server.</summary>
public class ImplicitFtpsTests(ImplicitFtpsServer ftps) : IClassFixture<ImplicitFtpsServer>
{
    const string Home = $"/home/{FtpServer.User}";

    [Fact]
    public async Task With_the_certificate_pinned_a_file_is_uploaded_and_picked_up_over_TLS()
    {
        await Adapters.Create<NativeFtpUploadHandler>(ftps.Settings(("TargetPath", $"{Home}/outbox")))
            .Handle(new XchangeFile("implicit tls", "implicit.txt"));
        Assert.Equal("implicit tls", await ftps.ReadAsync("outbox/implicit.txt"));

        var receiver = Adapters.Create<NativeFtpReceiver>(ftps.Settings(("TargetPath", $"{Home}/outbox")));
        await receiver.Initialize();
        Assert.Equal("implicit tls", (await receiver.GetFile("implicit.txt")).Data);
        await receiver.Finalize();
    }
}
