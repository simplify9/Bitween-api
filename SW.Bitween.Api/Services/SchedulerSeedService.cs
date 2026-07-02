using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.Scheduler;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SW.Bitween;

// Registers all active Receiving and Aggregation subscriptions with Quartz on startup.
// Uses ScheduleIfNotExists so restarts are idempotent against a persistent Quartz store.
public class SchedulerSeedService(IServiceProvider sp, ILogger<SchedulerSeedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var scope = sp.CreateScope();
        var dbContext      = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var subScheduler   = scope.ServiceProvider.GetRequiredService<SubscriptionSchedulerService>();
        var scheduleRepo   = scope.ServiceProvider.GetRequiredService<IScheduleRepository>();
        var options        = scope.ServiceProvider.GetRequiredService<BitweenOptions>();

        await scheduleRepo.Schedule<RetryJob>(options.RetryJobCron);

        var subscriptions = await dbContext.Set<Subscription>()
            .Where(s =>
                (s.Type == SubscriptionType.Receiving || s.Type == SubscriptionType.Aggregation) &&
                !s.Inactive &&
                s.Schedules.Any())
            .ToListAsync(stoppingToken);

        foreach (var sub in subscriptions)
        {
            try
            {
                await subScheduler.ScheduleAll(sub);
                logger.LogInformation(
                    "Seeded Quartz schedules for subscription {Id} ({Type})", sub.Id, sub.Type);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Failed to seed Quartz schedule for subscription {Id}", sub.Id);
            }
        }
    }
}
