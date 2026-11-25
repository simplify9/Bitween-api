using System.Threading;
using System.Threading.Tasks;
using SW.Bitween.Domain;

namespace SW.Bitween.Services.XchangeStates;

/// <summary>
/// The index the Exchanges page and dashboard read <see cref="Xchange.Status"/> and
/// <see cref="Xchange.Retried"/> through, built by <see cref="XchangeStateFiller"/> and never by a
/// migration (see <see cref="RuntimeIndex"/> for how).
/// </summary>
/// <remarks>
/// <para>
/// On Postgres, partial over the exchanges with a status, a few percent of them.
/// </para>
/// <para>
/// Status, then start time, then retried: every view lists one status newest first, which the first
/// two give in order. Retried last because "not retried" is a blank, and a test for blank can't keep
/// the start times in order, so ahead of them it left Postgres sorting thousands of failures to
/// show 25; measured on 3M exchanges, 18 s against 3 ms this way.
/// </para>
/// </remarks>
public static class XchangeStateIndex
{
    public const string Name = "ix_xchange_state";

    /// <summary>The Postgres advisory lock one build holds, so no other node's run touches it.</summary>
    internal const long BuildLock = 7_311_240_001;

    private static readonly RuntimeIndex Index = new(Name, BuildLock,
        [nameof(Xchange.Status), nameof(Xchange.StartedOn), nameof(Xchange.Retried)], nameof(Xchange.Status));

    /// <inheritdoc cref="RuntimeIndex.EnsureAsync"/>
    public static Task<bool> EnsureAsync(BitweenDbContext db, BitweenOptions options,
        CancellationToken cancellation = default) => Index.EnsureAsync(db, options, cancellation);

    /// <inheritdoc cref="RuntimeIndex.BuildsOnline"/>
    public static bool BuildsOnline(int engineEdition) => RuntimeIndex.BuildsOnline(engineEdition);

    /// <inheritdoc cref="RuntimeIndex.NeedsQuietHourAsync"/>
    public static Task<bool> NeedsQuietHourAsync(BitweenDbContext db, BitweenOptions options,
        CancellationToken cancellation = default) => RuntimeIndex.NeedsQuietHourAsync(db, options, cancellation);
}
