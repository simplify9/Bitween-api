using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SW.EfCoreExtensions;
using SW.Infolink.Api;
using SW.Infolink.Domain;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SW.Infolink
{
    internal class ScheduledReceiverService : IHostedService, IDisposable
    {

        readonly ILogger logger;
        readonly IServiceProvider sp;
        Timer timer;


        public ScheduledReceiverService(IServiceProvider sp, ILogger<ScheduledReceiverService> logger)
        {
            this.sp = sp;
            this.logger = logger;
        }

        public void Dispose()
        {
            timer?.Dispose();
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            logger.LogInformation("Service is starting.");

            timer = new Timer(async o => await Run(o), null, TimeSpan.FromSeconds(3),
                TimeSpan.FromSeconds(63));

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            logger.LogInformation("Service is stopping.");

            timer?.Change(Timeout.Infinite, 0);

            return Task.CompletedTask;
        }

        public async Task Run(object state)
        {
            try
            {
                var rd = DateTime.UtcNow;

                if (state != null) rd = (DateTime)state;

                using (var scope = sp.CreateScope())
                {

                    var dbContext = scope.ServiceProvider.GetRequiredService<InfolinkDbContext>();

                    var rcvList = await dbContext.ListAsync(new DueReceivers(rd));

                    if (rcvList.Count() == 0) return;

                    foreach (var rec in rcvList)
                    {
                        try
                        {

                            logger.LogInformation($"Starting to receive for subscriber: '{rec.Id}', adapter: '{rec.ReceiverId}'.");

                            //var sub = await dbContext.FindAsync<Subscription>(rec.Id);
                            //var adapter = await dbContext.FindAsync<Adapter>(rec.ReceiverId);
                            //var adapterSvc = scope.ServiceProvider.GetRequiredService<AdapterService>();
                            //var adapterpath = await adapterSvc.Install(rec.ReceiverId);

                            var startupParameters = rec.ReceiverProperties.ToDictionary();
                            //foreach (var subprop in sub.Properties)
                            //{
                            //    var subvValue = subprop.Value;
                            //    if (!startupParameters.TryGetValue(subprop.Key,out subvValue))
                            //    {                                    
                            //        startupParameters.Add(subprop.Key, subprop.Value);
                            //    }
                            //}
                                

                            //var rreq = new ReceiverRequest
                            //{
                            //    Receiver = rec.ToReceiverDto(),
                            //    Subscriber = sub.ToSubscriberDto()
                            //};


                            if (!string.IsNullOrWhiteSpace(rec.ReceiverId))
                            {
                                await RunReceiver(rec.ReceiverId, startupParameters , rec.Id);
                                rec.SetReceiveSchedules(); // = rec.Schedules.Next();
                                await dbContext.SaveChangesAsync();
                            }

                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, string.Concat("An error occurred while processing receiver:", rec.Id));
                        }

                    }

                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Service timer callback.");
            }
        }

        async Task RunReceiver(string serverlessId, IDictionary<string, string> startupParameters, int subscriberId)
        {

            var serverless = sp.GetRequiredService<IServerlessService>();
            await serverless.StartAsync(serverlessId, startupParameters);

            await serverless.InvokeAsync(nameof(IInfolinkReceiver.Initialize), null);

            var fileList = await serverless.InvokeAsync<string[]>(nameof(IInfolinkReceiver.ListFiles), null);

            logger.LogInformation($"Subscriber: '{subscriberId}' has {fileList.Length} file(s) to be received.");


            foreach (var file in fileList)
            {
                var xchangeFile = await serverless.InvokeAsync<XchangeFile>(nameof(IInfolinkReceiver.GetFile), file);
                using (var scope = sp.CreateScope())
                {
                    logger.LogInformation($"Submitting received file for subscriber: '{subscriberId}'.");
                    var xchangeService = scope.ServiceProvider.GetService<XchangeService>();

                    //await xchangeService.Submit(
                    //    subscriberId,
                    //    new XchangeFile(xchangeFile.Data, xchangeFile.Filename),
                    //    null);
                    await xchangeService.SubmitSubscriptionXchange(subscriberId, xchangeFile);
                }

                await serverless.InvokeAsync(nameof(IInfolinkReceiver.DeleteFile), file);
            }

            await serverless.InvokeAsync(nameof(IInfolinkReceiver.Finalize), null);
        }
    }

}
