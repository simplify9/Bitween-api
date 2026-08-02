using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using SW.Scheduler;

namespace SW.Bitween.Resources.Subscriptions;

/// <summary>
/// Execution history for one scheduled subscription, read from the scheduler's own
/// <c>job_executions</c> table (populated by AddSchedulerMonitoring, bounded by
/// <see cref="SchedulerOptions.RetentionDays"/>).
/// </summary>
[HandlerName("runs")]
public class GetRuns : IQueryHandler<SearchSubscriptionRunsModel, object>
{
    private const int MaxLimit = 100;

    private readonly BitweenDbContext dbContext;
    private readonly RequestContext requestContext;
    private readonly IScheduleRepository scheduleRepo;
    private readonly SchedulerOptions schedulerOptions;

    public GetRuns(
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

    public async Task<object> Handle(SearchSubscriptionRunsModel request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Subscriptions.View);

        var limit = Math.Clamp(request.Limit ?? 20, 1, MaxLimit);

        var type = await dbContext.Set<Subscription>()
            .AsNoTracking()
            .Where(s => s.Id == request.SubscriptionId)
            .Select(s => (SubscriptionType?)s.Type)
            .SingleOrDefaultAsync();

        if (type == null)
            throw new SWNotFoundException($"Subscription {request.SubscriptionId} was not found.");

        var jobType = SubscriptionRunHistory.JobTypeFor(type.Value);
        if (jobType == null)
            return new List<SubscriptionRunModel>();

        var job = scheduleRepo.GetJobDefinitions().Single(d => d.JobType == jobType);
        var manualPrefix = SubscriptionRunHistory.ManualPrefix(job);
        var since = DateTime.UtcNow.AddDays(-schedulerOptions.RetentionDays);

        return await SubscriptionRunHistory
            .Query(dbContext, job, request.SubscriptionId, since)
            .Take(limit)
            .Select(j => new SubscriptionRunModel
            {
                StartedOn = j.StartTimeUtc,
                EndedOn = j.EndTimeUtc,
                DurationMs = j.DurationMs,
                Success = j.Success,
                Error = j.Error,
                Node = j.Node,
                Manual = j.JobName.StartsWith(manualPrefix)
            })
            .ToListAsync();
    }

    private class Validate : AbstractValidator<SearchSubscriptionRunsModel>
    {
        public Validate()
        {
            RuleFor(i => i.SubscriptionId).NotEmpty();
        }
    }
}
