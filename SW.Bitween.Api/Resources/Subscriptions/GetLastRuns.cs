using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using SW.Scheduler;

namespace SW.Bitween.Resources.Subscriptions;

/// <summary>
/// The newest run of every scheduled subscription, so a list can show a last-run
/// column without asking per row.
/// </summary>
[HandlerName("lastruns")]
public class GetLastRuns : IQueryHandler<SearchSubscriptionLastRunsModel, object>
{
    /// <summary>How many recent runs the success ratio is measured over.</summary>
    private const int RecentWindow = 20;

    private readonly BitweenDbContext dbContext;
    private readonly RequestContext requestContext;
    private readonly IScheduleRepository scheduleRepo;
    private readonly SchedulerOptions schedulerOptions;

    public GetLastRuns(
        BitweenDbContext dbContext,
        RequestContext requestContext,
        IScheduleRepository scheduleRepo,
        SchedulerOptions schedulerOptions)
    {
        this.dbContext = dbContext;
        this.requestContext = requestContext;
        this.scheduleRepo = scheduleRepo;
        this.schedulerOptions = schedulerOptions;
    }

    public async Task<object> Handle(SearchSubscriptionLastRunsModel request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Subscriptions.View);

        var subscriptions = await dbContext.Set<Subscription>()
            .AsNoTracking()
            .Where(s => s.Type == SubscriptionType.Receiving || s.Type == SubscriptionType.Aggregation)
            .Select(s => new { s.Id, s.Type })
            .ToListAsync();

        var since = DateTime.UtcNow.AddDays(-schedulerOptions.RetentionDays);
        var results = new List<SubscriptionLastRunModel>();

        // One small indexed query per scheduled subscription. The subscription id
        // only exists inside the execution's JSON parameter, so there is nothing to
        // GROUP BY — but each of these is an ordered top-N, and the alternative
        // (pulling a whole retention window of every job's executions and grouping
        // in memory) is far worse on an instance with chatty schedules.
        //
        // Taking the window rather than just the newest row gets the success ratio
        // out of the same query instead of a second aggregate per subscription.
        foreach (var s in subscriptions)
        {
            var job = scheduleRepo.GetJobDefinitions()
                .Single(d => d.JobType == SubscriptionRunHistory.JobTypeFor(s.Type));
            var manualPrefix = SubscriptionRunHistory.ManualPrefix(job);

            var window = await SubscriptionRunHistory
                .Query(dbContext, job, s.Id, since)
                .Take(RecentWindow)
                .Select(j => new
                {
                    j.StartTimeUtc,
                    j.EndTimeUtc,
                    j.DurationMs,
                    j.Success,
                    j.Error,
                    j.Node,
                    Manual = j.JobName.StartsWith(manualPrefix)
                })
                .ToListAsync();

            if (window.Count == 0) continue;

            var last = window[0];
            results.Add(new SubscriptionLastRunModel
            {
                SubscriptionId = s.Id,
                StartedOn = last.StartTimeUtc,
                EndedOn = last.EndTimeUtc,
                DurationMs = last.DurationMs,
                Success = last.Success,
                Error = last.Error,
                Node = last.Node,
                Manual = last.Manual,
                RecentTotal = window.Count(j => j.Success != null),
                RecentSucceeded = window.Count(j => j.Success == true)
            });
        }

        return results;
    }
}
