using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;

namespace SW.Bitween.Services.XchangeStates;

/// <summary>
/// Whether the Exchanges page and dashboard may read <see cref="Xchange.Status"/> and
/// <see cref="Xchange.Retried"/>: once <see cref="XchangeStateFiller"/> has finished. Until then they
/// keep working the state out from the result and retry tables, as they always have.
/// </summary>
/// <remarks>
/// Kept for half a minute per node, so a page asks the database once in a while rather than on
/// every request. When the fill finishes, each node switches within that half minute.
/// </remarks>
public static class XchangeStateReadiness
{
    private static readonly TimeSpan KeptFor = TimeSpan.FromSeconds(30);

    private sealed record Known(bool Ready, DateTime Until);

    private static volatile Known known;

    public static async Task<bool> IsReadyAsync(BitweenDbContext db, CancellationToken cancellation = default)
    {
        var current = known;
        if (current != null && DateTime.UtcNow < current.Until) return current.Ready;

        var ready = await db.Set<XchangeStateFill>().AsNoTracking()
            .AnyAsync(f => f.Phase == XchangeStateFillPhase.Ready, cancellation);
        known = new Known(ready, DateTime.UtcNow + KeptFor);
        return ready;
    }

    /// <summary>Asks the database again next time. For tests, which change the fill under it.</summary>
    internal static void Forget() => known = null;
}
