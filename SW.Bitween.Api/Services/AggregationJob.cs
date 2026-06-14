using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SW.Bitween.Domain;
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
            }

            aggSub.SetSchedules();
            aggSub.SetHealth();
        }
        catch (Exception ex)
        {
            aggSub.SetHealth(ex.ToString());
            logger.LogError(ex, "Error processing aggregation for subscription {SubscriptionId}", jobParams.SubscriptionId);
        }

        await dbContext.SaveChangesAsync();
    }
}
