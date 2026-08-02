using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Quartz;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using SW.Scheduler;

namespace SW.Bitween.Resources.Subscriptions;

/// <summary>
/// Answers "will this schedule actually fire?" by asking the scheduler rather than
/// trusting what Bitween stored. Covers the two ways a job goes quiet without any
/// error surfacing: a trigger that is missing or not in a firing state, and a
/// subscription left flagged as running so its concurrency guard blocks every fire.
/// </summary>
[HandlerName("schedulehealth")]
public class GetScheduleHealth : IQueryHandler<SearchSubscriptionScheduleHealthModel, object>
{
    /// <summary>
    /// Mirrors SW.Scheduler's internal Constants.JobParamsKey — the Quartz data-map
    /// entry it serializes the job parameter under. Stable: it is persisted in
    /// qrtz_job_details, so changing it would break existing schedules.
    /// </summary>
    private const string JobParamsKey = "JobParams";

    private readonly BitweenDbContext dbContext;
    private readonly RequestContext requestContext;
    private readonly IScheduleRepository scheduleRepo;
    private readonly ISchedulerFactory schedulerFactory;

    public GetScheduleHealth(
        BitweenDbContext dbContext,
        RequestContext requestContext,
        IScheduleRepository scheduleRepo,
        ISchedulerFactory schedulerFactory)
    {
        this.dbContext = dbContext;
        this.requestContext = requestContext;
        this.scheduleRepo = scheduleRepo;
        this.schedulerFactory = schedulerFactory;
    }

    public async Task<object> Handle(SearchSubscriptionScheduleHealthModel request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Subscriptions.View);

        // Inactive subscriptions are unscheduled on purpose, so they have no triggers
        // and reporting them as broken would be noise.
        var subscriptions = await dbContext.Set<Subscription>()
            .AsNoTracking()
            .Where(s =>
                (s.Type == SubscriptionType.Receiving || s.Type == SubscriptionType.Aggregation) &&
                !s.Inactive)
            .ToListAsync();

        if (subscriptions.Count == 0)
            return new List<SubscriptionScheduleHealthModel>();

        var scheduler = await schedulerFactory.GetScheduler();
        var jobs = scheduleRepo.GetJobDefinitions().ToDictionary(d => d.JobType);
        var busy = await CurrentlyRunningSubscriptionIds(scheduler, jobs);

        var results = new List<SubscriptionScheduleHealthModel>();

        foreach (var sub in subscriptions)
        {
            var job = jobs[SubscriptionRunHistory.JobTypeFor(sub.Type)];
            var prefix = sub.Type == SubscriptionType.Receiving ? "receiver" : "aggregator";

            var states = new List<TriggerState>();
            DateTime? next = null;

            foreach (var schedule in sub.Schedules)
            {
                var jobKey = new JobKey(ScheduleToCronExtension.ScheduleKeyFor(prefix, sub.Id, schedule), job.Group);

                foreach (var trigger in await scheduler.GetTriggersOfJob(jobKey))
                {
                    states.Add(await scheduler.GetTriggerState(trigger.Key));

                    var fire = trigger.GetNextFireTimeUtc()?.UtcDateTime;
                    if (fire != null && (next == null || fire < next))
                        next = fire;
                }
            }

            results.Add(new SubscriptionScheduleHealthModel
            {
                SubscriptionId = sub.Id,
                ScheduleCount = sub.Schedules.Count,
                TriggerCount = states.Count,
                State = WorstState(states, sub.Schedules.Count),
                NextFireOn = next,
                Stuck = sub.IsRunning && !busy.Contains(sub.Id)
            });
        }

        return results;
    }

    /// <summary>
    /// Subscription ids the scheduler is executing right now. This — not the
    /// job-execution rows — is what distinguishes a genuinely running job from a
    /// stale IsRunning flag: a killed run never writes its end either, so its
    /// execution row stays open forever and would look identical.
    /// </summary>
    private static async Task<HashSet<int>> CurrentlyRunningSubscriptionIds(
        IScheduler scheduler, Dictionary<Type, IScheduledJobDefinition> jobs)
    {
        var groups = jobs.Values.Select(d => d.Group).ToHashSet();
        var running = new HashSet<int>();

        foreach (var context in await scheduler.GetCurrentlyExecutingJobs())
        {
            if (!groups.Contains(context.JobDetail.Key.Group)) continue;

            var json = context.JobDetail.JobDataMap.GetString(JobParamsKey);
            if (string.IsNullOrEmpty(json)) continue;

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("subscriptionId", out var id) && id.TryGetInt32(out var value))
                running.Add(value);
        }

        return running;
    }

    /// <summary>The state worth reporting: anything that stops a fire outranks the ones that don't.</summary>
    private static string WorstState(IReadOnlyCollection<TriggerState> states, int scheduleCount)
    {
        // No trigger where a schedule says there should be one — the job simply
        // isn't registered with the scheduler and nothing will ever fire it.
        if (states.Count < scheduleCount) return "Missing";
        if (states.Count == 0) return "Missing";

        if (states.Contains(TriggerState.Error)) return "Error";
        if (states.Contains(TriggerState.Blocked)) return "Blocked";
        if (states.Contains(TriggerState.Paused)) return "Paused";
        if (states.Contains(TriggerState.None)) return "Missing";
        if (states.All(s => s == TriggerState.Complete)) return "Complete";
        return "Normal";
    }
}
