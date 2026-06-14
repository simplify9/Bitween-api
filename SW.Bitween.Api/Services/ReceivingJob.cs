using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SW.Bitween.Domain;
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

        try
        {
            var startupParameters = rec.ReceiverProperties.ToDictionary();
            await RunReceiver(rec.ReceiverId, startupParameters, rec.Id);
            rec.SetSchedules();
            rec.SetHealth();
        }
        catch (Exception ex)
        {
            rec.SetHealth(ex.ToString());
            logger.LogError(ex, "Error processing receiver for subscription {SubscriptionId}", jobParams.SubscriptionId);
        }
        finally
        {
            await runFlagUpdater.MarkAsIdle(rec.Id);
        }

        await dbContext.SaveChangesAsync();
    }

    private async Task RunReceiver(string serverlessId, IDictionary<string, string> startupParameters, int subId)
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
                await xchangeService.SubmitSubscriptionXchange(subId, xchangeFile);
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
                await xchangeService.SubmitSubscriptionXchange(subId, xchangeFile);
                await serverless.InvokeAsync(nameof(IInfolinkReceiver.DeleteFile), file);
            }

            await serverless.InvokeAsync(nameof(IInfolinkReceiver.Finalize), null);
        }
    }
}
