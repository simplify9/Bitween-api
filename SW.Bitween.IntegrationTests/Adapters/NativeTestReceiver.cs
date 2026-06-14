using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SW.Bitween.NativeAdapters;
using SW.PrimitiveTypes;

namespace SW.Bitween.IntegrationTests.Adapters;

public class NativeTestReceiver : INativeInfolinkReceiver
{
    public string Name => nameof(NativeTestReceiver);
    public Type StartupValuesType => typeof(object);

    public void InitializeStartupValues(IDictionary<string, string> settings) { }

    public Task Initialize() => Task.CompletedTask;

    public Task<IEnumerable<string>> ListFiles() =>
        Task.FromResult<IEnumerable<string>>(new[] { "test-file-1", "test-file-2" });

    public Task<XchangeFile> GetFile(string fileId) =>
        Task.FromResult(new XchangeFile($"{{\"fileId\":\"{fileId}\"}}"));

    public Task DeleteFile(string fileId) => Task.CompletedTask;

    public Task Finalize() => Task.CompletedTask;
}
