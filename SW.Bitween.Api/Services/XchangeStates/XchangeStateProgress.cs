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
/// When the labeling should be done at the pace so far. Only while filling, once there is a pace.
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

                // By the time covered: the fill goes newest first, from when it began back to the oldest.
                var oldest = await db.Set<Xchange>().MinAsync(x => (DateTime?)x.StartedOn, cancellation) ?? position;
                var span = (fill.FillBegan - oldest).Ticks;
                var done = span <= 0 ? 1d : Math.Clamp((double)(fill.FillBegan - position).Ticks / span, 0, 1);
                DateTime? finish = done > 0
                    ? fill.FillBegan + TimeSpan.FromTicks((long)((DateTime.UtcNow - fill.FillBegan).Ticks / done))
                    : null;
                return new("Filling", Math.Min(99, (int)(done * 100)), finish, quietHour);

            case XchangeStateFillPhase.Index:
                return new(await XchangeStateIndex.NeedsQuietHourAsync(db, options, cancellation)
                        && DateTime.UtcNow.Hour != quietHour
                        ? "WaitingForQuietHour"
                        : "BuildingIndex",
                    99, null, quietHour);

            case XchangeStateFillPhase.Recheck:
                return new("Rechecking", 99, null, quietHour);

            default:
                return new("Done", 100, null, quietHour);
        }
    }
}
