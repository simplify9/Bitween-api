using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SW.Bitween.NativeAdapters;
using SW.PrimitiveTypes;

namespace SW.Bitween.IntegrationTests.Adapters;

/// <summary>Always finds nothing — for exercising ReceivingJob's no-new-data path.</summary>
public class NativeEmptyTestReceiver : INativeInfolinkReceiver
{
    public string Name => nameof(NativeEmptyTestReceiver);
    public Type StartupValuesType => typeof(object);

    public void InitializeStartupValues(IDictionary<string, string> settings) { }

    public Task Initialize() => Task.CompletedTask;

    public Task<IEnumerable<string>> ListFiles() => Task.FromResult<IEnumerable<string>>(Array.Empty<string>());

    public Task<XchangeFile> GetFile(string fileId) => throw new NotSupportedException();

    public Task DeleteFile(string fileId) => Task.CompletedTask;

    public Task Finalize() => Task.CompletedTask;
}
