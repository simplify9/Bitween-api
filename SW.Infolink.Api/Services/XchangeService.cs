using Microsoft.Extensions.DependencyInjection;
using SW.Infolink.Api;
using SW.Infolink.Domain;
using SW.Infolink.Model;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace SW.Infolink
{
    public class XchangeService :
        IConsume<ApiXchangeCreatedEvent>,
        IConsume<InternalXchangeCreatedEvent>,
        IConsume<AggregateXchangeCreatedEvent>,
        IConsume<ReceivingXchangeCreatedEvent>

    {
        private readonly InfolinkOptions infolinkSettings;
        private readonly InfolinkDbContext dbContext;
        private readonly FilterService filterService;
        private readonly ICloudFilesService cloudFiles;
        private readonly IServiceProvider serviceProvider;
        private readonly IPublish publish;

        public XchangeService(InfolinkOptions infolinkSettings, InfolinkDbContext dbContext, FilterService filterService, ICloudFilesService cloudFiles, IServiceProvider serviceProvider, IPublish publish)
        {
            this.infolinkSettings = infolinkSettings;
            this.dbContext = dbContext;
            this.filterService = filterService;
            this.cloudFiles = cloudFiles;
            this.serviceProvider = serviceProvider;
            this.publish = publish;
        }

        async public Task<string> SubmitSubscriptionXchange(int subscriptionId, XchangeFile file, string[] references = null)
        {
            var subscription = await dbContext.FindAsync<Subscription>(subscriptionId);
            var xchange = await CreateXchange(subscription, file, references);
            await dbContext.SaveChangesAsync();
            return xchange.Id;
        }

        async public Task<string> SubmitFilterXchange(int documentId, XchangeFile file)
        {
            var document = await dbContext.FindAsync<Document>(documentId);
            var xchange = await CreateXchange(document, file);
            await dbContext.SaveChangesAsync();
            return xchange.Id;
        }

        public async Task<Xchange> CreateXchange(Xchange xchange, XchangeFile file)
        {
            var newXchange = new Xchange(xchange, file);
            await AddFile(newXchange.Id, XchangeFileType.Input, file);
            dbContext.Add(newXchange);
            return xchange;
        }
        
        public async Task<Xchange> CreateXchange(Subscription subscription, Xchange xchange, XchangeFile file, string[] references = null)
        {
            var newXchange = new Xchange(subscription, xchange, file);
            await AddFile(newXchange.Id, XchangeFileType.Input, file);
            dbContext.Add(newXchange);
            return xchange;
            
            
        }

        public async Task<Xchange> CreateXchange(Document document, XchangeFile file)
        {
            var xchange = new Xchange(document.Id, file);
            await AddFile(xchange.Id, XchangeFileType.Input, file);
            dbContext.Add(xchange);
            return xchange;
        }

        public async Task<Xchange> CreateXchange(Subscription subscription, XchangeFile file, string[] references = null)
        {
            var xchange = new Xchange(subscription, file, references);
            await AddFile(xchange.Id, XchangeFileType.Input, file);
            dbContext.Add(xchange);
            return xchange;
        }
       



        async Task<XchangeFile> RunMapper(Xchange xchange, XchangeFile xchangeFile)
        {
            if (xchange.MapperId == null) return xchangeFile;

            var serverless = serviceProvider.GetRequiredService<IServerlessService>();

            var mapperProperties = xchange.MapperProperties.ToDictionary();
            mapperProperties["xchangeid"] = xchange.Id; 
            
            await serverless.StartAsync(xchange.MapperId, xchange.Id, mapperProperties);
            xchangeFile = await serverless.InvokeAsync<XchangeFile>(nameof(IInfolinkHandler.Handle), xchangeFile);
            if (xchangeFile is null)
                throw new InfolinkException($"Unexpected null return value after running mapping for exchange id: {xchange.Id}, adapter id: {xchange.MapperId}");
            else
                await AddFile(xchange.Id, XchangeFileType.Output, xchangeFile);

            return xchangeFile;
        }

        async public Task RunValidator(string validatorId, IDictionary<string, string> properties, XchangeFile xchangeFile)
        {
            if (validatorId == null) return;

            var serverless = serviceProvider.GetRequiredService<IServerlessService>();
            await serverless.StartAsync(validatorId, null, properties);
            var result = await serverless.InvokeAsync<InfolinkValidatorResult>(nameof(IInfolinkValidator.Validate), xchangeFile);
            if (!result.Success)
                throw new SWValidationException(result.Validations);
        }

        async Task<XchangeFile> RunHandler(Xchange xchange, XchangeFile xchangeFile)
        {
            if (xchange.HandlerId == null) return null;

            var serverless = serviceProvider.GetRequiredService<IServerlessService>();

            var handlerProperties = xchange.HandlerProperties.ToDictionary();
            handlerProperties["xchangeid"] = xchange.Id;

            await serverless.StartAsync(xchange.HandlerId, xchange.Id, handlerProperties);
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
                Public = true,
                Key = GetFileKey(xchangeId, type) //$"{infolinkSettings.DocumentPrefix}/{xchangeId}/{type.ToString().ToLower()}"
            });
        }

        public string GetFileUrl(string xchangeId, XchangeFileType type)
        {
            return cloudFiles.GetUrl(GetFileKey(xchangeId, type));
        }

        public string GetFileUrl(string xchangeId, int fileSize, XchangeFileType type)
        {
            if (fileSize == 0)
                return null;
            return cloudFiles.GetUrl(GetFileKey(xchangeId, type));
        }

        public string GetFileKey(string xchangeId, XchangeFileType type)
        {
            return $"{infolinkSettings.DocumentPrefix}/{xchangeId}/{type.ToString().ToLower()}";
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

            try
            {
                var inputFile = new XchangeFile(await GetFile(xchange.Id, XchangeFileType.Input), xchange.InputName);


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
                        var subscription = await dbContext.FindAsync<Subscription>(xchange.ResponseSubscriptionId.Value);
                        responseXchange = await CreateXchange(subscription, responseFile);
                    }

                    if (!string.IsNullOrWhiteSpace(xchange.ResponseMessageTypeName) && responseFile != null && !responseFile.BadData)
                    {
                        await publish.Publish(xchange.ResponseMessageTypeName, responseFile.Data);
                    }

                }
                else if (xchange.SubscriptionId == null)
                {
                    var result = filterService.Filter(xchange.DocumentId, inputFile);

                    dbContext.Add(new XchangePromotedProperties(xchange.Id, result));

                    foreach (var subscriptionId in result.Hits)
                    {
                        var subscription = await dbContext.FindAsync<Subscription>(subscriptionId);
                        await CreateXchange(subscription, inputFile);
                    }
                }

                dbContext.Add(new XchangeResult(xchange.Id, outputFile, responseFile, responseXchange?.Id));
                var tracker = dbContext.ChangeTracker.Entries();
                await dbContext.SaveChangesAsync();

            }
            catch (Exception ex)
            {
                dbContext.Add(new XchangeResult(xchange.Id, outputFile, responseFile, responseXchange?.Id, ex.ToString()));
                await dbContext.SaveChangesAsync();
            }
        }

        Task IConsume<ApiXchangeCreatedEvent>.Process(ApiXchangeCreatedEvent message) => Process(message);

        Task IConsume<AggregateXchangeCreatedEvent>.Process(AggregateXchangeCreatedEvent message) => Process(message);

        Task IConsume<InternalXchangeCreatedEvent>.Process(InternalXchangeCreatedEvent message) => Process(message);

        Task IConsume<ReceivingXchangeCreatedEvent>.Process(ReceivingXchangeCreatedEvent message) => Process(message);
    }

}




