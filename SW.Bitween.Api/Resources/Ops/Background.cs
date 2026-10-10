using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Quartz;
using Quartz.Impl.Matchers;
using SW.Bitween.Domain;
using SW.PrimitiveTypes;
using SW.Scheduler;

namespace SW.Bitween.Resources.Ops;

/// <summary>
/// Bitween's own background work: the system jobs (retries, retention, clean-ups) with when each
/// last ran and runs next, and the outbox of messages waiting for the broker. Neither was visible:
/// a job that stopped firing, or an outbox backing up, showed only as its consequences.
/// </summary>
[HandlerName("background")]
public class Background(BitweenDbContext dbContext, RequestContext requestContext,
    IScheduleRepository scheduleRepo, ISchedulerFactory schedulerFactory) : IQueryHandler<object>
{
    static readonly (Type Type, string Name, string Does)[] SystemJobs =
    [
        (typeof(RetryJob), "Automatic retries", "Re-runs failed exchanges whose retry policy says they are due."),
        (typeof(ExchangeRetentionJob), "Exchange retention", "Deletes or archives exchanges older than they are kept."),
        (typeof(ReceiveAttemptCleanupJob), "Receive attempt clean-up", "Deletes old records of scheduled jobs' receive attempts."),
        (typeof(Services.DataSources.InboundMessagePruneJob), "Broker deduplication clean-up", "Forgets deduplication keys older than each data source keeps them."),
    ];

    public async Task<object> Handle()
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Monitoring.View, Model.Permissions.Dashboard.View);

        var scheduler = await schedulerFactory.GetScheduler();
        var running = (await scheduler.GetCurrentlyExecutingJobs()).Select(j => j.JobDetail.JobType).ToHashSet();
        var definitions = scheduleRepo.GetJobDefinitions().ToDictionary(d => d.JobType);

        var jobs = new List<object>();
        foreach (var (type, name, does) in SystemJobs)
        {
            DateTime? next = null, previous = null;
            string cron = null;
            var states = new List<string>();
            if (definitions.TryGetValue(type, out var definition))
                foreach (var key in await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals(definition.Group)))
                foreach (var trigger in await scheduler.GetTriggersOfJob(key))
                {
                    states.Add((await scheduler.GetTriggerState(trigger.Key)).ToString());
                    var n = trigger.GetNextFireTimeUtc()?.UtcDateTime;
                    var p = trigger.GetPreviousFireTimeUtc()?.UtcDateTime;
                    if (n != null && (next == null || n < next)) next = n;
                    if (p != null && (previous == null || p > previous)) previous = p;
                    cron ??= (trigger as ICronTrigger)?.CronExpressionString;
                }

            jobs.Add(new
            {
                Name = name,
                Does = does,
                Cron = cron,
                Scheduled = states.Count > 0,
                State = states.Count == 0 ? "NotScheduled" : states.Contains("Error") ? "Error" : states.Contains("Paused") ? "Paused" : "Normal",
                LastRanOn = previous,
                NextRunOn = next,
                RunningNow = running.Contains(type),
            });
        }

        var outbox = dbContext.Set<OutboxMessage>().AsNoTracking();
        var pending = outbox.Where(m => m.PublishedOn == null);
        var since = DateTime.UtcNow.AddHours(-1);
        return new
        {
            Jobs = jobs,
            Outbox = new
            {
                Pending = await pending.CountAsync(),
                OldestPendingOn = await pending.OrderBy(m => m.CreatedOn).Select(m => (DateTime?)m.CreatedOn).FirstOrDefaultAsync(),
                Failing = await pending.CountAsync(m => m.Attempts > 0),
                LastError = await pending.Where(m => m.LastError != null).OrderByDescending(m => m.CreatedOn)
                    .Select(m => m.LastError).FirstOrDefaultAsync(),
                PublishedLastHour = await outbox.CountAsync(m => m.PublishedOn >= since),
            },
        };
    }
}
