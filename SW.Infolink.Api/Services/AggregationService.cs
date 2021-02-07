using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SW.EfCoreExtensions;
using SW.Infolink.Domain;
using SW.PrimitiveTypes;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SW.Infolink
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

        async protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {

                try
                {
                    using var scope = sp.CreateScope();
                    var xchangeService = scope.ServiceProvider.GetRequiredService<XchangeService>();
                    var dbContext = scope.ServiceProvider.GetRequiredService<InfolinkDbContext>();
                    var aggSubs = await dbContext.ListAsync(new DueAggregations());

                    foreach (var aggSub in aggSubs)
                    {
                        try
                        {
                            var xchangeQuery = from xchange in dbContext.Set<Xchange>()
                                               join result in dbContext.Set<XchangeResult>() on xchange.Id equals result.Id
                                               join agg in dbContext.Set<XchangeAggregation>() on xchange.Id equals agg.Id into xa
                                               from agg in xa.DefaultIfEmpty()
                                               where result.Success == true && agg == null && xchange.SubscriptionId == aggSub.AggregationForId && !aggSub.Inactive
                                               select xchange.Id;

                            var targetXchangeList = await xchangeQuery.Take(10000).ToListAsync();

                            if (targetXchangeList.Count > 0)
                            {
                                var urlList = targetXchangeList.Select(id => xchangeService.GetFileUrl(id, aggSub.AggregationTarget));
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
                            logger.LogError(ex, string.Concat("An error occurred while processing aggregator:", aggSub.Id));
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

        //public Task StartAsync(CancellationToken cancellationToken)
        //{
        //    logger.LogInformation("Service is starting.");

        //    timer = new Timer(async state => await Run(state), null, TimeSpan.FromSeconds(5),
        //        TimeSpan.FromMinutes(1));

        //    return Task.CompletedTask;
        //}

        //public Task StopAsync(CancellationToken cancellationToken)
        //{
        //    logger.LogInformation("Service is stopping.");

        //    timer?.Change(Timeout.Infinite, 0);

        //    return Task.CompletedTask;
        //}

        //public void Dispose()
        //{
        //    timer?.Dispose();
        //}

        //public async Task Run(object state)
        //{
        //    try
        //    {
        //        using var scope = sp.CreateScope();
        //        var xchangeService = scope.ServiceProvider.GetRequiredService<XchangeService>();
        //        var dbContext = scope.ServiceProvider.GetRequiredService<InfolinkDbContext>();
        //        var aggSubs = await dbContext.ListAsync(new DueAggregations());

        //        foreach (var aggSub in aggSubs)
        //        {
        //            try
        //            {
        //                var xchangeQuery = from xchange in dbContext.Set<Xchange>()
        //                                   join result in dbContext.Set<XchangeResult>() on xchange.Id equals result.Id
        //                                   join agg in dbContext.Set<XchangeAggregation>() on xchange.Id equals agg.Id into xa
        //                                   from agg in xa.DefaultIfEmpty()
        //                                   where result.Success == true && agg == null && xchange.SubscriptionId == aggSub.AggregationForId && !aggSub.Inactive
        //                                   select xchange.Id;

        //                var targetXchangeList = await xchangeQuery.Take(10000).ToListAsync();

        //                if (targetXchangeList.Count > 0)
        //                {
        //                    var urlList = targetXchangeList.Select(id => xchangeService.GetFileUrl(id, aggSub.AggregationTarget));
        //                    var xchangeAggregationFile = new XchangeFile(JsonConvert.SerializeObject(urlList));

        //                    var aggXchange = await xchangeService.CreateXchange(aggSub, xchangeAggregationFile);
        //                    dbContext.Add(aggXchange);

        //                    targetXchangeList.ForEach(id => dbContext.Add(new XchangeAggregation(id, aggXchange.Id)));
        //                }
        //                aggSub.SetSchedules();
        //                aggSub.SetHealth();
        //            }
        //            catch (Exception ex)
        //            {
        //                aggSub.SetHealth(ex.ToString());
        //                logger.LogError(ex, string.Concat("An error occurred while processing aggregator:", aggSub.Id));
        //            }
        //            await dbContext.SaveChangesAsync();
        //        }

        //    }
        //    catch (Exception ex)
        //    {
        //        logger.LogError(ex, "Service timer callback.");
        //    }
        //}



        //private async Task Send(XchangeService xchangeService, int subscriberId, JToken token)
        //{
        //    //await xchangeService.RunSubscriptionXchange(subscriberId, new XchangeFile(jArray.ToString()))
        //    //await xchangeService.Submit(subscriberId, new XchangeFile(jArray.ToString()), null, true);
        //}
        //private void AddTokenToArray(JArray jArray, JToken jtoken)
        //{
        //    //if (jArray != null)
        //    //{
        //    if (jtoken is JArray)
        //    {
        //        foreach (JToken token in jtoken)
        //        {
        //            jArray.Add(token);
        //        }
        //    }
        //    else if (jtoken is JObject)
        //    {
        //        jArray.Add(jtoken);
        //    }
        //    //}

        //}
    }
}

//int subId = 0;
//Subscription subscription = null;
//JArray jArray = null;

//foreach (var xchange in xchangeList)
//{
//    if (xchange.SubscriptionId != subId)
//    {
//        if (subId != 0)
//        {
//            //send!
//            //if (subscription.Aggregate)
//            await Send(xchangeService, subscription.Id, jArray);
//        }

//        //start with new subscriber
//        subId = xchange.SubscriptionId.Value;
//        jArray = new JArray();
//        subscription = await dbContext.FindAsync<Subscription>(subId);
//    }

//    //var xchangeDms = scope.ServiceProvider.GetRequiredService<BlobService>();
//    var xf = JToken.Parse(await xchangeService.GetFile(xchange.Id, XchangeFileType.Input));

//    //if (subscription.Aggregate)
//    //jArray.Add(xf);
//    AddTokenToArray(jArray, xf);
//    //else
//    //    await Send(xchangeService, subscription.Id, xf);

//    dbContext.Add(new XchangeDelivery(xchange.Id));
//}

//                }

//                //if (subscription.Aggregate)
//                await Send(xchangeService, subscription.Id, jArray);

//await dbContext.SaveChangesAsync();
