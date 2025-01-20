using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SW.EfCoreExtensions;
using SW.Bitween.Domain;
using SW.PrimitiveTypes;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SW.Bitween
{
    public class AggregationService : BackgroundService
    {
        readonly ILogger logger;
        readonly IServiceProvider sp;

        public AggregationService(IServiceProvider sp, ILogger<AggregationService> logger)
        {
            this.sp = sp;
            this.logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = sp.CreateScope();
                    var xchangeService = scope.ServiceProvider.GetRequiredService<XchangeService>();
                    var dbContext = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
                    var aggSubs = await dbContext.ListAsync(new DueAggregations());

                    foreach (var aggSub in aggSubs)
                    {
                        try
                        {
                            var xchangeQuery = from xchange in dbContext.Set<Xchange>()
                                join result in dbContext.Set<XchangeResult>() on xchange.Id equals result.Id
                                join agg in dbContext.Set<XchangeAggregation>() on xchange.Id equals agg.Id into xa
                                from agg in xa.DefaultIfEmpty()
                                where result.Success == true && agg == null &&
                                      xchange.SubscriptionId == aggSub.AggregationForId && !aggSub.Inactive
                                select xchange.Id;

                            var targetXchangeList =
                                await xchangeQuery.Take(10000).ToListAsync(cancellationToken: stoppingToken);

                            if (targetXchangeList.Count > 0)
                            {
                                var urlList = targetXchangeList.Select(id =>
                                    xchangeService.GetFileUrl(id, aggSub.AggregationTarget));
                                var xchangeAggregationFile = new XchangeFile(JsonConvert.SerializeObject(urlList));

                                var aggXchange = await xchangeService.CreateXchange(aggSub, xchangeAggregationFile);
                                dbContext.Add(aggXchange);

                                targetXchangeList.ForEach(
                                    id => dbContext.Add(new XchangeAggregation(id, aggXchange.Id)));
                            }

                            aggSub.SetSchedules();
                            aggSub.SetHealth();
                        }
                        catch (Exception ex)
                        {
                            aggSub.SetHealth(ex.ToString());
                            logger.LogError(ex,
                                string.Concat("An error occurred while processing aggregator:", aggSub.Id));
                        }

                        await dbContext.SaveChangesAsync();
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Service timer callback.");
                }


                await Task.Delay(TimeSpan.FromSeconds(61), stoppingToken);
            }
        }
    }
}