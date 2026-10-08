using System.Text;
using Azure.Storage.Blobs;
using SW.Bitween.NativeAdapters.AzureBlobReceiver;
using SW.Bitween.NativeAdapters.AzureBlobUploadHandler;
using SW.PrimitiveTypes;
using Testcontainers.Azurite;
using Xunit;

namespace SW.Bitween.TransportTests;

public sealed class AzuriteFixture : IAsyncLifetime
{
    readonly AzuriteContainer container = new AzuriteBuilder()
        .WithImage("mcr.microsoft.com/azure-storage/azurite:latest")
        // The service version the SDK speaks may be newer than the emulator knows.
        .WithCommand("--skipApiVersionCheck")
        .Build();

    public string ConnectionString => container.GetConnectionString();

    public Task InitializeAsync() => container.StartAsync();

    /// <summary>A private container — no public access — as customers' containers are.</summary>
    public async Task<BlobContainerClient> ContainerAsync()
    {
        var container = new BlobContainerClient(ConnectionString, $"c{Guid.NewGuid():N}"[..20]);
        await container.CreateAsync();
        return container;
    }

    public async Task DisposeAsync() => await container.DisposeAsync();
}

/// <summary>The Azure Blob upload handler and receiver against a real Blob service (Azurite).</summary>
public class AzureBlobTests(AzuriteFixture azurite) : IClassFixture<AzuriteFixture>
{
    Dictionary<string, string> Settings(BlobContainerClient container, params (string Key, string Value)[] more)
    {
        var settings = new Dictionary<string, string>
        {
            ["ConnectionString"] = azurite.ConnectionString,
            ["ContainerName"] = container.Name,
        };
        foreach (var (key, value) in more) settings[key] = value;
        return settings;
    }

    static async Task<List<string>> NamesAsync(BlobContainerClient container)
    {
        var names = new List<string>();
        await foreach (var blob in container.GetBlobsAsync()) names.Add(blob.Name);
        return names.OrderBy(n => n).ToList();
    }

    static async Task<string> TextAsync(BlobContainerClient container, string name) =>
        (await container.GetBlobClient(name).DownloadContentAsync()).Value.Content.ToString();

    static Task PutAsync(BlobContainerClient container, string name, string text) =>
        container.GetBlobClient(name).UploadAsync(new BinaryData(Encoding.UTF8.GetBytes(text)), overwrite: true);

    [Fact]
    public async Task The_upload_handler_writes_a_generated_or_fixed_name()
    {
        var container = await azurite.ContainerAsync();

        var generated = await Adapters.Create<NativeAzureBlobUploadHandler>(Settings(container, ("FileExtension", "csv")))
            .Handle(new XchangeFile("a,b\n1,2", "export.csv"));
        Assert.Matches(@"^\d{14}_[0-9a-f]{32}\.csv$", generated.Data);
        Assert.Equal("a,b\n1,2", await TextAsync(container, generated.Data));

        var fixedSettings = Settings(container, ("FileName", "daily/orders.json"));
        await Adapters.Create<NativeAzureBlobUploadHandler>(fixedSettings).Handle(new XchangeFile("first"));
        await Adapters.Create<NativeAzureBlobUploadHandler>(fixedSettings).Handle(new XchangeFile("second"));
        Assert.Equal("second", await TextAsync(container, "daily/orders.json"));
        Assert.Equal(2, (await NamesAsync(container)).Count);
    }

    [Fact]
    public async Task The_receiver_reads_only_its_folder_and_moves_each_file_once_received()
    {
        var container = await azurite.ContainerAsync();
        await PutAsync(container, "incoming/a.json", "{\"n\":\"a\"}");
        await PutAsync(container, "incoming/sub/b.json", "{\"n\":\"b\"}");
        await PutAsync(container, "incoming-archive/old.json", "a sibling, not inside the folder");

        var receiver = Adapters.Create<NativeAzureBlobReceiver>(Settings(container,
            ("FolderName", "incoming"), ("DeleteMovesFileTo", "processed")));
        await receiver.Initialize();

        var names = (await receiver.ListFiles()).OrderBy(n => n).ToList();
        Assert.Equal(["incoming/a.json", "incoming/sub/b.json"], names);

        foreach (var name in names)
        {
            Assert.StartsWith("{\"n\":", (await receiver.GetFile(name)).Data);
            await receiver.DeleteFile(name);
        }
        // Received twice — a retried run — is not an error.
        await receiver.DeleteFile(names[0]);

        Assert.Equal(["incoming-archive/old.json", "processed/a.json", "processed/sub/b.json"], await NamesAsync(container));
        Assert.Equal("{\"n\":\"b\"}", await TextAsync(container, "processed/sub/b.json"));
    }

    [Fact]
    public async Task The_receiver_lists_in_batches_and_deletes_when_there_is_nowhere_to_move()
    {
        var container = await azurite.ContainerAsync();
        for (var i = 0; i < 3; i++) await PutAsync(container, $"in/{i}.txt", $"file {i}");

        var receiver = Adapters.Create<NativeAzureBlobReceiver>(Settings(container, ("FolderName", "in"), ("BatchSize", "2")));
        await receiver.Initialize();

        var batch = (await receiver.ListFiles()).ToList();
        Assert.Equal(2, batch.Count);
        foreach (var name in batch) await receiver.DeleteFile(name);

        Assert.Single(await NamesAsync(container));
    }
}
