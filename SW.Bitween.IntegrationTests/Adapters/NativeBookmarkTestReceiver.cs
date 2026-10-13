using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SW.Bitween.NativeAdapters;
using SW.PrimitiveTypes;

namespace SW.Bitween.IntegrationTests.Adapters;

/// <summary>
/// Numbered items, from its Items setting, taking those after the bookmark it is handed; FailOn names
/// one that can't be read. Its bookmark is the highest number taken.
/// </summary>
public class NativeBookmarkTestReceiver : INativeInfolinkReceiver, IKeepsBookmark
{
    int[] items = [];
    int? failOn;
    int? given;
    int taken;

    public string Name => nameof(NativeBookmarkTestReceiver);
    public Type StartupValuesType => typeof(object);

    /// <summary>What the last run was handed, for the tests.</summary>
    public static string LastGiven { get; private set; }

    public void InitializeStartupValues(IDictionary<string, string> settings)
    {
        settings ??= new Dictionary<string, string>();
        items = (settings.TryGetValue("Items", out var list) ? list : "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
        failOn = settings.TryGetValue("FailOn", out var fail) && int.TryParse(fail, out var f) ? f : null;
        LastGiven = settings.TryGetValue("__bookmark__", out var bookmark) ? bookmark : null;
        given = int.TryParse(LastGiven, out var g) ? g : null;
        taken = given ?? 0;
    }

    public Task Initialize() => Task.CompletedTask;

    public Task<IEnumerable<string>> ListFiles() =>
        Task.FromResult<IEnumerable<string>>(items.Where(i => given == null || i > given).Select(i => i.ToString()).ToList());

    public Task<XchangeFile> GetFile(string fileId)
    {
        if (int.Parse(fileId) == failOn) throw new InvalidOperationException($"item {fileId} can't be read");
        taken = Math.Max(taken, int.Parse(fileId));
        return Task.FromResult(new XchangeFile($"{{\"item\":{fileId}}}"));
    }

    public Task DeleteFile(string fileId) => Task.CompletedTask;

    public Task Finalize() => Task.CompletedTask;

    public Task<string> Bookmark() => Task.FromResult(taken == 0 ? null : taken.ToString());
}
