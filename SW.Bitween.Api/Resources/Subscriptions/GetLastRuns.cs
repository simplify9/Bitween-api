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
        // GROUP BY — but each of these is an ordered top-1, and the alternative
        // (pulling a whole retention window of every job's executions and grouping
        // in memory) is far worse on an instance with chatty schedules.
        foreach (var s in subscriptions)
        {
            var job = scheduleRepo.GetJobDefinitions()
                .Single(d => d.JobType == SubscriptionRunHistory.JobTypeFor(s.Type));
            var manualPrefix = SubscriptionRunHistory.ManualPrefix(job);

            var last = await SubscriptionRunHistory
                .Query(dbContext, job, s.Id, since)
                .Select(j => new SubscriptionLastRunModel
                {
                    SubscriptionId = s.Id,
                    StartedOn = j.StartTimeUtc,
                    EndedOn = j.EndTimeUtc,
                    DurationMs = j.DurationMs,
                    Success = j.Success,
                    Error = j.Error,
                    Node = j.Node,
                    Manual = j.JobName.StartsWith(manualPrefix)
                })
                .FirstOrDefaultAsync();

            if (last != null)
                results.Add(last);
        }

        return results;
    }
}
