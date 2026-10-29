using System;
using SW.PrimitiveTypes;

namespace SW.Bitween.Domain.Cluster;

/// <summary>
/// A Bitween node, as it last said it was: written by every node on a heartbeat, so the cluster
/// can be seen from any of them. Not audited: it is the nodes describing themselves, not
/// configuration anyone changes.
/// </summary>
public class ClusterNode : BaseEntity<string>
{
    private ClusterNode()
    {
    }

    /// <param name="name">The node's name as leases record it: host and process id.</param>
    public ClusterNode(string name)
    {
        Id = name ?? throw new ArgumentNullException(nameof(name));
    }

    public string Host { get; set; }
    public DateTime StartedOn { get; set; }
    public DateTime LastSeenOn { get; set; }
    public string Version { get; set; }

    /// <summary>Bitween:BusProvidersEnabled on this node: whether it runs data sources.</summary>
    public bool DataSources { get; set; }

    /// <summary>The adapter runtimes this node has, comma-separated: dotnet,python,node.</summary>
    public string Runtimes { get; set; }

    /// <summary>
    /// The resident adapter instances running on this node at its last beat - state, uptime,
    /// restarts, quarantine - as JSON, so any node can show every node's. Null on a node without
    /// resident adapters, and on beats from before this was recorded.
    /// </summary>
    public string ResidentHealth { get; set; }

    /// <summary>
    /// Outbox messages this node published in the hour before its last beat. Published rows are
    /// deleted now rather than kept, so this is where the queue health page counts them from. Null
    /// on beats from nodes from before this was recorded.
    /// </summary>
    public int? OutboxPublishedLastHour { get; set; }
}
