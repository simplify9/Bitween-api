using System.Text;
using Azure.Storage.Blobs;
using SW.PrimitiveTypes;

namespace SW.Bitween.NativeAdapters.AzureBlobUploadHandler;

public class NativeAzureBlobUploadHandler : INativeInfolinkHandler
{
    private AzureBlobUploadHandlerInput _options = new();

    public async Task<XchangeFile> Handle(XchangeFile xchangeFile)
    {
        var container = new BlobContainerClient(_options.ConnectionString.Trim(), _options.ContainerName.Trim());

        var blobName = _options.FileName;
        if (string.IsNullOrWhiteSpace(blobName))
        {
            var extension = _options.FileExtension?.TrimStart('.');
            var name = $"{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}";
            blobName = string.IsNullOrEmpty(extension) ? name : $"{name}.{extension}";
        }

        var blobClient = container.GetBlobClient(blobName);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xchangeFile.Data));
        await blobClient.UploadAsync(stream, overwrite: true);

        return new XchangeFile(blobName, xchangeFile.Filename);
    }

    public string Name => "NativeAzureBlobUploadHandler";

    public void InitializeStartupValues(IDictionary<string, string> settings)
    {
        _options = settings.ConvertTo<AzureBlobUploadHandlerInput>();
    }

    public Type StartupValuesType => typeof(AzureBlobUploadHandlerInput);
}
