using System.Text;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using SW.PrimitiveTypes;

namespace SW.Bitween.NativeAdapters.AzureBlobReceiver;

public class NativeAzureBlobReceiver : INativeInfolinkReceiver
{
    private AzureBlobReceiverInput _options = new();
    private BlobContainerClient _container = null!;

    public Task Initialize()
    {
        _container = new BlobContainerClient(_options.ConnectionString.Trim(), _options.ContainerName.Trim());
        return Task.CompletedTask;
    }

    public Task Finalize()
    {
        return Task.CompletedTask;
    }

    public async Task<IEnumerable<string>> ListFiles()
    {
        var blobNames = new List<string>();

        // Trailing slash keeps the prefix scoped to the folder itself, so a sibling
        // like "incoming-archive/x.txt" doesn't match a FolderName of "incoming".
        var prefix = string.IsNullOrEmpty(_options.FolderName) ? _options.FolderName : _options.FolderName + "/";

        await foreach (var blob in _container.GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix))
        {
            blobNames.Add(blob.Name);
            if (blobNames.Count >= _options.BatchSize)
                break;
        }

        return blobNames;
    }

    public async Task<XchangeFile> GetFile(string fileId)
    {
        var blobClient = _container.GetBlobClient(fileId);
        var download = await blobClient.DownloadAsync();

        using var memoryStream = new MemoryStream();
        await download.Value.Content.CopyToAsync(memoryStream);
        var bytes = memoryStream.ToArray();

        return (_options.ResponseEncoding ?? "utf8").ToLower() switch
        {
            "base64" => new XchangeFile(Convert.ToBase64String(bytes), fileId),
            "utf8" => new XchangeFile(Encoding.UTF8.GetString(bytes), fileId),
            _ => throw new ArgumentException(
                $"Unknown {nameof(AzureBlobReceiverInput.ResponseEncoding)} '{_options.ResponseEncoding}'")
        };
    }

    public async Task DeleteFile(string fileId)
    {
        var sourceBlob = _container.GetBlobClient(fileId);

        // Retry-safe: if a prior attempt already moved/deleted this file, there's
        // nothing left to do — treat it as already completed, not an error.
        if (!await sourceBlob.ExistsAsync())
            return;

        if (!string.IsNullOrWhiteSpace(_options.DeleteMovesFileTo))
        {
            // Preserve the path relative to FolderName so files with the same name in
            // different subdirectories don't collide at the destination.
            var relativePath = !string.IsNullOrEmpty(_options.FolderName) && fileId.StartsWith(_options.FolderName + "/")
                ? fileId[(_options.FolderName.Length + 1)..]
                : fileId;
            var targetName = $"{_options.DeleteMovesFileTo}/{relativePath}";

            // Server-side copy: Azure moves the blob internally, so no bytes are
            // downloaded or re-uploaded through this process.
            var targetBlob = _container.GetBlobClient(targetName);
            await targetBlob.SyncCopyFromUriAsync(sourceBlob.Uri);
        }

        await sourceBlob.DeleteAsync();
    }

    public string Name => "NativeAzureBlobReceiver";

    public void InitializeStartupValues(IDictionary<string, string> settings)
    {
        _options = settings.ConvertTo<AzureBlobReceiverInput>();
    }

    public Type StartupValuesType => typeof(AzureBlobReceiverInput);
}
