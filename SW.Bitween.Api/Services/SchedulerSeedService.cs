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

        // Two Quartz job-store writes back to back can race Npgsql's connection-pool cleanup:
        // the second call grabs a connection still left in an aborted-transaction state from
        // the first, failing with "25P02: current transaction is aborted" while obtaining its
        // row lock. A fixed delay between the two calls was tried and proved unreliable (still
        // reproduced on a fresh DB with the delay in place) — retrying the specific lock failure
        // is correct instead, since it naturally waits however long is actually needed rather
        // than guessing, and still fails loudly if the lock truly can't be obtained after 3 tries.
        await ScheduleWithRetry(() => scheduleRepo.Schedule<RetryJob>(options.RetryJobCron), nameof(RetryJob), stoppingToken);
        await ScheduleWithRetry(() => scheduleRepo.Schedule<ReceiveAttemptCleanupJob>(options.ReceiveAttemptCleanupCron), nameof(ReceiveAttemptCleanupJob), stoppingToken);

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

    private async Task ScheduleWithRetry(Func<Task> schedule, string jobName, CancellationToken ct)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await schedule();
                return;
            }
            catch (Quartz.Impl.AdoJobStore.LockException) when (attempt < maxAttempts)
            {
                logger.LogWarning(
                    "Transient lock error seeding {Job} (attempt {Attempt}/{Max}) — retrying",
                    jobName, attempt, maxAttempts);
                await Task.Delay(200 * attempt, ct);
            }
        }
    }
}
