using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SW.Bitween.Domain;

namespace SW.Bitween.Services.XchangeStates;

/// <summary>
/// Labels the exchanges from before <see cref="Xchange.Status"/> and <see cref="Xchange.Retried"/>
/// existed, then checks once more the ones that ran while it did. Works in small batches and
/// remembers where it got to, so each run is short and a restart carries on.
/// </summary>
/// <remarks>
/// <para>
/// Newest first, so the exchanges people look at most are right soonest. Every batch is labeled from
/// the result and retry tables, whatever the columns hold, and only rows that differ are written:
/// nearly every exchange is a success that was never retried, which is blank already.
/// </para>
/// <para>
/// The re-check covers the deploy that brings these columns in, when servers without them still
/// finish some exchanges for a few minutes, and an exchange labeled processing by the fill just as
/// its result arrived. It waits until those servers are gone: see <see cref="DeployOverlap"/>.
/// </para>
/// </remarks>
public class XchangeStateFiller(BitweenDbContext db, BitweenOptions options, ILogger<XchangeStateFiller> logger)
{
    internal const int BatchSize = 5_000;

    /// <summary>The longest one statement may take. A batch is far below it.</summary>
    internal static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(2);

    /// <summary>Exchanges are timed by the node that made them, whose clock may be a little off.</summary>
    internal static readonly TimeSpan ClockMargin = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How long after the fill began the re-check waits, so the servers of the earlier version a deploy
    /// replaces have stopped: they save results and retries without labels, and nothing checks again
    /// once the fill is done. A small database is filled within a minute, while they may still run.
    /// </summary>
    internal static readonly TimeSpan DeployOverlap = TimeSpan.FromMinutes(30);

    /// <summary>MySQL takes a list of ids to update, not a condition that reads the same table.</summary>
    private const int IdsPerUpdate = 1_000;

    /// <summary>
    /// Works until <paramref name="budget"/> has passed, always at least one step, and returns the
    /// phase it stopped in. <paramref name="deployOverlap"/> replaces <see cref="DeployOverlap"/>.
    /// </summary>
    public async Task<XchangeStateFillPhase> RunAsync(TimeSpan budget, int batchSize = BatchSize,
        TimeSpan? deployOverlap = null, CancellationToken cancellation = default)
    {
        var until = DateTime.UtcNow + budget;
        db.Database.SetCommandTimeout(CommandTimeout);

        var fill = await db.Set<XchangeStateFill>()
            .SingleOrDefaultAsync(f => f.Id == XchangeStateFill.SingleId, cancellation);
        if (fill == null)
        {
            fill = new XchangeStateFill(DateTime.UtcNow);
            db.Add(fill);
            try
            {
                await db.SaveChangesAsync(cancellation);
                logger.LogInformation("Filling exchange status for exchanges started before {FillBegan:u}.", fill.FillBegan);
            }
            catch (DbUpdateException)
            {
                // Another run started it a moment ago: carry on with its row.
                db.Entry(fill).State = EntityState.Detached;
                fill = await db.Set<XchangeStateFill>()
                    .SingleAsync(f => f.Id == XchangeStateFill.SingleId, cancellation);
            }
        }

        var startedIn = fill.Phase;
        var labeled = 0;
        var waiting = false;
        do
        {
            switch (fill.Phase)
            {
                case XchangeStateFillPhase.Fill:
                    var filled = await FillBatchAsync(fill, batchSize, cancellation);
                    labeled += filled;
                    if (filled < batchSize) fill.MoveTo(XchangeStateFillPhase.Index);
                    break;

                case XchangeStateFillPhase.Index:
                    // Before the re-check, whose last step reads the processing ones through it.
                    var builtAt = DateTime.UtcNow;
                    if (await XchangeStateIndex.EnsureAsync(db, options, cancellation))
                    {
                        logger.LogInformation("Exchange status index {Index} ready in {Seconds:0.#} s.",
                            XchangeStateIndex.Name, (DateTime.UtcNow - builtAt).TotalSeconds);
                        fill.MoveTo(XchangeStateFillPhase.Recheck);
                    }
                    else
                    {
                        // Waiting for the quiet hour or for another node's build, or about to build
                        // again: a later run tries.
                        waiting = true;
                    }
                    break;

                case XchangeStateFillPhase.Recheck:
                    if (await RecheckWaitsUntilAsync(db, fill, deployOverlap ?? DeployOverlap, cancellation) != null)
                    {
                        waiting = true;
                        break;
                    }
                    var rechecked = await RecheckBatchAsync(fill, batchSize, cancellation);
                    labeled += rechecked;
                    if (rechecked < batchSize)
                    {
                        await LabelRetriedOriginalsAsync(fill, cancellation);
                        await LabelAsync(x => x.Status == XchangeStatus.Processing, cancellation);
                        fill.MoveTo(XchangeStateFillPhase.Ready);
                    }
                    break;
            }

            await db.SaveChangesAsync(cancellation);
        } while (fill.Phase != XchangeStateFillPhase.Ready && !waiting && DateTime.UtcNow < until);

        if (labeled > 0 || fill.Phase != startedIn)
            logger.LogInformation("Exchange status: {Phase}, {Labeled} exchange(s) checked this run.",
                fill.Phase, labeled);

        return fill.Phase;
    }

