using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using SW.Scheduler;
using System;
using System.Threading.Tasks;

namespace SW.Bitween;

// Wraps IScheduleRepository with subscription-specific scheduling logic.
// Inject this (scoped) in command handlers and the seed service.
public class SubscriptionSchedulerService(IScheduleRepository scheduleRepo)
{
    // Unschedules all old schedule entries then schedules all new ones (if not inactive).
    // Capture entity.Schedules.ToList() BEFORE calling SetSchedules() to get the old set.
    public async Task Sync(Subscription sub, System.Collections.Generic.IReadOnlyCollection<Schedule> oldSchedules)
    {
        if (sub.Type != SubscriptionType.Receiving && sub.Type != SubscriptionType.Aggregation)
            return;

        foreach (var s in oldSchedules)
            await TryUnschedule(sub.Type, sub.Id, s);

        if (!sub.Inactive)
            foreach (var s in sub.Schedules)
                await Schedule(sub.Type, sub.Id, s);
    }

    // Registers all schedules for a subscription (no-op if already registered or inactive).
    public async Task ScheduleAll(Subscription sub)
    {
        if (sub.Type != SubscriptionType.Receiving && sub.Type != SubscriptionType.Aggregation)
            return;
        if (sub.Inactive)
            return;

        foreach (var s in sub.Schedules)
            await Schedule(sub.Type, sub.Id, s);
    }

    // Triggers a one-time immediate execution outside the normal cron cadence.
    public async Task RunNow(Subscription sub)
    {
        if (sub.Type == SubscriptionType.Receiving)
            await scheduleRepo.ScheduleOnce<ReceivingJob, ReceivingJobParams>(
                new ReceivingJobParams(sub.Id, null));
        else if (sub.Type == SubscriptionType.Aggregation)
            await scheduleRepo.ScheduleOnce<AggregationJob, AggregationJobParams>(
                new AggregationJobParams(sub.Id, null));
    }

    private async Task Schedule(SubscriptionType type, int subId, Schedule schedule)
    {
        var cron = schedule.ToCronExpression();

        if (type == SubscriptionType.Receiving)
        {
            var key = ScheduleToCronExtension.ScheduleKeyFor("receiver", subId, schedule);
            await scheduleRepo.ScheduleIfNotExists<ReceivingJob, ReceivingJobParams>(
                new ReceivingJobParams(subId, cron), cron, key);
        }
        else
        {
            var key = ScheduleToCronExtension.ScheduleKeyFor("aggregator", subId, schedule);
            await scheduleRepo.ScheduleIfNotExists<AggregationJob, AggregationJobParams>(
                new AggregationJobParams(subId, cron), cron, key);
        }
    }

    private async Task TryUnschedule(SubscriptionType type, int subId, Schedule schedule)
    {
        try
        {
            if (type == SubscriptionType.Receiving)
            {
                var key = ScheduleToCronExtension.ScheduleKeyFor("receiver", subId, schedule);
                await scheduleRepo.UnscheduleJob<ReceivingJob, ReceivingJobParams>(key);
            }
            else
            {
                var key = ScheduleToCronExtension.ScheduleKeyFor("aggregator", subId, schedule);
                await scheduleRepo.UnscheduleJob<AggregationJob, AggregationJobParams>(key);
            }
        }
        catch (SWValidationException)
        {
            // Schedule didn't exist in Quartz — nothing to remove.
        }
    }
}
