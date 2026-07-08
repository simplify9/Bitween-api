using System.Text;
using Amazon.S3;
using SW.CloudFiles.S3;
using SW.PrimitiveTypes;

namespace SW.Bitween.NativeAdapters.S3Receiver;

public class NativeS3Receiver : INativeInfolinkReceiver, IDisposable
{
    private S3ReceiverInput _options = new();
    private CloudFilesService? _cloudFiles;
    private AmazonS3Client? _s3Client;

    public Task Initialize()
    {
        var options = new CloudFilesOptions
        {
            AccessKeyId = _options.AccessKeyId,
            SecretAccessKey = _options.SecretAccessKey,
            ServiceUrl = _options.ServiceUrl,
            BucketName = _options.BucketName,
        };

        _cloudFiles = new CloudFilesService(options);
        _s3Client = options.CreateClient();

        return Task.CompletedTask;
    }

    public Task Finalize()
    {
        _cloudFiles?.Dispose();
        _s3Client?.Dispose();
        return Task.CompletedTask;
    }

    // Safety net: the DI container disposes scoped instances when the job's scope ends,
    // even if Initialize/ListFiles/GetFile/DeleteFile threw and Finalize was never reached.
    public void Dispose()
    {
        _cloudFiles?.Dispose();
        _s3Client?.Dispose();
    }

    public async Task<IEnumerable<string>> ListFiles()
    {
        var files = await _cloudFiles.ListAsync(_options.FolderName ?? string.Empty);

        return files
            .Where(f => !f.Key.EndsWith("/"))
            .Select(f => f.Key)
            .Take(_options.BatchSize)
            .ToList();
    }

    public async Task<XchangeFile> GetFile(string fileId)
    {
        await using var stream = await _cloudFiles.OpenReadAsync(fileId);
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream);
        var bytes = memoryStream.ToArray();

        return (_options.ResponseEncoding ?? "utf8").ToLower() switch
        {
            "base64" => new XchangeFile(Convert.ToBase64String(bytes), fileId),
            "utf8" => new XchangeFile(Encoding.UTF8.GetString(bytes), fileId),
            _ => throw new ArgumentException(
                $"Unknown {nameof(S3ReceiverInput.ResponseEncoding)} '{_options.ResponseEncoding}'")
        };
    }

    public async Task DeleteFile(string fileId)
    {
        if (!string.IsNullOrWhiteSpace(_options.DeleteMovesFileTo))
        {
            // Preserve the path relative to FolderName so files with the same name in
            // different subdirectories don't collide at the destination.
            var relativePath = !string.IsNullOrEmpty(_options.FolderName) && fileId.StartsWith(_options.FolderName + "/")
                ? fileId[(_options.FolderName.Length + 1)..]
                : fileId;

            // Server-side copy: S3 moves the object internally, so no bytes are
            // downloaded or re-uploaded through this process.
            var targetKey = $"{_options.DeleteMovesFileTo}/{relativePath}";
            await _s3Client!.CopyObjectAsync(_options.BucketName, fileId, _options.BucketName, targetKey);
        }

        await _cloudFiles.DeleteAsync(fileId);
    }

    public string Name => "NativeS3Receiver";

    public void InitializeStartupValues(IDictionary<string, string> settings)
    {
        _options = settings.ConvertTo<S3ReceiverInput>();
    }

    public Type StartupValuesType => typeof(S3ReceiverInput);
}
