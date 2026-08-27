using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.Scheduler;
using System;
using System.Linq;
using System.Threading.Tasks;
using SW.PrimitiveTypes;

namespace SW.Bitween;

public record AggregationJobParams(int SubscriptionId, string? CronExpression);

[ScheduleConfig(AllowConcurrentExecution = false, MisfireInstructions = MisfireInstructions.Skip)]
public class AggregationJob(
    BitweenDbContext dbContext,
    XchangeService xchangeService,
    ILogger<AggregationJob> logger) : IScheduledJob<AggregationJobParams>
{
    public async Task Execute(AggregationJobParams jobParams)
    {
        var aggSub = await dbContext.Set<Subscription>()
            .FirstOrDefaultAsync(s => s.Id == jobParams.SubscriptionId && !s.Inactive);

        if (aggSub == null) return;

        var startedOn = DateTime.UtcNow;
        // Decided as the run goes and written once at the end. Recording inside both the try
        // and the catch wrote two rows for a single run whenever something threw after the
        // roll-up was already built — SetSchedules does exactly that on an aggregation that
        // somehow has no schedule.
        var outcome = ReceiveOutcome.NoNewData;
        string errorMessage = null;
        string[] createdExchangeIds = [];

        try
        {
            var xchangeQuery =
                from xchange in dbContext.Set<Xchange>()
                join result in dbContext.Set<XchangeResult>() on xchange.Id equals result.Id
                join agg in dbContext.Set<XchangeAggregation>() on xchange.Id equals agg.Id into xa
                from agg in xa.DefaultIfEmpty()
                where result.Success == true && agg == null &&
                      xchange.SubscriptionId == aggSub.AggregationForId && !aggSub.Inactive
                select xchange.Id;

            var targetXchangeList = await xchangeQuery.Take(10000).ToListAsync();

            if (targetXchangeList.Count > 0)
            {
                var urlList = targetXchangeList.Select(id =>
                    xchangeService.GetFileUrl(id, aggSub.AggregationTarget));
                var xchangeAggregationFile = new XchangeFile(JsonConvert.SerializeObject(urlList));
                var aggXchange = await xchangeService.CreateXchange(aggSub, xchangeAggregationFile);
                dbContext.Add(aggXchange);
                targetXchangeList.ForEach(id => dbContext.Add(new XchangeAggregation(id, aggXchange.Id)));
                outcome = ReceiveOutcome.Received;
                createdExchangeIds = [aggXchange.Id];
            }
            // Otherwise the outcome stays NoNewData: nothing outstanding is an ordinary, quiet
            // result, the same thing a receiver reports when the folder it polls is empty.

            aggSub.SetSchedules();
            aggSub.SetHealth();
        }
        catch (Exception ex)
        {
            aggSub.SetHealth(ex.ToString());
            errorMessage = ex.ToString();
            // A run that already built its roll-up did the thing it exists to do; a throw after
            // that is bookkeeping, and calling the run failed would misreport delivered work.
            // The error is kept on the attempt either way, and on the integration's health.
            if (createdExchangeIds.Length == 0) outcome = ReceiveOutcome.Failed;
            logger.LogError(ex, "Error processing aggregation for subscription {SubscriptionId}", jobParams.SubscriptionId);
        }

        RecordAttempt(aggSub.Id, startedOn, outcome, errorMessage, createdExchangeIds);

        await dbContext.SaveChangesAsync();
    }

    /// <summary>
    /// Writes the run into the same history a receiver's runs go into, which is what lets one
    /// table on the integration page show a run beside the exchange it produced. The entity is
    /// named for the job that came first; nothing on it is specific to receiving.
    /// </summary>
    private void RecordAttempt(int subscriptionId, DateTime startedOn, ReceiveOutcome outcome,
        string errorMessage, string[] exchangeIds)
    {
        dbContext.Add(new ReceiveAttempt
        {
            SubscriptionId = subscriptionId,
            StartedOn = startedOn,
            FinishedOn = DateTime.UtcNow,
            Outcome = outcome,
            ErrorMessage = errorMessage,
            ExchangeIds = exchangeIds,
        });
    }
}