    /// <summary>The next batch before the fill began, newest first. Returns how many it covered.</summary>
    private async Task<int> FillBatchAsync(XchangeStateFill fill, int batchSize, CancellationToken cancellation)
    {
        var began = fill.FillBegan;
        var query = db.Set<Xchange>().AsNoTracking().Where(x => x.StartedOn < began);
        if (fill.PositionStartedOn is { } on)
        {
            var id = fill.PositionId;
            // The plain bound lets the index start at the position, rather than walk past every
            // exchange done already; the second part only settles ties.
            query = query.Where(x => x.StartedOn <= on
                                     && (x.StartedOn < on || string.Compare(x.Id, id) < 0));
        }

        var keys = await query.OrderByDescending(x => x.StartedOn).ThenByDescending(x => x.Id)
            .Select(x => new { x.StartedOn, x.Id }).Take(batchSize).ToListAsync(cancellation);
        if (keys.Count == 0) return 0;

        await LabelAsync(Between(keys[^1].StartedOn, keys[^1].Id, keys[0].StartedOn, keys[0].Id), cancellation);
        fill.Advance(keys[^1].StartedOn, keys[^1].Id);
        return keys.Count;
    }

    /// <summary>
    /// The next batch from just before the fill began up to now, oldest first. Returns how many it covered.
    /// </summary>
    private async Task<int> RecheckBatchAsync(XchangeStateFill fill, int batchSize, CancellationToken cancellation)
    {
        var from = fill.FillBegan - ClockMargin;
        var query = db.Set<Xchange>().AsNoTracking().Where(x => x.StartedOn >= from);
        if (fill.PositionStartedOn is { } on)
        {
            var id = fill.PositionId;
            query = query.Where(x => x.StartedOn >= on
                                     && (x.StartedOn > on || string.Compare(x.Id, id) > 0));
        }

        var keys = await query.OrderBy(x => x.StartedOn).ThenBy(x => x.Id)
            .Select(x => new { x.StartedOn, x.Id }).Take(batchSize).ToListAsync(cancellation);
        if (keys.Count == 0) return 0;

        await LabelAsync(Between(keys[0].StartedOn, keys[0].Id, keys[^1].StartedOn, keys[^1].Id), cancellation);
        fill.Advance(keys[^1].StartedOn, keys[^1].Id);
        return keys.Count;
    }

    /// <summary>
    /// When the re-check may start, or null when it may now: <paramref name="overlap"/> after the fill
    /// began, unless there were no exchanges before it, as on a new install, which has no servers of an
    /// earlier version to wait for.
    /// </summary>
    internal static async Task<DateTime?> RecheckWaitsUntilAsync(BitweenDbContext db, XchangeStateFill fill,
        TimeSpan overlap, CancellationToken cancellation = default)
    {
        if (fill.Phase != XchangeStateFillPhase.Recheck || fill.PositionStartedOn != null) return null;
        var until = fill.FillBegan + overlap;
        if (DateTime.UtcNow >= until) return null;
        var began = fill.FillBegan;
        return await db.Set<Xchange>().AnyAsync(x => x.StartedOn < began, cancellation) ? until : null;
    }

    /// <summary>
    /// The exchanges retried since the fill began, however old: a server of the earlier version doesn't
    /// flag them, and the re-check walks only the exchanges that started since.
    /// </summary>
    private async Task LabelRetriedOriginalsAsync(XchangeStateFill fill, CancellationToken cancellation)
    {
        var from = fill.FillBegan - ClockMargin;
        var originals = await db.Set<Xchange>().Where(x => x.StartedOn >= from && x.RetryFor != null)
            .Select(x => x.RetryFor).Distinct().ToListAsync(cancellation);
        foreach (var ids in originals.Chunk(IdsPerUpdate))
            await LabelAsync(x => ids.Contains(x.Id), cancellation);
    }

    /// <summary>
    /// Exchanges from one (start time, id) to another, both included. The plain time bounds are what
    /// the index range is taken from; the rest only settles ties at either end.
    /// </summary>
    private static Expression<Func<Xchange, bool>> Between(DateTime fromOn, string fromId, DateTime toOn, string toId) =>
        x => x.StartedOn >= fromOn && x.StartedOn <= toOn
             && (x.StartedOn > fromOn || string.Compare(x.Id, fromId) >= 0)
             && (x.StartedOn < toOn || string.Compare(x.Id, toId) <= 0);

    /// <summary>
    /// Sets both labels from the result and retry tables on the exchanges <paramref name="scope"/>
    /// picks, writing only the ones that differ. The same rules as <see cref="Xchange.RecordOutcome"/>.
    /// Retried is only ever set, never cleared.
    /// </summary>
    private async Task LabelAsync(Expression<Func<Xchange, bool>> scope, CancellationToken cancellation)
    {
        var results = db.Set<XchangeResult>();
        var exchanges = db.Set<Xchange>().Where(scope);

        await exchanges
            .Where(x => x.Status != XchangeStatus.Failed && results.Any(r => r.Id == x.Id && !r.Success))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, (XchangeStatus?)XchangeStatus.Failed), cancellation);
        await exchanges
            .Where(x => x.Status != XchangeStatus.BadResponse && results.Any(r => r.Id == x.Id && r.Success && r.ResponseBad))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, (XchangeStatus?)XchangeStatus.BadResponse), cancellation);
        await exchanges
            .Where(x => x.Status != null && results.Any(r => r.Id == x.Id && r.Success && !r.ResponseBad))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, (XchangeStatus?)null), cancellation);
        await exchanges
            .Where(x => x.Status != XchangeStatus.Processing && !results.Any(r => r.Id == x.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, (XchangeStatus?)XchangeStatus.Processing), cancellation);

        // Read first, then written by id: MySQL refuses an update whose condition reads the table it updates.
        var retried = await exchanges
            .Where(x => x.Retried != true && db.Set<Xchange>().Any(r => r.RetryFor == x.Id))
            .Select(x => x.Id).ToListAsync(cancellation);
        foreach (var ids in retried.Chunk(IdsPerUpdate))
            await db.Set<Xchange>().Where(x => ids.Contains(x.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Retried, (bool?)true), cancellation);
    }
}
