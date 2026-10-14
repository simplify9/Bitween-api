using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SW.Bitween.Model;

namespace SW.Bitween;

/// <summary>
/// What one unit of work hands to its save without the database ever carrying it: the exchange
/// files being written to storage, which the save waits for.
/// </summary>
/// <remarks>
/// <para>
/// A file used to be written and waited for before anything else happened: the input before the
/// exchange was even added, the output before the handler ran, the response before the result was
/// prepared. Now the write starts and the work goes on beside it, and
/// <see cref="BitweenDbContext.SaveChangesAsync"/> waits for every write before it saves anything.
/// So a saved exchange or result still always has its files: nothing commits until the storage
/// has them, and a write that fails fails the save.
/// </para>
/// <para>
/// One per <see cref="BitweenDbContext"/>, so per scope, like the change tracker it goes with.
/// </para>
/// </remarks>
public sealed class XchangeHandoff
{
    private readonly List<(string XchangeId, XchangeFileType Type, Task Write)> writes = [];

    /// <summary>A file write that has started; the next save waits for it.</summary>
    public void Writing(string xchangeId, XchangeFileType type, Task write) => writes.Add((xchangeId, type, write));

    /// <summary>
    /// Waits for every write started so far, and throws the first that failed. Writes that went
    /// through are forgotten; after a failure they are all kept, for <see cref="SettleAsync"/>.
    /// </summary>
    public async Task WrittenAsync()
    {
        if (writes.Count == 0) return;
        var pending = writes.Select(w => w.Write).ToArray();
        await Task.WhenAll(pending);
        writes.RemoveAll(w => pending.Contains(w.Write));
    }

    /// <summary>
    /// Waits for every write started so far without throwing, forgets them, and says which failed:
    /// for a failure path that saves what it can and must not name a file that isn't there.
    /// </summary>
    public async Task<IReadOnlyCollection<(string XchangeId, XchangeFileType Type)>> SettleAsync()
    {
        var pending = writes.ToArray();
        writes.Clear();
        var failed = new List<(string, XchangeFileType)>();
        foreach (var (xchangeId, type, write) in pending)
        {
            try
            {
                await write;
            }
            catch
            {
                failed.Add((xchangeId, type));
            }
        }

        return failed;
    }

}
