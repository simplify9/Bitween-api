using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SW.Bitween.NativeAdapters;
using SW.PrimitiveTypes;
using System.Collections.Concurrent;
using System.Threading;

namespace SW.Bitween.IntegrationTests.Adapters;

/// <summary>Lists three items and cannot read the middle one — for the "one bad file" path.</summary>
public class NativePartlyFailingTestReceiver : INativeInfolinkReceiver
{
    public static readonly ConcurrentBag<string> Deleted = new();
    public static int Finalized;

    public string Name => nameof(NativePartlyFailingTestReceiver);
    public Type StartupValuesType => typeof(object);

    public void InitializeStartupValues(IDictionary<string, string> settings) { }

    public Task Initialize() => Task.CompletedTask;

    public Task<IEnumerable<string>> ListFiles() =>
        Task.FromResult<IEnumerable<string>>(["first.json", "corrupt.json", "third.json"]);

    public Task<XchangeFile> GetFile(string fileId) => fileId == "corrupt.json"
        ? throw new InvalidOperationException("unreadable file")
        : Task.FromResult(new XchangeFile("{}", fileId));

    public Task DeleteFile(string fileId)
    {
        Deleted.Add(fileId);
        return Task.CompletedTask;
    }

    public Task Finalize()
    {
        Interlocked.Increment(ref Finalized);
        return Task.CompletedTask;
    }
}
