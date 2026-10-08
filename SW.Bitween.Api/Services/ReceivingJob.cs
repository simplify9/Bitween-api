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
using SW.Bitween.Services.Adapters;

namespace SW.Bitween;

public record ReceivingJobParams(int SubscriptionId, string? CronExpression);

[ScheduleConfig(AllowConcurrentExecution = false, MisfireInstructions = MisfireInstructions.Skip)]
public class ReceivingJob(
    BitweenDbContext dbContext,
    RunFlagUpdater runFlagUpdater,
    IAdapterInvoker adapterInvoker,
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
            var startupParameters = rec.ReceiverProperties.ToDictionary().Fill(null, globals)
                .WithDataSource(rec.DataSourceId)
                // The receiver's cursor is namespaced by this. Without it, two subscriptions
                // polling one data source share a cursor and split the rows between them.
                .WithSubscription(rec.Id);
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
        // One session for the whole run: Initialize, the listing, every GetFile and DeleteFile, and
        // Finalize all have to reach the SAME instance, or Initialize runs somewhere the listing
        // never sees. A resident receiver is rented from the pool and held for the duration; a
        // classic one is spawned; a native one is just an object. The pipeline cannot tell.
        await using var session = await adapterInvoker.BeginAsync(
            serverlessId, AdapterRole.Receiver, startupParameters);

        await session.InvokeAsync(nameof(IInfolinkReceiver.Initialize));

        // Finalize runs whatever happens in between: it is what closes the connection, and for POP3
        // what commits the deletes of the messages that were taken.
        var failed = new List<string>();
        try
        {
            var fileList = (await session.InvokeAsync<IEnumerable<string>>(nameof(IInfolinkReceiver.ListFiles))).ToList();

            logger.LogInformation("Subscription '{SubId}' found {Count} items for retrieval.", subId, fileList.Count);

            // One file at a time, each on its own. A file that could not be read or submitted used to
            // end the run, and since it stays at the source it ended every run after: one corrupt
            // file at the top of the listing and nothing behind it was ever received. Now it is left
            // where it is, reported, and the rest go through.
            foreach (var file in fileList)
            {
                string exchangeId;
                try
                {
                    var xchangeFile = await session.InvokeAsync<XchangeFile>(nameof(IInfolinkReceiver.GetFile), file);
                    logger.LogInformation("Submitting received file for subscriber: '{SubId}'.", subId);
                    exchangeId = await xchangeService.SubmitSubscriptionXchange(subId, xchangeFile);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Subscription {SubId} could not receive {File}; it is left at the source.", subId, file);
                    failed.Add($"{file}: {ex.Message}");
                    continue;
                }

                createdExchangeIds.Add(exchangeId);
                await session.InvokeAsync(nameof(IInfolinkReceiver.DeleteFile), file);
            }
        }
        finally
        {
            await session.InvokeAsync(nameof(IInfolinkReceiver.Finalize));
        }

        if (failed.Count > 0)
            throw new ReceiverPartlyFailedException(failed);
    }
}

/// <summary>
/// Some items of a run could not be received. Thrown once the rest have been, so the run is
/// recorded as failed with each item's reason while the exchanges it did create are kept.
/// </summary>
public class ReceiverPartlyFailedException(IReadOnlyCollection<string> failures)
    : Exception($"{failures.Count} item(s) could not be received and were left at the source:\n" +
                string.Join("\n", failures));
