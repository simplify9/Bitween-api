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

            timer = new Timer(async state => await Run(state), null, TimeSpan.FromSeconds(5),
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
                using var scope = sp.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<InfolinkDbContext>();
                var rcvList = await dbContext.ListAsync(new DueReceivers(DateTime.UtcNow));

                foreach (var rec in rcvList)
                {
                    try
                    {
                        //logger.LogInformation($"Starting to receive for subscriber: '{rec.Id}', adapter: '{rec.ReceiverId}'.");
                        var startupParameters = rec.ReceiverProperties.ToDictionary();
                        await RunReceiver(scope.ServiceProvider, rec.ReceiverId, startupParameters, rec.Id);
                        rec.SetReceiveSchedules();
                        rec.SetReceiveResult();
                    }
                    catch (Exception ex)
                    {
                        rec.SetReceiveResult(ex.ToString());
                        logger.LogError(ex, string.Concat("An error occurred while processing receiver:", rec.Id));
                    }
                    await dbContext.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Service timer callback.");
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
    }

}
