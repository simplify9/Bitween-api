using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using SW.Bitween.NativeAdapters.RebexFtpReceiver;
using SW.Bitween.NativeAdapters.RebexFtpUploadHandler;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.TransportTests;

/// <summary>Runs only with a Rebex license key in REBEX_LICENSE_KEY: the FTP adapters are built on Rebex.</summary>
public sealed class RebexFactAttribute : FactAttribute
{
    public RebexFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("REBEX_LICENSE_KEY")))
            Skip = "Set REBEX_LICENSE_KEY to run the FTP/SFTP adapters, which are built on Rebex.";
    }
}

public sealed class SftpFixture : IAsyncLifetime
{
    public const string User = "partner";
    public const string Password = "partner-pass";

    // An "inbox" and an "outbox" in the user's home, which is all a partner's server usually offers.
    public IContainer Container { get; } = new ContainerBuilder()
        .WithImage("atmoz/sftp:alpine")
        .WithCommand($"{User}:{Password}:1001:100:inbox,outbox,done")
        .WithPortBinding(22, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(22))
        .Build();

    public string Host => Container.Hostname;
    public int Port => Container.GetMappedPublicPort(22);

    public Task InitializeAsync() =>
        string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("REBEX_LICENSE_KEY"))
            ? Task.CompletedTask
            : Container.StartAsync();

    public async Task<string> ListAsync(string folder) =>
        (await Container.ExecAsync(["ls", "-1", $"/home/{User}/{folder}"])).Stdout;

    public async Task PutAsync(string path, string text) =>
        await Container.ExecAsync(["sh", "-c", $"printf '%s' '{text}' > /home/{User}/{path} && chown 1001 /home/{User}/{path}"]);

    public async Task<string> ReadAsync(string path) =>
        (await Container.ExecAsync(["cat", $"/home/{User}/{path}"])).Stdout;

    public async Task DisposeAsync() => await Container.DisposeAsync();
}

/// <summary>The FTP adapters over SFTP against a real SFTP server.</summary>
public class SftpTests(SftpFixture sftp) : IClassFixture<SftpFixture>
{
    static string LicenseKey => Environment.GetEnvironmentVariable("REBEX_LICENSE_KEY")!;

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

    [RebexFact]
    public async Task The_upload_handler_puts_the_file_in_the_target_folder_with_its_prefix()
    {
        var handler = new NativeRebexFtpUploadHandler(LicenseKey);
        handler.InitializeStartupValues(Settings(("TargetPath", "outbox"), ("FileNamePrefix", "acme")));

        await handler.Handle(new XchangeFile("{\"order\":1}", "order-1.json"));

        var name = Assert.Single((await sftp.ListAsync("outbox")).Split('\n', StringSplitOptions.RemoveEmptyEntries),
            n => n.StartsWith("acme_"));
        Assert.Equal("{\"order\":1}", await sftp.ReadAsync($"outbox/{name}"));
    }

    [RebexFact]
    public async Task The_receiver_reads_the_inbox_and_moves_each_file_once_received()
    {
        await sftp.PutAsync("inbox/a.json", "{\"n\":1}");
        var receiver = new NativeRebexFtpReceiver(LicenseKey);
        receiver.InitializeStartupValues(Settings(("TargetPath", "inbox"), ("DeleteMovesFileTo", "done")));
        await receiver.Initialize();

        var file = Assert.Single(await receiver.ListFiles());
        Assert.Equal("{\"n\":1}", (await receiver.GetFile(file)).Data);
        await receiver.DeleteFile(file);
        await receiver.Finalize();

        Assert.DoesNotContain("a.json", await sftp.ListAsync("inbox"));
        Assert.Contains("a.json", await sftp.ListAsync("done"));
    }

    [RebexFact]
    public async Task A_server_whose_host_key_is_not_the_configured_one_gets_no_credentials()
    {
        var receiver = new NativeRebexFtpReceiver(LicenseKey);
        receiver.InitializeStartupValues(Settings(("TargetPath", "inbox"),
            ("HostKeyFingerprint", "SHA256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")));

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => receiver.Initialize());
        Assert.Contains("Refusing to send credentials", refused.Message);
    }
}
