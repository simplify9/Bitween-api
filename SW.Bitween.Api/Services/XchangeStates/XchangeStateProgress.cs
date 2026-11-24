using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;

namespace SW.Bitween.Services.XchangeStates;

/// <summary>
/// How far the exchange status fill has got, for Queue health and the Exchanges page.
/// </summary>
/// <param name="Phase">
/// NotStarted (the job hasn't run yet), Filling, BuildingIndex, WaitingForQuietHour, Rechecking or Done.
/// </param>
/// <param name="Percent">How much of the exchanges' date range is labeled. 100 only when done.</param>
/// <param name="EstimatedFinishOn">
/// While filling, when the labeling should be done at the pace so far, once there is a pace and it
/// gives a date. While the re-check waits for a deploy's earlier servers to stop, when it starts.
/// </param>
/// <param name="QuietHourUtc">The hour a database that can't build online waits for.</param>
public record XchangeStateProgress(string Phase, int Percent, DateTime? EstimatedFinishOn, int QuietHourUtc)
{
    public static async Task<XchangeStateProgress> ReadAsync(BitweenDbContext db, BitweenOptions options,
        CancellationToken cancellation = default)
    {
        var quietHour = options.MaintenanceQuietHourUtc;
        var fill = await db.Set<XchangeStateFill>().AsNoTracking()
            .SingleOrDefaultAsync(f => f.Id == XchangeStateFill.SingleId, cancellation);

        switch (fill?.Phase)
        {
            case null:
                return new("NotStarted", 0, null, quietHour);

            case XchangeStateFillPhase.Fill:
                if (fill.PositionStartedOn is not { } position)
                    return new("Filling", 0, null, quietHour);

                var oldest = await db.Set<Xchange>().MinAsync(x => (DateTime?)x.StartedOn, cancellation) ?? position;
                var (percent, finish) = Estimate(fill.FillBegan, position, oldest, DateTime.UtcNow);
                return new("Filling", percent, finish, quietHour);

            case XchangeStateFillPhase.Index:
                return new(await XchangeStateIndex.NeedsQuietHourAsync(db, options, cancellation)
                        && DateTime.UtcNow.Hour != quietHour
                        ? "WaitingForQuietHour"
                        : "BuildingIndex",
                    99, null, quietHour);

            case XchangeStateFillPhase.Recheck:
                return new("Rechecking", 99, await XchangeStateFiller.RecheckWaitsUntilAsync(
                    db, fill, XchangeStateFiller.DeployOverlap, cancellation), quietHour);

            default:
                return new("Done", 100, null, quietHour);
        }
    }

    /// <summary>
    /// How much of the date range is filled, by the time covered: the fill goes newest first, from
    /// when it <paramref name="began"/> back to the <paramref name="oldest"/> exchange. And when it
    /// should finish at the pace so far, unless that is past any date, as for a fill that stalled for
    /// days just after it began.
    /// </summary>
    internal static (int Percent, DateTime? Finish) Estimate(DateTime began, DateTime position, DateTime oldest,
        DateTime now)
    {
        var span = (began - oldest).Ticks;
        var done = span <= 0 ? 1d : Math.Clamp((double)(began - position).Ticks / span, 0, 1);
        var takes = done > 0 ? (now - began).Ticks / done : double.MaxValue;
        DateTime? finish = takes < (DateTime.MaxValue - began).Ticks ? began + TimeSpan.FromTicks((long)takes) : null;
        return (Math.Min(99, (int)(done * 100)), finish);
    }
}
