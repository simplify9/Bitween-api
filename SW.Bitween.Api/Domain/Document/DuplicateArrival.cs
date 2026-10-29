using System;

namespace SW.Bitween.Domain;

/// <summary>
/// A payload that arrived again within its information type's duplicate window: what the type's
/// page counts, and how an administrator decides whether to have duplicates dropped.
/// </summary>
public class DuplicateArrival
{
    private DuplicateArrival()
    {
    }

    public DuplicateArrival(int documentId, int? partnerId, string originalXchangeId, bool dropped)
    {
        DocumentId = documentId;
        PartnerId = partnerId;
        OriginalXchangeId = originalXchangeId;
        Dropped = dropped;
        OccurredOn = DateTime.UtcNow;
    }

    public long Id { get; private set; }
    public int DocumentId { get; private set; }
    public int? PartnerId { get; private set; }

    /// <summary>The earlier exchange this payload repeats.</summary>
    public string OriginalXchangeId { get; private set; }

    /// <summary>Dropped, or let through and reported.</summary>
    public bool Dropped { get; private set; }

    public DateTime OccurredOn { get; private set; }
}
