using System.Linq;
using System.Threading.Tasks;
using Quartz;
using SW.Bitween.Domain;
using SW.PrimitiveTypes;
using SW.Scheduler;

namespace SW.Bitween.Resources.Subscriptions;

public class SubscriptionClearRunning { }

/// <summary>
/// Clears the running flag of a scheduled job or aggregation whose run died with its process. Until
/// it was cleared, every later run was skipped as "already running" for StaleRunAfterMinutes, and
/// the only other way out was editing the database. Refused while the job really is running here.
/// Saved through the context, so the audit trail says who cleared it.
/// </summary>
[HandlerName("clearrunning")]
public class ClearRunning(BitweenDbContext dbContext, RequestContext requestContext,
    IScheduleRepository scheduleRepo, ISchedulerFactory schedulerFactory)
    : ICommandHandler<int, SubscriptionClearRunning, object>
{
    public async Task<object> Handle(int key, SubscriptionClearRunning request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Subscriptions.Operate);

        var entity = await dbContext.FindAsync<Subscription>(key) ?? throw new SWNotFoundException(key.ToString());
        if (!entity.IsRunning) return new { entity.Id, Cleared = false };

        var scheduler = await schedulerFactory.GetScheduler();
        var jobs = scheduleRepo.GetJobDefinitions().ToDictionary(d => d.JobType);
        if ((await GetScheduleHealth.CurrentlyRunningSubscriptionIds(scheduler, jobs)).Contains(key))
            throw new SWValidationException("STILL_RUNNING",
                "This job is running right now on this node. Wait for it to finish; it clears the flag itself.");

        entity.ClearRunning();
        await dbContext.SaveChangesAsync();
        return new { entity.Id, Cleared = true };
    }
}
