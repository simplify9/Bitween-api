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
        private readonly InfolinkOptions _infolinkSettings;
        private readonly InfolinkDbContext _dbContext;
        private readonly FilterService _filterService;
        private readonly ICloudFilesService _cloudFiles;
        private readonly IServiceProvider _serviceProvider;
        private readonly IPublish _publish;
        private readonly ILogger _logger;
        private readonly IInfolinkCache _infolinkCache;

        public XchangeService(InfolinkOptions infolinkSettings, InfolinkDbContext dbContext,
            FilterService filterService,
            ICloudFilesService cloudFiles, IServiceProvider serviceProvider,
            IPublish publish, ILogger<XchangeService> logger, IInfolinkCache infolinkCache)
        {
            _infolinkSettings = infolinkSettings;
            _dbContext = dbContext;
            _filterService = filterService;
            _cloudFiles = cloudFiles;
            _serviceProvider = serviceProvider;
            _publish = publish;
            _logger = logger;
            _infolinkCache = infolinkCache;
        }

        public async Task<string> SubmitSubscriptionXchange(int subscriptionId, XchangeFile file,
            string[] references = null)
        {
            var subscription = await _infolinkCache.SubscriptionByIdAsync(subscriptionId);

            var xchange = await CreateXchange(subscription, file, references, Guid.NewGuid().ToString("N"));
            await _dbContext.SaveChangesAsync();
            return xchange.Id;
        }

        public async Task<string> SubmitFilterXchange(int documentId, XchangeFile file, string[] references = null,
            string correlationId = null)
        {
            var document = await _infolinkCache.DocumentByIdAsync(documentId);
            Xchange xchange;

            if (document?.DisregardsUnfilteredMessages ?? false)
            {
                xchange = new Xchange(documentId, file, references, SubscriptionType.Internal, correlationId);
                var result = await _filterService.Filter(xchange.DocumentId, file);
                await CreateXchangesForHits(xchange, result, file);
            }
            else
            {
                xchange = await CreateXchange(document, file, references, correlationId);
            }

            await _dbContext.SaveChangesAsync();

            return xchange.Id;
        }

        public async Task<Xchange> CreateXchange(Xchange xchange, XchangeFile file)
        {
            var newXchange = new Xchange(xchange, file);
            await AddFile(newXchange.Id, XchangeFileType.Input, file);
            _dbContext.Add(newXchange);
            return xchange;
        }

        public async Task<Xchange> CreateXchange(Subscription subscription, Xchange xchange, XchangeFile file,
            string[] references = null)
        {
            var newXchange = new Xchange(subscription, xchange, file);
            await AddFile(newXchange.Id, XchangeFileType.Input, file);
            _dbContext.Add(newXchange);
            return xchange;
        }

        public async Task<Xchange> CreateXchange(Document document, XchangeFile file, string[] references = null,
            string correlationId = null)
        {
            var xchange = new Xchange(document.Id, file, references, SubscriptionType.Internal, correlationId);
            await AddFile(xchange.Id, XchangeFileType.Input, file);
            _dbContext.Add(xchange);
            return xchange;
        }

        public async Task<Xchange> CreateXchange(Subscription subscription, XchangeFile file,
            string[] references = null, string correlationId = null)
        {
            var xchange = new Xchange(subscription, file, references, correlationId);
            await AddFile(xchange.Id, XchangeFileType.Input, file);
            _dbContext.Add(xchange);
            return xchange;
        }

        private Task CreateOnHoldXchange(Subscription subscription, XchangeFile file, string[] references = null)
        {
            var xchange = new OnHoldXchange(subscription, file.Data, file.Filename, file.BadData, references);
            _dbContext.Add(xchange);
            return Task.CompletedTask;
        }


        async Task<XchangeFile> RunMapper(Xchange xchange, XchangeFile xchangeFile)
        {
            if (xchange.MapperId == null) return xchangeFile;

            var serverless = _serviceProvider.GetRequiredService<IServerlessService>();

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

        public async Task RunValidator(string validatorId, IDictionary<string, string> properties,
            XchangeFile xchangeFile)
        {
            if (validatorId == null) return;

            var serverless = _serviceProvider.GetRequiredService<IServerlessService>();
            await serverless.StartAsync(validatorId, null, properties);
            var result =
                await serverless.InvokeAsync<InfolinkValidatorResult>(nameof(IInfolinkValidator.Validate), xchangeFile);
            if (!result.Success)
                throw new SWValidationException(result.Validations);
        }

        private async Task<XchangeFile> RunHandler(Xchange xchange, XchangeFile xchangeFile)
        {
            if (xchange.HandlerId == null) return null;

            var serverless = _serviceProvider.GetRequiredService<IServerlessService>();

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
            await _cloudFiles.WriteTextAsync(file.Data, new WriteFileSettings
            {
                Public = !_infolinkSettings.AreXChangeFilesPrivate,
                Key = GetFileKey(xchangeId, type)
            });
        }

        public string GetFileUrl(string xchangeId, XchangeFileType type)
        {
            return _cloudFiles.GetUrl(GetFileKey(xchangeId, type));
        }

        public string GetFileUrl(string xchangeId, int? fileSize, XchangeFileType type)
        {
            return fileSize is null or 0 ? null : _cloudFiles.GetUrl(GetFileKey(xchangeId, type));
        }

        public string GetFileKey(string xchangeId, int? fileSize, XchangeFileType type)
        {
            if (fileSize is null or 0)
                return null;
            var key = $"{_infolinkSettings.DocumentPrefix}/{xchangeId}/{type.ToString().ToLower()}";
            _logger.LogInformation($"the file key is:'{key}'");
            return key;
        }

        private string GetFileKey(string xchangeId, XchangeFileType type)
        {
            var key = $"{_infolinkSettings.DocumentPrefix}/{xchangeId}/{type.ToString().ToLower()}";
            _logger.LogInformation($"the file key is:'{key}'");
            return key;
        }

        public async Task<string> GetFile(string xchangeId, XchangeFileType type)
        {
            await using var cloudStream = await _cloudFiles.OpenReadAsync(GetFileKey(xchangeId, type));
            using var reader = new StreamReader(cloudStream);
            return await reader.ReadToEndAsync();
        }

        async Task Process(XchangeCreatedEvent message)
        {
            Xchange responseXchange = null;
            XchangeFile outputFile = null;
            XchangeFile responseFile = null;

            var xchange = await _dbContext.FindAsync<Xchange>(message.Id);

            if (xchange == null) throw new InfolinkException($"Xchange '{message.Id}' not found.");

            try
            {
                var inputFile = new XchangeFile(await GetFile(xchange.Id, XchangeFileType.Input), xchange.InputName);
                var result = await _filterService.Filter(xchange.DocumentId, inputFile);

                _dbContext.Add(new XchangePromotedProperties(xchange.Id, result));

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
                        var subscription =
                            await _infolinkCache.SubscriptionByIdAsync(xchange.ResponseSubscriptionId.Value);

                        responseXchange = await CreateXchange(subscription, responseFile, null, xchange.CorrelationId);
                    }

                    if (!string.IsNullOrWhiteSpace(xchange.ResponseMessageTypeName) && responseFile != null &&
                        !responseFile.BadData)
                    {
                        await _publish.Publish(xchange.ResponseMessageTypeName, responseFile.Data);
                    }
                }
                else if (xchange.SubscriptionId == null)
                {
                    await CreateXchangesForHits(xchange, result, inputFile);
                }

                _dbContext.Add(new XchangeResult(xchange.Id, outputFile, responseFile, responseXchange?.Id));
                await _dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _dbContext.Add(new XchangeResult(xchange.Id, outputFile, responseFile, responseXchange?.Id,
                    ex.ToString()));
                await _dbContext.SaveChangesAsync();
            }
        }

        async Task CreateXchangesForHits(Xchange xchange, FilterResult result, XchangeFile inputFile)
        {
            foreach (var subscriptionId in result.Hits)
            {
                var subscription = await _infolinkCache.SubscriptionByIdAsync(subscriptionId);
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
            var notifiers = await _infolinkCache.ListNotifiersAsync();

            var xchangeResult = await _dbContext.FindAsync<XchangeResult>(message.Id);

            var xchange = await _dbContext.FindAsync<Xchange>(message.Id);

            foreach (var notifier in notifiers)
            {
                if (notifier.Inactive || notifier.RunOnSubscriptions is null) continue;

                //review 
                if (notifier.RunOnSubscriptions.All(i => i != xchange!.SubscriptionId))
                {
                    continue;
                }


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

            var serverless = _serviceProvider.GetRequiredService<IServerlessService>();

            var handlerProperties = notifier.HandlerProperties.ToDictionary();
            handlerProperties["xchangeid"] = xchangeResult.Id;

            try
            {
                await serverless.StartAsync(notifier.HandlerId, correlationId, handlerProperties);
                await serverless.InvokeAsync<XchangeFile>(nameof(IInfolinkHandler.Handle),
                    new XchangeFile(JsonConvert.SerializeObject(notificationData), xchangeResult.Id));

                _dbContext.Add(new XchangeNotification(xchangeResult.Id, notifier.Id, notifier.Name));
            }
            catch (Exception ex)
            {
                _dbContext.Add(new XchangeNotification(xchangeResult.Id, notifier.Id, notifier.Name, ex.ToString()));
            }

            await _dbContext.SaveChangesAsync();
        }

        public async Task Process(SubscriptionUnpausedEvent message)
        {
            var subscription = await _infolinkCache.SubscriptionByIdAsync(message.Id);

            if (subscription == null || subscription.Inactive || subscription.PausedOn != null) return;

            var xchangesDetails = await _dbContext.Set<OnHoldXchange>().Where(x => x.SubscriptionId == subscription.Id)
                .ToListAsync();

            foreach (var xchangeDetails in xchangesDetails)
            {
                var file = new XchangeFile(xchangeDetails.Data, xchangeDetails.FileName, xchangeDetails.BadData);
                await CreateXchange(subscription, file, xchangeDetails.References);
                _dbContext.Remove(xchangeDetails);
            }

            await _dbContext.SaveChangesAsync();
        }
    }
}