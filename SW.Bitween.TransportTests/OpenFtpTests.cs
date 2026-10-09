using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using SW.Bitween.NativeAdapters.FtpReceiver;
using SW.Bitween.NativeAdapters.FtpUploadHandler;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.TransportTests;

/// <summary>
/// The FTP adapters built on open-source clients — SSH.NET for SFTP, FluentFTP for FTP — against
/// real servers. They need no license, so unlike the Rebex adapters' tests these always run, and
/// they check the same behaviour: the same settings must do the same thing on either adapter.
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
            ["Protocol"] = "sftp",
        };
        foreach (var (key, value) in more) settings[key] = value;
        return settings;
    }

    [Fact]
    public async Task The_upload_handler_puts_the_file_in_the_target_folder_with_its_prefix()
    {
        await Adapters.Create<NativeFtpUploadHandler>(Settings(("TargetPath", "outbox"), ("FileNamePrefix", "acme")))
            .Handle(new XchangeFile("{\"order\":1}", "order-1.json"));

        Assert.Contains("acme_order-1.json", await sftp.ListAsync("outbox"));
        Assert.Equal("{\"order\":1}", await sftp.ReadAsync("outbox/acme_order-1.json"));
    }

    [Fact]
    public async Task A_file_name_from_the_message_cannot_climb_out_of_the_target_folder()
    {
        await Adapters.Create<NativeFtpUploadHandler>(Settings(("TargetPath", "outbox")))
            .Handle(new XchangeFile("climbing", "../../inbox/escaped.txt"));

        Assert.Contains("escaped.txt", await sftp.ListAsync("outbox"));
        Assert.DoesNotContain("escaped.txt", await sftp.ListAsync("inbox"));
    }

    [Fact]
    public async Task The_receiver_reads_the_folder_and_moves_each_file_once_received()
    {
        var name = $"r{Guid.NewGuid():N}.json"[..12];
        await sftp.PutAsync($"inbox/{name}", "{\"n\":1}");
        var receiver = Adapters.Create<NativeFtpReceiver>(Settings(("TargetPath", "inbox"), ("DeleteMovesFileTo", "../done")));
        await receiver.Initialize();

        var files = (await receiver.ListFiles()).ToList();
        Assert.Contains(name, files);
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
        var receiver = Adapters.Create<NativeFtpReceiver>(Settings(("TargetPath", "inbox"), ("MinimumFileAgeSeconds", "300")));
        await receiver.Initialize();

        Assert.DoesNotContain(name, await receiver.ListFiles());
        await receiver.Finalize();
    }

    [Fact]
    public async Task A_private_key_signs_in_and_the_pinned_host_key_is_accepted()
    {
        var receiver = Adapters.Create<NativeFtpReceiver>(Settings(("Protocol", "sftpssh"), ("Password", ""),
            // Pasted onto one line, as a settings form often leaves it.
            ("PrivateKey", sftp.PrivateKey.ReplaceLineEndings(" ")),
            ("HostKeyFingerprint", sftp.HostKeyFingerprint), ("TargetPath", "inbox")));

        await receiver.Initialize();
        await receiver.ListFiles();
        await receiver.Finalize();
    }

    [Fact]
    public async Task An_ed25519_key_in_the_OpenSSH_format_signs_in()
    {
        var receiver = Adapters.Create<NativeFtpReceiver>(Settings(("Protocol", "sftpssh"), ("Password", ""),
            ("PrivateKey", sftp.OpenSshPrivateKey), ("TargetPath", "inbox")));

        await receiver.Initialize();
        await receiver.ListFiles();
        await receiver.Finalize();
    }

    [Fact]
    public async Task A_server_whose_host_key_is_not_the_configured_one_gets_no_credentials()
    {
        var receiver = Adapters.Create<NativeFtpReceiver>(Settings(("TargetPath", "inbox"),
            ("HostKeyFingerprint", "SHA256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")));

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => receiver.Initialize());
        Assert.Contains("Refusing to send credentials", refused.Message);
    }

    [Fact]
    public async Task A_wrong_password_and_a_missing_one_are_refused()
    {
        await Assert.ThrowsAnyAsync<Exception>(() =>
            Adapters.Create<NativeFtpReceiver>(Settings(("Password", "wrong"))).Initialize());

        var missing = await Assert.ThrowsAsync<ArgumentException>(() =>
            Adapters.Create<NativeFtpReceiver>(Settings(("Password", ""))).Initialize());
        Assert.Contains("Password is required", missing.Message);
    }
}

public sealed class FtpFixture : IAsyncLifetime
{
    public const string User = "partner";
    public const string Password = "partner-pass";
    const int FirstPassivePort = 21100, LastPassivePort = 21110;

