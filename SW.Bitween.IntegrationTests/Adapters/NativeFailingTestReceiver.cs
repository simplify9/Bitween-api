using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SW.Bitween.NativeAdapters;
using SW.PrimitiveTypes;

namespace SW.Bitween.IntegrationTests.Adapters;

/// <summary>Always fails to list files — for exercising ReceivingJob's failure path.</summary>
public class NativeFailingTestReceiver : INativeInfolinkReceiver
{
    public string Name => nameof(NativeFailingTestReceiver);
    public Type StartupValuesType => typeof(object);

    public void InitializeStartupValues(IDictionary<string, string> settings) { }

    public Task Initialize() => Task.CompletedTask;

    public Task<IEnumerable<string>> ListFiles() => throw new InvalidOperationException("Connection refused");

    public Task<XchangeFile> GetFile(string fileId) => throw new NotSupportedException();

    public Task DeleteFile(string fileId) => Task.CompletedTask;

    public Task Finalize() => Task.CompletedTask;
}
