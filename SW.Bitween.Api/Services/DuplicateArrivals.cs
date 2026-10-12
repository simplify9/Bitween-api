using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.PrimitiveTypes;

namespace SW.Bitween;

/// <param name="OriginalXchangeId">The earlier exchange the payload repeats.</param>
/// <param name="Drop">Whether the type drops duplicates, rather than only reporting them.</param>
public record DuplicateVerdict(string OriginalXchangeId, bool Drop)
{
    /// <summary>The reference a duplicate that is let through carries, so it can be searched for.</summary>
    public string Reference => $"duplicate-of: {OriginalXchangeId}";
}

/// <summary>
/// The information type's duplicate window, checked where a payload first arrives from outside:
/// an API gateway, the older partner endpoint, a scheduled job, the bus or a customer's broker.
/// Never for retries, aggregations or what a pipeline makes from a response, which repeat a payload
/// on purpose.
/// </summary>
/// <remarks>
/// The comparison is the payload's hash, which every exchange already stores and indexes, against
/// exchanges of the same type started within the window. Per partner: the same document from two
/// partners is two documents. A payload that arrives with no partner, from the bus or a broker,
/// matches whichever partner its earlier copy was routed to, since it is the same message again.
/// </remarks>
public class DuplicateArrivals(BitweenDbContext dbContext)
{
    /// <summary>How long duplicates are kept for counting.</summary>
    public const int KeepDays = 30;

    /// <returns>Null when the payload isn't a duplicate, or the type has no window.</returns>
    public async Task<DuplicateVerdict> CheckAsync(Document document, int? partnerId, XchangeFile file)
    {
        if (document == null || document.DuplicateInterval <= 0 || file?.Data == null) return null;

        var since = DateTime.UtcNow.AddMinutes(-document.DuplicateInterval);
        var hash = file.Hash;
        var earlier = dbContext.Set<Xchange>().AsNoTracking()
            .Where(x => x.DocumentId == document.Id && x.InputHash == hash && x.RetryFor == null && x.StartedOn >= since);
        if (partnerId != null)
            earlier = earlier.Where(x => x.PartnerId == partnerId);

        var original = await earlier.OrderBy(x => x.StartedOn).Select(x => x.Id).FirstOrDefaultAsync();
        if (original == null) return null;

        // Saved with whatever the caller saves next: the exchange let through, or nothing when dropped.
        dbContext.Add(new DuplicateArrival(document.Id, partnerId, original, document.DropDuplicates));
        return new DuplicateVerdict(original, document.DropDuplicates);
    }

    /// <summary>Duplicates within the last <paramref name="days"/> days, and how many of them were dropped.</summary>
    public async Task<(int Seen, int Dropped)> CountAsync(int documentId, int days = 7)
    {
        var since = DateTime.UtcNow.AddDays(-days);
        var rows = dbContext.Set<DuplicateArrival>().AsNoTracking()
            .Where(d => d.DocumentId == documentId && d.OccurredOn >= since);
        return (await rows.CountAsync(), await rows.CountAsync(d => d.Dropped));
    }

    /// <summary>Removes duplicates older than <see cref="KeepDays"/>.</summary>
    public Task<int> PruneAsync() =>
        dbContext.Set<DuplicateArrival>()
            .Where(d => d.OccurredOn < DateTime.UtcNow.AddDays(-KeepDays))
            .ExecuteDeleteAsync();
}
