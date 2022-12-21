using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SW.Infolink.Api;
using SW.Infolink.Domain;
using SW.Infolink.Model;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace SW.Infolink
{
    public class XchangeService :
        IConsume<ApiXchangeCreatedEvent>,
        IConsume<InternalXchangeCreatedEvent>,
        IConsume<AggregateXchangeCreatedEvent>,
        IConsume<ReceivingXchangeCreatedEvent>,
        IConsume<XchangeResultCreatedEvent>,
        IConsume<SubscriptionUnpausedEvent>

    {
        private readonly InfolinkOptions infolinkSettings;
        private readonly InfolinkDbContext dbContext;
        private readonly FilterService filterService;
        private readonly ICloudFilesService cloudFiles;
        private readonly IServiceProvider serviceProvider;
        private readonly IPublish publish;
        private readonly ILogger logger;

        public XchangeService(InfolinkOptions infolinkSettings, InfolinkDbContext dbContext,
            FilterService filterService,
            ICloudFilesService cloudFiles, IServiceProvider serviceProvider,
            IPublish publish, ILogger<XchangeService> logger)
        {
            this.infolinkSettings = infolinkSettings;
            this.dbContext = dbContext;
            this.filterService = filterService;
            this.cloudFiles = cloudFiles;
            this.serviceProvider = serviceProvider;
            this.publish = publish;
            this.logger = logger;
        }

        async public Task<string> SubmitSubscriptionXchange(int subscriptionId, XchangeFile file,
            string[] references = null)
        {
            //var subscription = await dbContext.FindAsync<Subscription>(subscriptionId);
            var subscription = await dbContext.Set<Subscription>()
                .Where(e => e.Id == subscriptionId)
                .AsNoTracking()
                .FirstOrDefaultAsync();

            var xchange = await CreateXchange(subscription, file, references, Guid.NewGuid().ToString("N"));
            await dbContext.SaveChangesAsync();
            return xchange.Id;
        }

        async public Task<string> SubmitFilterXchange(int documentId, XchangeFile file, string[] references = null,
            string correlationId = null)
        {
            var document = await dbContext.FindAsync<Document>(documentId);
            Xchange xchange;
            
            if (infolinkSettings.DisregardsUnfilteredMessages ?? false)
            {
                xchange = new Xchange(documentId, file, references, SubscriptionType.Internal, correlationId);
                var result = filterService.Filter(xchange.DocumentId, file,
                    document?.DocumentFormat ?? DocumentFormat.Json);
                await CreateXchangesForHits(xchange, result, file);
            }
            else
            {
                xchange = await CreateXchange(document, file, references, correlationId);
                await dbContext.SaveChangesAsync();
            }

            return xchange.Id;
        }

        public async Task<Xchange> CreateXchange(Xchange xchange, XchangeFile file)
        {
            var newXchange = new Xchange(xchange, file);
            await AddFile(newXchange.Id, XchangeFileType.Input, file);
            dbContext.Add(newXchange);
            return xchange;
        }

        public async Task<Xchange> CreateXchange(Subscription subscription, Xchange xchange, XchangeFile file,
            string[] references = null)
        {
            var newXchange = new Xchange(subscription, xchange, file);
            await AddFile(newXchange.Id, XchangeFileType.Input, file);
            dbContext.Add(newXchange);
            return xchange;
        }

        public async Task<Xchange> CreateXchange(Document document, XchangeFile file, string[] references = null,
            string correlationId = null)
        {
            var xchange = new Xchange(document.Id, file, references, SubscriptionType.Internal, correlationId);
            await AddFile(xchange.Id, XchangeFileType.Input, file);
            dbContext.Add(xchange);
            return xchange;
        }

        public async Task<Xchange> CreateXchange(Subscription subscription, XchangeFile file,
            string[] references = null, string correlationId = null)
        {
            var xchange = new Xchange(subscription, file, references, correlationId);
            await AddFile(xchange.Id, XchangeFileType.Input, file);
            dbContext.Add(xchange);
            return xchange;
        }

        public async Task CreateOnHoldXchange(Subscription subscription, XchangeFile file, string[] references = null)
        {
            var xchange = new OnHoldXchange(subscription, file.Data, file.Filename, file.BadData, references);
            dbContext.Add(xchange);
        }


        async Task<XchangeFile> RunMapper(Xchange xchange, XchangeFile xchangeFile)
        {
            if (xchange.MapperId == null) return xchangeFile;

            var serverless = serviceProvider.GetRequiredService<IServerlessService>();

            var mapperProperties = xchange.MapperProperties.ToDictionary();
            mapperProperties["xchangeid"] = xchange.Id;

            await serverless.StartAsync(xchange.MapperId, xchange.CorrelationId ?? xchange.Id, mapperProperties);
            xchangeFile = await serverless.InvokeAsync<XchangeFile>(nameof(IInfolinkHandler.Handle), xchangeFile);
            if (xchangeFile is null)
                throw new InfolinkException(
                    $"Unexpected null return value after running mapping for exchange id: {xchange.Id}, adapter id: {xchange.MapperId}");
            else
                await AddFile(xchange.Id, XchangeFileType.Output, xchangeFile);

            return xchangeFile;
        }

        async public Task RunValidator(string validatorId, IDictionary<string, string> properties,
            XchangeFile xchangeFile)
        {
            if (validatorId == null) return;

            var serverless = serviceProvider.GetRequiredService<IServerlessService>();
            await serverless.StartAsync(validatorId, null, properties);
            var result =
                await serverless.InvokeAsync<InfolinkValidatorResult>(nameof(IInfolinkValidator.Validate), xchangeFile);
            if (!result.Success)
                throw new SWValidationException(result.Validations);
        }

        async Task<XchangeFile> RunHandler(Xchange xchange, XchangeFile xchangeFile)
        {
            if (xchange.HandlerId == null) return null;

            var serverless = serviceProvider.GetRequiredService<IServerlessService>();

            var handlerProperties = xchange.HandlerProperties.ToDictionary();
            handlerProperties["xchangeid"] = xchange.Id;

            await serverless.StartAsync(xchange.HandlerId, xchange.CorrelationId ?? xchange.Id, handlerProperties);
            xchangeFile = await serverless.InvokeAsync<XchangeFile>(nameof(IInfolinkHandler.Handle), xchangeFile);
            if (xchangeFile != null)
                await AddFile(xchange.Id, XchangeFileType.Response, xchangeFile);
            return xchangeFile;
        }

        async Task AddFile(string xchangeId, XchangeFileType type, XchangeFile file)
        {
            await cloudFiles.WriteTextAsync(file.Data, new WriteFileSettings
            {
                //ContentType = "",
                Public = !infolinkSettings.AreXChangeFilesPrivate,
                Key = GetFileKey(xchangeId,
                    type) //$"{infolinkSettings.DocumentPrefix}/{xchangeId}/{type.ToString().ToLower()}"
            });
        }

        public string GetFileUrl(string xchangeId, XchangeFileType type)
        {
            return cloudFiles.GetUrl(GetFileKey(xchangeId, type));
        }

        public string GetFileUrl(string xchangeId, int? fileSize, XchangeFileType type)
        {
            if (fileSize == null || fileSize == 0)
                return null;
            return cloudFiles.GetUrl(GetFileKey(xchangeId, type));
        }

        public string GetFileKey(string xchangeId, XchangeFileType type)
        {
            var key = $"{infolinkSettings.DocumentPrefix}/{xchangeId}/{type.ToString().ToLower()}";
            logger.LogInformation($"the file key is:'{key}'");
            return key;
        }

        public async Task<string> GetFile(string xchangeId, XchangeFileType type)
        {
            using var cloudStream = await cloudFiles.OpenReadAsync(GetFileKey(xchangeId, type));
            using var reader = new StreamReader(cloudStream);
            return await reader.ReadToEndAsync();
        }

        async Task Process(XchangeCreatedEvent message)
        {
            Xchange responseXchange = null;
            XchangeFile outputFile = null;
            XchangeFile responseFile = null;

            var xchange = await dbContext.FindAsync<Xchange>(message.Id);

            if (xchange == null) throw new InfolinkException($"Xchange '{message.Id}' not found.");
            var document = await dbContext.FindAsync<Document>(xchange.DocumentId);

            try
            {
                var inputFile = new XchangeFile(await GetFile(xchange.Id, XchangeFileType.Input), xchange.InputName);
                var result = filterService.Filter(xchange.DocumentId, inputFile,
                    document?.DocumentFormat ?? DocumentFormat.Json);

                dbContext.Add(new XchangePromotedProperties(xchange.Id, result));

                if (xchange.SubscriptionId != null)
                {
                    if (xchange.MapperId == null)
                        responseFile = await RunHandler(xchange, inputFile);
                    else
                    {
                        outputFile = await RunMapper(xchange, inputFile);
                        responseFile = await RunHandler(xchange, outputFile);
                    }

                    if (xchange.ResponseSubscriptionId != null && responseFile != null)
                    {
                        //var subscription = await dbContext.FindAsync<Subscription>(xchange.ResponseSubscriptionId.Value);
                        var subscription = await dbContext.Set<Subscription>()
                            .Where(e => e.Id == xchange.ResponseSubscriptionId)
                            .AsNoTracking()
                            .FirstOrDefaultAsync();

                        responseXchange = await CreateXchange(subscription, responseFile, null, xchange.CorrelationId);
                    }

                    if (!string.IsNullOrWhiteSpace(xchange.ResponseMessageTypeName) && responseFile != null &&
                        !responseFile.BadData)
                    {
                        await publish.Publish(xchange.ResponseMessageTypeName, responseFile.Data);
                    }
                }
                else if (xchange.SubscriptionId == null)
                {
                    await CreateXchangesForHits(xchange, result, inputFile);
                }

                dbContext.Add(new XchangeResult(xchange.Id, outputFile, responseFile, responseXchange?.Id));
                var tracker = dbContext.ChangeTracker.Entries();
                await dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                dbContext.Add(new XchangeResult(xchange.Id, outputFile, responseFile, responseXchange?.Id,
                    ex.ToString()));
                await dbContext.SaveChangesAsync();
            }
        }

        async Task CreateXchangesForHits(Xchange xchange, FilterResult result, XchangeFile inputFile)
        {
            foreach (var subscriptionId in result.Hits)
            {
                //var subscription = await dbContext.FindAsync<Subscription>(subscriptionId);//TODO:this needs to be AsNoTrackable
                var subscription = await dbContext.Set<Subscription>()
                    .Where(e => e.Id == subscriptionId)
                    .AsNoTracking()
                    .FirstOrDefaultAsync();

                if (subscription.PausedOn != null)
                {
                    await CreateOnHoldXchange(subscription, inputFile);
                }
                else
                {
                    await CreateXchange(subscription, inputFile, null, xchange.CorrelationId);
                }
            }
        }

        Task IConsume<ApiXchangeCreatedEvent>.Process(ApiXchangeCreatedEvent message) => Process(message);

        Task IConsume<AggregateXchangeCreatedEvent>.Process(AggregateXchangeCreatedEvent message) => Process(message);

        Task IConsume<InternalXchangeCreatedEvent>.Process(InternalXchangeCreatedEvent message) => Process(message);

        Task IConsume<ReceivingXchangeCreatedEvent>.Process(ReceivingXchangeCreatedEvent message) => Process(message);


        public async Task Process(XchangeResultCreatedEvent message)
        {
            var notifiers = await dbContext.Set<Notifier>()
                .ToListAsync();

            var xchangeResult = await dbContext.FindAsync<XchangeResult>(message.Id);

            var xchange = await dbContext.FindAsync<Xchange>(message.Id);

            foreach (var notifier in notifiers)
            {
                if (notifier.RunOnSubscriptions != null && notifier.RunOnSubscriptions.Any() &&
                    notifier.RunOnSubscriptions.All(s => s != xchange.SubscriptionId)) return;
                if (notifier.Inactive) return;

                switch (message.Success)
                {
                    case true when !message.ResponseBad && notifier.RunOnSuccessfulResult:
                    case true when message.ResponseBad && notifier.RunOnBadResult:
                    case false when notifier.RunOnFailedResult:
                        await NotifyResult(notifier, xchangeResult, xchange?.CorrelationId ?? xchange?.Id);
                        break;
                }
            }
        }

        private async Task NotifyResult(Notifier notifier, XchangeResult xchangeResult, string correlationId)
        {
            if (xchangeResult == null) throw new InfolinkException($"Xchange Result '{xchangeResult.Id}' not found.");

            if (notifier?.HandlerId == null) return;

            var notificationData = new XchangeResultNotification
            {
                Id = xchangeResult.Id,
                Exception = xchangeResult.Exception,
                Success = xchangeResult.Success,
                FinishedOn = xchangeResult.FinishedOn,
                OutputBad = xchangeResult.OutputBad,
                ResponseBad = xchangeResult.ResponseBad,
            };

            var serverless = serviceProvider.GetRequiredService<IServerlessService>();

            var handlerProperties = notifier.HandlerProperties.ToDictionary();
            handlerProperties["xchangeid"] = xchangeResult.Id;

            try
            {
                await serverless.StartAsync(notifier.HandlerId, correlationId, handlerProperties);
                var xchangeFile = await serverless.InvokeAsync<XchangeFile>(nameof(IInfolinkHandler.Handle),
                    new XchangeFile(JsonConvert.SerializeObject(notificationData), xchangeResult.Id));

                dbContext.Add(new XchangeNotification(xchangeResult.Id, notifier.Id, notifier.Name));
            }
            catch (Exception ex)
            {
                dbContext.Add(new XchangeNotification(xchangeResult.Id, notifier.Id, notifier.Name, ex.ToString()));
            }

            await dbContext.SaveChangesAsync();
        }

        public async Task Process(SubscriptionUnpausedEvent message)
        {
            var subscription = await dbContext.Set<Subscription>().FirstOrDefaultAsync(s => s.Id == message.Id);

            if (subscription == null || subscription.Inactive || subscription.PausedOn != null) return;

            var xchangesDetails = await dbContext.Set<OnHoldXchange>().Where(x => x.SubscriptionId == subscription.Id)
                .ToListAsync();

            foreach (var xchangeDetails in xchangesDetails)
            {
                var file = new XchangeFile(xchangeDetails.Data, xchangeDetails.FileName, xchangeDetails.BadData);
                await CreateXchange(subscription, file, xchangeDetails.References);
                dbContext.Remove(xchangeDetails);
            }

            await dbContext.SaveChangesAsync();
        }
    }
}