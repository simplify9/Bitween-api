using SW.PrimitiveTypes;
using System;

namespace SW.Bitween.Domain.DataSources;

/// <summary>
/// One inbound message we have already persisted, remembered by its dedupe key.
///
/// At-least-once delivery is not an edge case here - it is what persist-then-acknowledge buys.
/// A crash between committing the Xchange and acknowledging the broker redelivers by design, so
/// duplicates are normal and something has to recognise them.
///
/// The KEY IS THE PRIMARY KEY, deliberately. Deduplication is decided by an insert failing, not
/// by a lookup succeeding: "check whether it exists, then insert" is check-then-act and races, so
/// two concurrent deliveries of one key would both miss and both persist. The database is the
/// arbiter.
/// </summary>
public class InboundMessage : BaseEntity<string>
{
    private InboundMessage()
    {
    }

    public InboundMessage(string key, int dataSourceId, string xchangeId)
    {
        Id = key ?? throw new ArgumentNullException(nameof(key));
        DataSourceId = dataSourceId;
        XchangeId = xchangeId;
        SeenOn = DateTime.UtcNow;
    }

    /// <summary>Which data source it arrived on, for pruning and for diagnosing a duplicate.</summary>
    public int DataSourceId { get; private set; }

    /// <summary>What we persisted the first time, so a duplicate can be acknowledged with it.</summary>
    public string XchangeId { get; private set; }

    public DateTime SeenOn { get; private set; }
}
