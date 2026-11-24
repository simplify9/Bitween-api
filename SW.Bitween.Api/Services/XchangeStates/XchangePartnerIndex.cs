using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SW.Bitween.Domain;

namespace SW.Bitween.Services.XchangeStates;

/// <summary>
/// The index the partner portal lists a partner's exchanges through, newest first: partner, then
/// start time. Built by the same job as <see cref="XchangeStateIndex"/>, once the status fill is
/// done, and never by a migration (see <see cref="RuntimeIndex"/>).
/// </summary>
/// <remarks>
/// Without it, a partner with few exchanges had Postgres walk the start-time index backwards past
/// everyone else's to find a page of theirs: 0.27 s on 2M exchanges, and longer the more there are.
/// On Postgres it covers only the exchanges that carry a partner.
/// </remarks>
public static class XchangePartnerIndex
{
    public const string Name = "ix_xchange_partner";

    internal const long BuildLock = 7_311_240_002;

    private static readonly RuntimeIndex Index = new(Name, BuildLock,
        [nameof(Xchange.PartnerId), nameof(Xchange.StartedOn)], nameof(Xchange.PartnerId));

    /// <summary>Set once this node has seen it built, so later runs don't ask the database again.</summary>
    private static volatile bool built;

    /// <summary>
    /// Builds it if it isn't there. Quick once built; until then it may take the whole run, or wait
    /// for the quiet hour or for another node, and a later run tries again.
    /// </summary>
    public static async Task EnsureAsync(BitweenDbContext db, BitweenOptions options, ILogger logger,
        CancellationToken cancellation = default)
    {
        if (built) return;
        var startedAt = DateTime.UtcNow;
        if (!await Index.EnsureAsync(db, options, cancellation)) return;
        built = true;
        var seconds = (DateTime.UtcNow - startedAt).TotalSeconds;
        if (seconds > 1)
            logger.LogInformation("Exchange partner index {Index} ready in {Seconds:0.#} s.", Name, seconds);
    }

    /// <summary>Asks the database again next time. For tests, which drop it under it.</summary>
    internal static void Forget() => built = false;
}
