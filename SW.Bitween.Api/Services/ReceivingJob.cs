using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using SW.Scheduler;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SW.Bitween;

public record ReceivingJobParams(int SubscriptionId, string? CronExpression);

[ScheduleConfig(AllowConcurrentExecution = false, MisfireInstructions = MisfireInstructions.Skip)]
public class ReceivingJob(
    BitweenDbContext dbContext,
    RunFlagUpdater runFlagUpdater,
    NativeAdapterDiscoveryService nativeAdapterDiscovery,
    IServerlessService serverless,
    XchangeService xchangeService,
    ILogger<ReceivingJob> logger) : IScheduledJob<ReceivingJobParams>
{
    public async Task Execute(ReceivingJobParams jobParams)
    {
        var rec = await dbContext.Set<Subscription>()
            .FirstOrDefaultAsync(s => s.Id == jobParams.SubscriptionId && !s.Inactive);

        if (rec == null) return;

        // Atomic DB-level guard: returns false if another execution is already running.
        var isIdle = await runFlagUpdater.MarkAsRunning(rec.Id);
        if (!isIdle) return;

        var startedOn = DateTime.UtcNow;
        // Populated as files come in, so a mid-loop failure still leaves the ones that
        // did make it through visible on the attempt record rather than orphaned.
        var createdExchangeIds = new List<string>();

        // Advances regardless of outcome: the Quartz trigger fires on its own cron no
        // matter what happens below, so "next run" has to track that, not the receive
        // step's success — otherwise a receiver that keeps failing freezes ReceiveOn
        // in the past forever while the job keeps firing on schedule underneath it.
        // Isolated in its own try: a schedule problem is unrelated to receiving and
        // must not stop the step below from running.
        try
        {
            rec.SetSchedules();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not advance the schedule for subscription {SubscriptionId}", jobParams.SubscriptionId);
        }

        try
        {
            var globals = await dbContext.Set<GlobalAdapterValuesSet>().ToArrayAsync();
            var startupParameters = rec.ReceiverProperties.ToDictionary().Fill(null, globals);
            await RunReceiver(rec.ReceiverId, startupParameters, rec.Id, createdExchangeIds);
            rec.SetHealth();
            RecordAttempt(rec.Id, startedOn,
                createdExchangeIds.Count > 0 ? ReceiveOutcome.Received : ReceiveOutcome.NoNewData,
                null, createdExchangeIds);
        }
        catch (Exception ex)
        {
            rec.SetHealth(ex.ToString());
            RecordAttempt(rec.Id, startedOn, ReceiveOutcome.Failed, ex.ToString(), createdExchangeIds);
            logger.LogError(ex, "Error processing receiver for subscription {SubscriptionId}", jobParams.SubscriptionId);
        }
        finally
        {
            await runFlagUpdater.MarkAsIdle(rec.Id);
        }

        await dbContext.SaveChangesAsync();
    }

    private void RecordAttempt(
        int subscriptionId, DateTime startedOn, ReceiveOutcome outcome, string errorMessage,
        List<string> exchangeIds)
    {
        dbContext.Add(new ReceiveAttempt
        {
            SubscriptionId = subscriptionId,
            StartedOn = startedOn,
            FinishedOn = DateTime.UtcNow,
            Outcome = outcome,
            ErrorMessage = errorMessage,
            ExchangeIds = exchangeIds.ToArray(),
        });
    }

    private async Task RunReceiver(
        string serverlessId, IDictionary<string, string> startupParameters, int subId,
        List<string> createdExchangeIds)
    {
        if (serverlessId.StartsWith(NativeAdapterDiscoveryService.NativePrefix, StringComparison.OrdinalIgnoreCase))
        {
            var receiver = nativeAdapterDiscovery.GetNativeReceiver(serverlessId, startupParameters);
            await receiver.Initialize();
            var fileList = (await receiver.ListFiles()).ToList();

            logger.LogInformation("Subscription '{SubId}' found {Count} items for retrieval.", subId, fileList.Count);

            foreach (var file in fileList)
            {
                var xchangeFile = await receiver.GetFile(file);
                logger.LogInformation("Submitting received file for subscriber: '{SubId}'.", subId);
                createdExchangeIds.Add(await xchangeService.SubmitSubscriptionXchange(subId, xchangeFile));
                await receiver.DeleteFile(file);
            }

            await receiver.Finalize();
        }
        else
        {
            await serverless.StartAsync(serverlessId, null, startupParameters);
            await serverless.InvokeAsync(nameof(IInfolinkReceiver.Initialize), null);
            var fileList = (await serverless.InvokeAsync<IEnumerable<string>>(nameof(IInfolinkReceiver.ListFiles), null)).ToList();

            logger.LogInformation("Subscription '{SubId}' found {Count} items for retrieval.", subId, fileList.Count);

            foreach (var file in fileList)
            {
                var xchangeFile = await serverless.InvokeAsync<XchangeFile>(nameof(IInfolinkReceiver.GetFile), file);
                logger.LogInformation("Submitting received file for subscriber: '{SubId}'.", subId);
                createdExchangeIds.Add(await xchangeService.SubmitSubscriptionXchange(subId, xchangeFile));
                await serverless.InvokeAsync(nameof(IInfolinkReceiver.DeleteFile), file);
            }

            await serverless.InvokeAsync(nameof(IInfolinkReceiver.Finalize), null);
        }
    }
}
