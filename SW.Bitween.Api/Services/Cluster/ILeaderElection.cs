using System;
using System.Threading;
using System.Threading.Tasks;

namespace SW.Bitween.Services.Cluster;

/// <summary>
/// Exclusive ownership of one named resource across the cluster.
///
/// Deliberately an abstraction over ONE implementation. The mechanism today is a RabbitMQ
/// exclusive queue, because the internal bus is RabbitMQ everywhere Bitween runs - but the day
/// that stops being true, the supervisor should not have to change. What must not happen is two
/// implementations maintained at once; the interface exists so the second can replace the first,
/// not sit beside it.
/// </summary>
public interface ILeaderElection
{
    /// <summary>
    /// Returns a held lease, or null when another node already owns the resource. Never blocks
    /// waiting for it: a supervisor that queued behind a lock would stop reconciling everything
    /// else it owns.
    /// </summary>
    Task<IResourceLease> TryAcquireAsync(string resource, CancellationToken cancellationToken = default);
}

/// <summary>
/// Ownership of a resource, for as long as it is held. Disposing releases it.
/// </summary>
public interface IResourceLease : IAsyncDisposable
{
    string Resource { get; }

    /// <summary>
    /// The fencing token: monotonic, and issued by the database rather than by the lock.
    ///
    /// The lock alone is not enough. A node can be paused long enough - a stop-the-world GC, a
    /// network partition that heals - for its exclusive queue to be released and claimed by
    /// another node while it still believes it holds ownership. RabbitMQ has no monotonic counter
    /// to detect that, so the database supplies one: acquiring bumps the term, and a holder whose
    /// term is no longer the current one has been superseded and must stop immediately.
    /// </summary>
    long Term { get; }

    /// <summary>False once the connection carrying the lock has gone, or after release.</summary>
    bool IsHeld { get; }

    /// <summary>
    /// Confirms this lease is still the current one, by comparing its term against the database.
    /// Cheap, and the supervisor calls it before acting on anything it believes it owns.
    /// </summary>
    Task<bool> ValidateAsync(CancellationToken cancellationToken = default);
}
