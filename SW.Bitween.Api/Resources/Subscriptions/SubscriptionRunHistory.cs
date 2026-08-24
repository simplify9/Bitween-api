using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.Scheduler;

namespace SW.Bitween.Resources.Subscriptions;

/// <summary>
/// Shared lookup for reading a subscription's runs out of the scheduler's
/// <c>job_executions</c> table. Used by both <see cref="GetRuns"/> (one
/// subscription, full history) and <see cref="GetLastRuns"/> (every scheduled
/// subscription, newest run only).
/// </summary>
internal static class SubscriptionRunHistory
{
    /// <summary>The job that runs this subscription, or null if it isn't scheduled at all.</summary>
    public static Type JobTypeFor(SubscriptionType type) => type switch
    {
        SubscriptionType.Receiving => typeof(ReceivingJob),
        SubscriptionType.Aggregation => typeof(AggregationJob),
        _ => null
    };

    /// <summary>Executions of <paramref name="job"/> belonging to one subscription, newest first.</summary>
    public static IQueryable<JobExecution> Query(
        BitweenDbContext dbContext, IScheduledJobDefinition job, int subscriptionId, DateTime since)
    {
        // Executions are keyed per schedule entry, and a manual run gets its own
        // generated key entirely — so neither the subscription id nor the set of
        // current schedule keys can be matched on JobName. The subscription id is
        // in the serialized job parameter instead, which covers all of them:
        // scheduled runs, manual runs, and runs of schedules since edited away.
        //
        // The trailing comma keeps 128 from matching 1289 — safe because
        // ReceivingJobParams/AggregationJobParams both carry CronExpression after
        // SubscriptionId, so it is never the last property.
        var needle = $"\"subscriptionId\":{subscriptionId},";

        return dbContext.Set<JobExecution>()
            .AsNoTracking()
            .Where(j => j.JobGroup == job.Group && j.StartTimeUtc >= since && j.Context.Contains(needle))
            .OrderByDescending(j => j.StartTimeUtc);
    }

    /// <summary>A run started by Receive now / Aggregate now rather than by the cron.</summary>
    public static string ManualPrefix(IScheduledJobDefinition job) => $"{job.Name}_OneTime_";
}
