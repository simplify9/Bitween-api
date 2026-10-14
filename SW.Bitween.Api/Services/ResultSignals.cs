using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using SW.Bitween.Services.Cluster;

namespace SW.Bitween;

/// <summary>
/// The bell a synchronous gateway call waits on: rung by the worker that saved its exchange's
/// result, on this node directly, or through <see cref="ResultReadyListener"/> when the worker ran
/// on another node.
/// </summary>
/// <remarks>
/// <para>
/// The call used to find out by asking the database: a first check after a second, then 1, 2, 3, 5
/// and 8 seconds apart. A 200 ms exchange was answered at one second, a 1.2 s one at two. Now the
/// worker says so the moment the result is saved, and the database is only asked as a backstop
/// (see <see cref="ResultWait"/>), for a signal lost to a restart or a worker on a version that
/// doesn't send one.
/// </para>
/// <para>
/// The call registers before its exchange is published, so a result can't arrive unannounced. An
/// entry goes when the call stops waiting, or at the latest when its wait would have ended, so an
/// abandoned call leaves nothing behind.
/// </para>
/// </remarks>
public sealed class ResultSignals
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<XchangeResultReady>> waiting = new();

    /// <summary>
    /// The name a worker addresses this bell by: the node, and this instance of the application on
    /// it, so a second application in the same process (a test's second node) has its own.
    /// </summary>
    public string Node { get; } = $"{NodeHeartbeat.NodeName}/{Guid.NewGuid():N}";

    /// <summary>How many calls are waiting on this node.</summary>
    public int Count => waiting.Count;

    /// <summary>
    /// Starts waiting for an exchange's result. Dispose the returned wait when done with it; it ends
    /// by itself, cancelled, after <paramref name="expiry"/>.
    /// </summary>
    public Wait Register(string xchangeId, TimeSpan expiry)
    {
        var bell = new TaskCompletionSource<XchangeResultReady>(TaskCreationOptions.RunContinuationsAsynchronously);
        // A second call for the same exchange (none today) shares the first one's bell, and leaves its
        // removal to it.
        var registered = waiting.GetOrAdd(xchangeId, bell);
        return new Wait(this, xchangeId, registered, owns: ReferenceEquals(registered, bell), expiry);
    }

    /// <summary>Rings the bell of a call waiting for this result on this node. False when none is.</summary>
    public bool Complete(XchangeResultReady ready) =>
        ready?.Id != null && waiting.TryRemove(ready.Id, out var bell) && bell.TrySetResult(ready);

    private void Forget(string xchangeId, TaskCompletionSource<XchangeResultReady> bell)
    {
        waiting.TryRemove(new(xchangeId, bell));
        bell.TrySetCanceled();
    }

    /// <summary>One call's wait for one result.</summary>
    public sealed class Wait : IDisposable
    {
        private readonly ResultSignals signals;
        private readonly string xchangeId;
        private readonly TaskCompletionSource<XchangeResultReady> bell;
        private readonly bool owns;
        private readonly CancellationTokenSource expiry;

        internal Wait(ResultSignals signals, string xchangeId, TaskCompletionSource<XchangeResultReady> bell, bool owns,
            TimeSpan after)
        {
            (this.signals, this.xchangeId, this.bell, this.owns) = (signals, xchangeId, bell, owns);
            if (!owns) return;
            expiry = new CancellationTokenSource(after);
            expiry.Token.Register(() => signals.Forget(xchangeId, bell));
        }

        /// <summary>Completes with the result when it is rung; cancelled when the wait expires or is disposed.</summary>
        public Task<XchangeResultReady> Result => bell.Task;

        public void Dispose()
        {
            if (!owns) return;
            expiry.Dispose();
            signals.Forget(xchangeId, bell);
        }
    }
}
