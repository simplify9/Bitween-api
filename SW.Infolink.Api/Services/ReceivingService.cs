using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SW.EfCoreExtensions;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SW.Infolink
{
    public class ReceivingService : BackgroundService
    {
        readonly ILogger logger;
        readonly IServiceProvider sp;


        public ReceivingService(IServiceProvider sp, ILogger<ReceivingService> logger)
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
                    var dbContext = scope.ServiceProvider.GetRequiredService<InfolinkDbContext>();
                    var rcvList = await dbContext.ListAsync(new DueReceivers());

                    foreach (var rec in rcvList)
                    {
                        try
                        {
                            var startupParameters = rec.ReceiverProperties.ToDictionary();
                            await RunReceiver(scope.ServiceProvider, rec.ReceiverId, startupParameters, rec.Id);
                            rec.SetSchedules();
                            rec.SetHealth();
                        }
                        catch (Exception ex)
                        {
                            rec.SetHealth(ex.ToString());
                            logger.LogError(ex, string.Concat("An error occurred while processing receiver:", rec.Id));
                        }
                        await dbContext.SaveChangesAsync();
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Service timer callback.");
                }

                var options = sp.GetService<InfolinkOptions>();
                var delay = options.ReceiversDelayInSeconds ?? 60;
                await Task.Delay(TimeSpan.FromSeconds(delay), stoppingToken);
            }
        }

        async Task RunReceiver(IServiceProvider serviceProvider, string serverlessId, IDictionary<string, string> startupParameters, int subId)
        {
            var serverless = serviceProvider.GetRequiredService<IServerlessService>();
            await serverless.StartAsync(serverlessId, null, startupParameters);
            await serverless.InvokeAsync(nameof(IInfolinkReceiver.Initialize), null);
            var fileList = await serverless.InvokeAsync<IEnumerable<string>>(nameof(IInfolinkReceiver.ListFiles), null);

            logger.LogInformation($"Subscription:'{subId}' found {fileList.Count()} items for retrieval.");

            foreach (var file in fileList)
            {
                var xchangeFile = await serverless.InvokeAsync<XchangeFile>(nameof(IInfolinkReceiver.GetFile), file);

                logger.LogInformation($"Submitting received file for subscriber: '{subId}'.");

                var xchangeService = serviceProvider.GetService<XchangeService>();
                await xchangeService.SubmitSubscriptionXchange(subId, xchangeFile);
                await serverless.InvokeAsync(nameof(IInfolinkReceiver.DeleteFile), file);
            }
            await serverless.InvokeAsync(nameof(IInfolinkReceiver.Finalize), null);
        }


        //public void Dispose()
        //{
        //    timer?.Dispose();
        //}

        //public Task StartAsync(CancellationToken cancellationToken)
        //{
        //    logger.LogInformation("Service is starting.");

        //    timer = new Timer(async state => await Run(state), null, TimeSpan.FromSeconds(5),
        //        TimeSpan.FromSeconds(63));

        //    return Task.CompletedTask;
        //}

        //public Task StopAsync(CancellationToken cancellationToken)
        //{
        //    logger.LogInformation("Service is stopping.");
        //    timer?.Change(Timeout.Infinite, 0);
        //    return Task.CompletedTask;
        //}

        //public async Task Run(object state)
        //{
        //    try
        //    {
        //        using var scope = sp.CreateScope();
        //        var dbContext = scope.ServiceProvider.GetRequiredService<InfolinkDbContext>();
        //        var rcvList = await dbContext.ListAsync(new DueReceivers());

        //        foreach (var rec in rcvList)
        //        {
        //            try
        //            {
        //                var startupParameters = rec.ReceiverProperties.ToDictionary();
        //                await RunReceiver(scope.ServiceProvider, rec.ReceiverId, startupParameters, rec.Id);
        //                rec.SetSchedules();
        //                rec.SetHealth();
        //            }
        //            catch (Exception ex)
        //            {
        //                rec.SetHealth(ex.ToString());
        //                logger.LogError(ex, string.Concat("An error occurred while processing receiver:", rec.Id));
        //            }
        //            await dbContext.SaveChangesAsync();
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        logger.LogError(ex, "Service timer callback.");
        //    }
        //}



    }

}
