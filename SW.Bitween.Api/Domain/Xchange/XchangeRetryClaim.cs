using System;
using SW.PrimitiveTypes;

namespace SW.Bitween.Domain;

/// <summary>
/// That an exchange has been retried, keyed by the exchange retried, so the database itself refuses
/// a second retry of it: two committed at the very same moment can't both be kept.
/// </summary>
/// <remarks>
/// A table of its own rather than a unique index on <c>Xchange.RetryFor</c>: exchanges retried
/// before the one-retry rule existed can already have forked, and an index can't be created over
/// them. Claims start with the first retry made after this table, so nothing old stands in the way.
/// </remarks>
public class XchangeRetryClaim : BaseEntity<string>
{
    private XchangeRetryClaim()
    {
    }

    public XchangeRetryClaim(string retriedXchangeId, string retryXchangeId)
    {
        Id = retriedXchangeId;
        RetryXchangeId = retryXchangeId;
        ClaimedOn = DateTime.UtcNow;
    }

    /// <summary>The exchange made by the retry.</summary>
    public string RetryXchangeId { get; private set; }

    public DateTime ClaimedOn { get; private set; }
}
