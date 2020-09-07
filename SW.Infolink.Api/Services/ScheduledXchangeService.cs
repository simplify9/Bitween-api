using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using SW.EfCoreExtensions;
using SW.Infolink.Domain;
using SW.Infolink.Model;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SW.Infolink
{
    internal class ScheduledXchangeService : IHostedService, IDisposable
    {
        readonly ILogger logger;
        readonly IServiceProvider sp;
        Timer timer;


        public ScheduledXchangeService(IServiceProvider sp, ILogger<ScheduledXchangeService> logger)
        {
            this.sp = sp;
            this.logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            logger.LogInformation("Service is starting.");

            timer = new Timer(async state => await Run(state), null, TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(61));

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            logger.LogInformation("Service is stopping.");

            timer?.Change(Timeout.Infinite, 0);

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            timer?.Dispose();
        }

        public async Task Run(object state)
        {
            try
            {
                using var scope = sp.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<InfolinkDbContext>();

                var xchangeQuery = from xchange in dbContext.Set<Xchange>()
                                   join result in dbContext.Set<XchangeResult>() on xchange.Id equals result.XchangeId
                                   join delivery in dbContext.Set<XchangeDelivery>() on xchange.Id equals delivery.XchangeId into xd
                                   from delivery in xd.DefaultIfEmpty()
                                   where result.Success == true && delivery == null && xchange.DeliverOn != null && xchange.DeliverOn < DateTime.UtcNow
                                   select xchange;

                var xchangeList = await xchangeQuery.ToListAsync();

                if (xchangeList.Count() == 0) return;

                int subId = 0;
                Subscription subscription = null;
                JArray jArray = null;

                foreach (var xchange in xchangeList)
                {
                    if (xchange.SubscriptionId != subId)
                    {
                        if (subId != 0)
                        {
                            //send!
                            if (subscription.Aggregate)
                                await Send(scope, subscription.Id, jArray);
                        }

                        //start with new subscriber
                        subId = xchange.SubscriptionId.Value;
                        jArray = new JArray();
                        subscription = await dbContext.FindAsync<Subscription>(subId);
                    }

                    var xchangeDms = scope.ServiceProvider.GetRequiredService<BlobService>();
                    var xf = JToken.Parse(await xchangeDms.GetFile(xchange.Id, XchangeFileType.Input));

                    if (subscription.Aggregate)
                        //jArray.Add(xf);
                        AddTokenToArray(jArray, xf);
                    else
                        await Send(scope, subscription.Id, xf);

                    dbContext.Add(new XchangeDelivery(xchange.Id));
                }

                if (subscription.Aggregate)
                    await Send(scope, subscription.Id, jArray);

                await dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Service timer callback.");
            }
        }

        private async Task Send(IServiceScope scope, int subscriberId, JToken token)
        {
            //var xchangeService = scope.ServiceProvider.GetService<XchangeService>();
            //await xchangeService.RunSubscriptionXchange(subscriberId, new XchangeFile(jArray.ToString()))
            //await xchangeService.Submit(subscriberId, new XchangeFile(jArray.ToString()), null, true);
        }
        private void AddTokenToArray(JArray jArray, JToken jtoken)
        {
            //if (jArray != null)
            //{
            if (jtoken is JArray)
            {
                foreach (JToken token in jtoken)
                {
                    jArray.Add(token);
                }
            }
            else if (jtoken is JObject)
            {
                jArray.Add(jtoken);
            }
            //}

        }
    }
}
