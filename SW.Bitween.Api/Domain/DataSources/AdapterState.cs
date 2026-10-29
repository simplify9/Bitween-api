using System;

namespace SW.Bitween.Domain.DataSources;

/// <summary>
/// A bookmark a resident adapter asked Bitween to hold for it.
///
/// The motivating case is a polling database receiver's cursor - the last incrementing id or
/// timestamp it consumed. The adapter cannot keep it: the supervisor restarts it, the next instance
/// may come up on another node, and a pooled one is not the same process twice. Any of those resets
/// a cursor held in a field back to the beginning, which means replaying every row already
/// processed. So the host holds it, and the adapter reads it back at startup.
///
/// This is the same role Airbyte's <c>state</c> argument plays for its connectors, and it is
/// deliberately tiny: a bookmark, not a place to stage data. See <see cref="MaxValueLength"/>.
/// </summary>
public class AdapterState
{
    /// <summary>
    /// Big enough for a cursor, a watermark or a small JSON object holding several of them; small
    /// enough that nobody mistakes this for storage. A write past it is refused with a message
    /// saying so rather than truncated.
    /// </summary>
    public const int MaxValueLength = 8000;

    public string AdapterId { get; set; }

    /// <summary>
    /// Which instance owns it - the data source id for an exclusive resident. Part of the key
    /// because two instances of one adapter are two different connections, and one reading the
    /// other's cursor would skip rows that were never processed.
    /// </summary>
    public string InstanceKey { get; set; }

    /// <summary>Chosen by the adapter, which namespaces its own entries.</summary>
    public string Name { get; set; }

    public string Value { get; set; }

    public DateTime UpdatedOn { get; set; }
}
