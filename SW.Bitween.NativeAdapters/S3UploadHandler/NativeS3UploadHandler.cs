using SW.CloudFiles.S3;
using SW.PrimitiveTypes;

namespace SW.Bitween.NativeAdapters.S3UploadHandler;

public class NativeS3UploadHandler : INativeInfolinkHandler
{
    private S3UploadHandlerInput _options = new();

    public async Task<XchangeFile> Handle(XchangeFile xchangeFile)
    {
        using var cloudFiles = new CloudFilesService(new CloudFilesOptions
        {
            AccessKeyId = _options.AccessKeyId,
            SecretAccessKey = _options.SecretAccessKey,
            ServiceUrl = _options.ServiceUrl,
            BucketName = _options.BucketName,
        });

        var key = _options.FileName;
        if (string.IsNullOrWhiteSpace(key))
        {
            var extension = _options.FileExtension?.TrimStart('.');
            var name = $"{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}";
            key = string.IsNullOrEmpty(extension) ? name : $"{name}.{extension}";

            if (!string.IsNullOrWhiteSpace(_options.FolderName))
                key = $"{_options.FolderName}/{key}";
        }

        await cloudFiles.WriteTextAsync(xchangeFile.Data, new WriteFileSettings
        {
            Key = key,
            ContentType = _options.ContentType,
        });

        return new XchangeFile(key, xchangeFile.Filename);
    }

    public string Name => "NativeS3UploadHandler";

    public void InitializeStartupValues(IDictionary<string, string> settings)
    {
        _options = settings.ConvertTo<S3UploadHandlerInput>();
    }

    public Type StartupValuesType => typeof(S3UploadHandlerInput);
}
