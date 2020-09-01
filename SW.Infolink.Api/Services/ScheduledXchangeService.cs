using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using SW.EfCoreExtensions;
using SW.Infolink.Domain;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SW.Infolink
{
    public class ScheduledXchangeService : IHostedService, IDisposable
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

            timer = new Timer(async o => await Run(o), null, TimeSpan.FromSeconds(5),
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
                var rd = DateTime.UtcNow;

                if (state != null) rd = (DateTime)state;

                using (var scope = sp.CreateScope())
                {

                    var repo = scope.ServiceProvider.GetRequiredService<InfolinkDbContext>();

                    var xchangeList = (await repo.ListAsync(new XchangesDueForDelivery(rd))).OrderBy(e => e.SubscriberId);
                    
                    if (xchangeList.Count() == 0) return;

                    int subId = 0;
                    Subscriber subscriber = null;
                    JArray jArray = null;

                    foreach (var xchange in xchangeList)
                    {
                        if (xchange.SubscriberId != subId)
                        {
                            if (subId != 0)
                            {
                                //send!
                                if (subscriber.Aggregate)
                                    await Send(scope, subscriber.Id, jArray);
                            }

                            //start with new subscriber
                            subId = xchange.SubscriberId;
                            jArray = new JArray();
                            subscriber = await repo.FindAsync<Subscriber>(subId);
                        }

                        var xchangeDms = scope.ServiceProvider.GetRequiredService<BlobService>();
                        JToken xf = JToken.Parse(await xchangeDms.GetFile(xchange.Id, XchangeFileType.Input));

                        if (subscriber.Aggregate)
                            //jArray.Add(xf);
                            AddTokensToArray(jArray, xf);
                        else
                            await Send(scope, subscriber.Id, xf);

                        xchange.DeliveredOn = DateTime.UtcNow;
                        await repo.SaveChangesAsync();

                    }

                    if (subscriber.Aggregate)
                        await Send(scope, subscriber.Id, jArray);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Service timer callback.");
            }
        }

        private async Task Send(IServiceScope scope, int subscriberId, object jArray)
        {
            var xchangeService = scope.ServiceProvider.GetService<XchangeService>();
            await xchangeService.Submit(subscriberId, new XchangeFile(jArray.ToString()), null, true);
        }
        private void AddTokensToArray(JArray jArray, JToken jtoken)
        {
            if (jArray != null)
            {
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
            }

        }
    }
}
