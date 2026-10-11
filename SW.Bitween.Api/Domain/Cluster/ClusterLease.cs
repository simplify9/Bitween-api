using SW.PrimitiveTypes;
using System;

namespace SW.Bitween.Domain.Cluster;

/// <summary>
/// The fencing token for one exclusively-owned resource.
///
/// This is not the lock - the lock lives on the bus. This is the monotonic counter the bus cannot
/// provide, so that a node which was paused while ownership moved can discover it lost.
/// </summary>
public class ClusterLease : BaseEntity<string>
{
    private ClusterLease()
    {
    }

    public ClusterLease(string resource, string ownerNode)
    {
        Id = resource ?? throw new ArgumentNullException(nameof(resource));
        Term = 1;
        OwnerNode = ownerNode;
        AcquiredOn = DateTime.UtcNow;
    }

    /// <summary>Increments on every acquisition. Never reused, never decreases.</summary>
    public long Term { get; private set; }

    public string OwnerNode { get; private set; }
    public DateTime AcquiredOn { get; private set; }

    public long Claim(string ownerNode)
    {
        Term++;
        OwnerNode = ownerNode;
        AcquiredOn = DateTime.UtcNow;
        return Term;
    }
}
