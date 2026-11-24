using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quartz;
using Quartz.Impl.Matchers;
using SW.Bitween.Domain;
using SW.Scheduler;

namespace SW.Bitween.Services.XchangeStates;

/// <summary>
/// Schedules <see cref="XchangeStateFillJob"/> again if its schedule goes missing before the fill is
/// done, then stops watching.
/// </summary>
/// <remarks>
/// During the deploy that brings the fill, nodes still on the version before it share the schedule.
/// One of them can claim the fill's first run, and not knowing the job, the scheduler deletes its
/// schedule. Each node schedules it when it starts, so it only stays missing when that happens after
/// the last new node started - and then until the next restart, with nothing saying why.
/// </remarks>
public class XchangeStateFillWatch(IServiceProvider sp, ILogger<XchangeStateFillWatch> logger) : BackgroundService
{
    /// <summary>A deploy's older nodes are gone within minutes, so the fill is never long without a schedule.</summary>
    internal static readonly TimeSpan Every = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Every);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = sp.CreateScope();
                var services = scope.ServiceProvider;
                if (await CheckAsync(services.GetRequiredService<BitweenDbContext>(),
                        services.GetRequiredService<IScheduleRepository>(),
                        services.GetRequiredService<ISchedulerFactory>(),
                        services.GetRequiredService<BitweenOptions>().XchangeStateFillCron, logger, stoppingToken))
                    return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Couldn't check the exchange status fill's schedule; trying again in {Minutes} min.",
                    Every.TotalMinutes);
            }
        }
    }

    /// <summary>
    /// Schedules the fill if it is not done and has no schedule. True once the fill is done, when
    /// there is nothing left to watch.
    /// </summary>
    /// <remarks>
    /// Only when the schedule is missing: scheduling replaces it, which would also clear Quartz's
    /// note that a run is under way and let a second one start beside it.
    /// </remarks>
    internal static async Task<bool> CheckAsync(BitweenDbContext db, IScheduleRepository scheduleRepo,
        ISchedulerFactory schedulerFactory, string cron, ILogger logger, CancellationToken cancellation = default)
    {
        if (await db.Set<XchangeStateFill>().AnyAsync(f => f.Phase == XchangeStateFillPhase.Ready, cancellation))
            return true;

        var group = scheduleRepo.GetJobDefinitions().Single(d => d.JobType == typeof(XchangeStateFillJob)).Group;
        var scheduler = await schedulerFactory.GetScheduler(cancellation);
        foreach (var job in await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals(group), cancellation))
            if ((await scheduler.GetTriggersOfJob(job, cancellation)).Count > 0)
                return false;

        logger.LogWarning("The exchange status fill had no schedule, so it is scheduled again.");
        await scheduleRepo.Schedule<XchangeStateFillJob>(cron);
        return false;
    }
}