    // Passive mode tells the client which port to open next, so those ports are published as they
    // are, not remapped.
    public IContainer Container { get; } = new ContainerBuilder()
        .WithImage("delfer/alpine-ftp-server:latest")
        .WithEnvironment("USERS", $"{User}|{Password}|/home/{User}|1001")
        .WithEnvironment("ADDRESS", "127.0.0.1")
        .WithEnvironment("MIN_PORT", FirstPassivePort.ToString())
        .WithEnvironment("MAX_PORT", LastPassivePort.ToString())
        .WithPortBinding(21, true)
        .WithPortBinding(FirstPassivePort, FirstPassivePort)
        .WithPortBinding(FirstPassivePort + 1, FirstPassivePort + 1)
        .WithPortBinding(FirstPassivePort + 2, FirstPassivePort + 2)
        .WithPortBinding(FirstPassivePort + 3, FirstPassivePort + 3)
        .WithPortBinding(FirstPassivePort + 4, FirstPassivePort + 4)
        .WithPortBinding(FirstPassivePort + 5, FirstPassivePort + 5)
        .WithPortBinding(FirstPassivePort + 6, FirstPassivePort + 6)
        .WithPortBinding(FirstPassivePort + 7, FirstPassivePort + 7)
        .WithPortBinding(FirstPassivePort + 8, FirstPassivePort + 8)
        .WithPortBinding(FirstPassivePort + 9, FirstPassivePort + 9)
        .WithPortBinding(LastPassivePort, LastPassivePort)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(21))
        .Build();

    public string Host => "127.0.0.1";
    public int Port => Container.GetMappedPublicPort(21);

    public async Task InitializeAsync()
    {
        await Container.StartAsync();
        await Container.ExecAsync(["sh", "-c", $"mkdir -p /home/{User}/inbox /home/{User}/outbox /home/{User}/done && chown -R 1001 /home/{User}"]);
    }

    public async Task<string> ListAsync(string folder) =>
        (await Container.ExecAsync(["ls", "-1", $"/home/{User}/{folder}"])).Stdout;

    public async Task PutAsync(string path, string text) =>
        await Container.ExecAsync(["sh", "-c", $"printf '%s' '{text}' > /home/{User}/{path} && chown 1001 /home/{User}/{path}"]);

    public async Task<string> ReadAsync(string path) =>
        (await Container.ExecAsync(["cat", $"/home/{User}/{path}"])).Stdout;

    public async Task DisposeAsync() => await Container.DisposeAsync();
}

public class OpenFtpTests(FtpFixture ftp) : IClassFixture<FtpFixture>
{
    Dictionary<string, string> Settings(params (string Key, string Value)[] more)
    {
        var settings = new Dictionary<string, string>
        {
            ["Host"] = ftp.Host,
            ["Port"] = ftp.Port.ToString(),
            ["Username"] = FtpFixture.User,
            ["Password"] = FtpFixture.Password,
            ["Protocol"] = "ftp",
        };
        foreach (var (key, value) in more) settings[key] = value;
        return settings;
    }

    [Fact]
    public async Task The_upload_handler_puts_the_file_in_the_target_folder_decoding_base64()
    {
        await Adapters.Create<NativeFtpUploadHandler>(Settings(("TargetPath", $"/home/{FtpFixture.User}/outbox"),
                ("DataEncoding", "base64")))
            .Handle(new XchangeFile(Convert.ToBase64String("binary-ish"u8.ToArray()), "doc.bin"));

        Assert.Contains("doc.bin", await ftp.ListAsync("outbox"));
        Assert.Equal("binary-ish", await ftp.ReadAsync("outbox/doc.bin"));
    }

    [Fact]
    public async Task The_receiver_lists_in_batches_reads_and_deletes_each_file()
    {
        for (var i = 0; i < 3; i++) await ftp.PutAsync($"inbox/f{i}.txt", $"file {i}");
        var receiver = Adapters.Create<NativeFtpReceiver>(Settings(("TargetPath", $"/home/{FtpFixture.User}/inbox"),
            ("BatchSize", "2")));
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
        var receiver = Adapters.Create<NativeFtpReceiver>(Settings(("TargetPath", $"/home/{FtpFixture.User}/inbox"),
            ("DeleteMovesFileTo", $"/home/{FtpFixture.User}/done")));
        await receiver.Initialize();

        await receiver.DeleteFile("move-me.txt");
        await receiver.Finalize();

        Assert.Contains("move-me.txt", await ftp.ListAsync("done"));
        Assert.DoesNotContain("move-me.txt", await ftp.ListAsync("inbox"));
    }
}
