using System.Text;
using SW.CloudFiles.S3;
using SW.PrimitiveTypes;

namespace SW.Bitween.NativeAdapters.S3Receiver;

public class NativeS3Receiver : INativeInfolinkReceiver
{
    private S3ReceiverInput _options = new();
    private CloudFilesService _cloudFiles = null!;

    public Task Initialize()
    {
        _cloudFiles = new CloudFilesService(new CloudFilesOptions
        {
            AccessKeyId = _options.AccessKeyId,
            SecretAccessKey = _options.SecretAccessKey,
            ServiceUrl = _options.ServiceUrl,
            BucketName = _options.BucketName,
        });

        return Task.CompletedTask;
    }

    public Task Finalize()
    {
        _cloudFiles.Dispose();
        return Task.CompletedTask;
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

        return _options.ResponseEncoding.ToLower() switch
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
            await using var sourceStream = await _cloudFiles.OpenReadAsync(fileId);
            using var buffer = new MemoryStream();
            await sourceStream.CopyToAsync(buffer);
            buffer.Position = 0;

            var targetKey = $"{_options.DeleteMovesFileTo}/{Path.GetFileName(fileId)}";
            await _cloudFiles.WriteAsync(buffer, new WriteFileSettings { Key = targetKey });
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
