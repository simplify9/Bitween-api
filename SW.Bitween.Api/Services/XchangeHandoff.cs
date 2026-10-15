using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SW.Bitween.Domain;
using SW.Bitween.Model;

namespace SW.Bitween;

/// <summary>
/// What one unit of work hands to its save, and to the publish straight after it, without the
/// database ever carrying it: the exchange files being written to storage, which the save waits
/// for, and what an exchange's created message carries beside its id.
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
/// One per <see cref="BitweenDbContext"/>, so per scope, like the change tracker it goes with. A
/// message the outbox dispatcher publishes later comes from another scope and carries the id
/// alone; whoever reads it falls back to storage, which always has the files.
/// </para>
/// </remarks>
public sealed class XchangeHandoff
{
    private readonly List<(string XchangeId, XchangeFileType Type, Task Write)> writes = [];
    private readonly Dictionary<string, string> inputs = new();
    private readonly Dictionary<string, string> awaitedOn = new();
    private readonly Dictionary<OutboxMessage, string> created = new(ReferenceEqualityComparer.Instance);

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

    /// <summary>
    /// Waits for the writes of one exchange's <paramref name="types"/> files without throwing, forgets
    /// them, and says why each that failed did. The other writes are left for the save to wait for:
    /// for a result whose delivery has happened, where a file that couldn't be stored is recorded as
    /// missing rather than failing the save.
    /// </summary>
    public async Task<IReadOnlyDictionary<XchangeFileType, Exception>> SettleAsync(string xchangeId,
        params XchangeFileType[] types)
    {
        var mine = writes.Where(w => w.XchangeId == xchangeId && types.Contains(w.Type)).ToArray();
        writes.RemoveAll(w => mine.Contains(w));
        var failed = new Dictionary<XchangeFileType, Exception>();
        foreach (var (_, type, write) in mine)
        {
            try
            {
                await write;
            }
            catch (Exception ex)
            {
                failed[type] = ex;
            }
        }

        return failed;
    }

    /// <summary>The input of a new exchange, as written to storage, for its created message to carry.</summary>
    public void Carry(string xchangeId, string input) => inputs[xchangeId] = input;

    /// <summary>A synchronous call on <paramref name="node"/> waits for this exchange's result.</summary>
    public void AwaitedOn(string xchangeId, string node) => awaitedOn[xchangeId] = node;

    /// <summary>
    /// Notes which exchange an outbox message announces, when it is an exchange's created message;
    /// the publish then adds what this unit of work holds for it. Returns the message.
    /// </summary>
    public OutboxMessage Announcing(object domainEvent, OutboxMessage message)
    {
        if (domainEvent is XchangeCreatedEvent createdEvent) created[message] = createdEvent.Id;
        return message;
    }

    /// <summary>
    /// The body to publish a message with straight after the commit, when it is a created message
    /// this unit of work has something to add to; null to publish the stored body, the id alone.
    /// </summary>
    public string BodyToPublish(OutboxMessage message)
    {
        if (!created.Remove(message, out var xchangeId)) return null;
        inputs.Remove(xchangeId, out var input);
        awaitedOn.Remove(xchangeId, out var node);
        if (input == null && node == null) return null;

        return JsonConvert.SerializeObject(new XchangeMessage { Id = xchangeId, Input = input, AwaitedOn = node });
    }
}
