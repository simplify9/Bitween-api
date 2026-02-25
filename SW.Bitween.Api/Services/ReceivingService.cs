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
using SW.Bitween.Domain;

namespace SW.Bitween
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

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = sp.CreateScope();
                    var dbContext = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
                    var rcvList = await dbContext.ListAsync(new DueReceivers());

                    var runFlagUpdater = scope.ServiceProvider.GetRequiredService<RunFlagUpdater>();

                    foreach (var rec in rcvList)
                    {
                        try
                        {
                            var isIdle = await runFlagUpdater.MarkAsRunning(rec.Id);
                            if (!isIdle) continue;

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
                        finally
                        {
                            await runFlagUpdater.MarkAsIdle(rec.Id);
                        }

                        await dbContext.SaveChangesAsync(stoppingToken);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Service timer callback.");
                }

                var options = sp.GetService<BitweenOptions>();
                var delay = options.ReceiversDelayInSeconds ?? 60;
                await Task.Delay(TimeSpan.FromSeconds(delay), stoppingToken);
            }
        }

        async Task RunReceiver(IServiceProvider serviceProvider, string serverlessId,
            IDictionary<string, string> startupParameters, int subId)
        {
            // Check if it's a native adapter
            if (serverlessId.StartsWith("native.", StringComparison.OrdinalIgnoreCase))
            {
                var nativeAdapterDiscovery = serviceProvider.GetRequiredService<NativeAdapterDiscoveryService>();
                var receiver = InstantiateNativeReceiver(nativeAdapterDiscovery, serverlessId, startupParameters);
                
                await receiver.Initialize();
                var fileList = (await receiver.ListFiles()).ToList();

                logger.LogInformation($"Subscription:'{subId}' found {fileList.Count()} items for retrieval.");

                foreach (var file in fileList)
                {
                    var xchangeFile = await receiver.GetFile(file);

                    logger.LogInformation($"Submitting received file for subscriber: '{subId}'.");

                    var xchangeService = serviceProvider.GetService<XchangeService>();
                    await xchangeService.SubmitSubscriptionXchange(subId, xchangeFile);
                    await receiver.DeleteFile(file);
                }

                await receiver.Finalize();
            }
            else
            {
                // Use serverless for external adapters
                var serverless = serviceProvider.GetRequiredService<IServerlessService>();
                await serverless.StartAsync(serverlessId, null, startupParameters);
                await serverless.InvokeAsync(nameof(IInfolinkReceiver.Initialize), null);
                var fileList =
                    (await serverless.InvokeAsync<IEnumerable<string>>(nameof(IInfolinkReceiver.ListFiles), null)).ToList();

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

        private IInfolinkReceiver InstantiateNativeReceiver(NativeAdapterDiscoveryService nativeAdapterDiscovery, 
            string adapterId, IDictionary<string, string> properties)
        {
            var adapterInfo = nativeAdapterDiscovery.GetNativeAdapterInfo(adapterId);
            if (adapterInfo == null)
                throw new BitweenException($"Native adapter not found: {adapterId}");

            // Get the constructor that takes a parameter
            var constructor = adapterInfo.Type.GetConstructors()
                .FirstOrDefault(c => c.GetParameters().Length > 0);

            if (constructor == null)
                throw new BitweenException($"Native adapter {adapterId} must have a constructor that accepts an input model");

            // Get the input parameter type
            var inputParameter = constructor.GetParameters().First();
            var inputType = inputParameter.ParameterType;

            // Create an instance of the input model by mapping properties
            var inputInstance = Activator.CreateInstance(inputType);

            // Map dictionary properties to the input model
            foreach (var prop in inputType.GetProperties())
            {
                // Case-insensitive property lookup
                var propEntry = properties.FirstOrDefault(p => 
                    string.Equals(p.Key, prop.Name, StringComparison.OrdinalIgnoreCase));
                
                if (!string.IsNullOrEmpty(propEntry.Key))
                {
                    var value = propEntry.Value;
                    try
                    {
                        var convertedValue = Convert.ChangeType(value, 
                            Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType);
                        prop.SetValue(inputInstance, convertedValue);
                    }
                    catch
                    {
                        // If conversion fails, set string value directly
                        if (prop.PropertyType == typeof(string))
                            prop.SetValue(inputInstance, value);
                    }
                }
            }

            // Instantiate the adapter with the input model
            var adapter = Activator.CreateInstance(adapterInfo.Type, inputInstance);
            
            return (IInfolinkReceiver)adapter;
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
        //        var dbContext = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
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