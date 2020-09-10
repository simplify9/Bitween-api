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
    internal class XchangeService : IConsume<XchangeCreatedEvent>
    {
        private readonly InfolinkSettings infolinkSettings;
        private readonly InfolinkDbContext dbContext;
        private readonly FilterService filterService;
        private readonly ICloudFilesService cloudFiles;
        private readonly IServiceProvider serviceProvider;

        public XchangeService(InfolinkSettings infolinkSettings, InfolinkDbContext dbContext, FilterService filterService, ICloudFilesService cloudFiles, IServiceProvider serviceProvider)
        {
            this.infolinkSettings = infolinkSettings;
            this.dbContext = dbContext;
            this.filterService = filterService;
            this.cloudFiles = cloudFiles;
            this.serviceProvider = serviceProvider;
        }

        async public Task<string> SubmitSubscriptionXchange(int subscriptionId, XchangeFile file)
        {
            var subscription = await dbContext.FindAsync<Subscription>(subscriptionId);
            var xchange = await CreateXchange(subscription, file);
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

        public async Task<Xchange> CreateXchange(Subscription subscription, XchangeFile file)
        {
            var xchange = new Xchange(subscription, file, null);
            await AddFile(xchange.Id, XchangeFileType.Input, file);
            dbContext.Add(xchange);
            return xchange;
        }

        public async Task<Xchange> CreateXchange(Document document, XchangeFile file)
        {
            var xchange = new Xchange(document.Id, file);
            await AddFile(xchange.Id, XchangeFileType.Input, file);
            dbContext.Add(xchange);
            return xchange;
        }

        async Task<XchangeFile> RunMapper(Xchange xchange, XchangeFile xchangeFile)
        {
            if (xchange.MapperId == null) return xchangeFile;

            var serverless = serviceProvider.GetRequiredService<IServerlessService>();
            await serverless.StartAsync(xchange.MapperId, xchange.Id, xchange.MapperProperties.ToDictionary());
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
            await serverless.StartAsync(xchange.HandlerId, xchange.Id, xchange.HandlerProperties.ToDictionary());
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

        async Task IConsume<XchangeCreatedEvent>.Process(XchangeCreatedEvent message)
        {

            Xchange responseXchange = null;
            XchangeFile outputFile = null;
            XchangeFile responseFile = null;

            var xchange = await dbContext.FindAsync<Xchange>(message.Id);
            if (xchange == null) throw new InfolinkException($"Could not find xchnage id: {message.Id}");

            try
            {
                var inputFile = new XchangeFile(await GetFile(xchange.Id, XchangeFileType.Input), xchange.InputName);


                if (xchange.SubscriptionId != null)
                {
                    outputFile = await RunMapper(xchange, inputFile);

                    responseFile = await RunHandler(xchange, outputFile);

                    if (xchange.ResponseSubscriptionId != null && responseFile != null)
                    {
                        var subscription = await dbContext.FindAsync<Subscription>(xchange.ResponseSubscriptionId.Value);
                        responseXchange = await CreateXchange(subscription, responseFile);
                    }

                }
                else if (xchange.SubscriptionId == null)
                {
                    var result = filterService.Filter(xchange.DocumentId, inputFile);

                    dbContext.Add(new XchangePromotedProperties(xchange.Id, result.Properties));

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
    }

}


//        public async Task<int> Retry(int xchangeId)
//        {
//            var xchange = await dbContext.FindAsync<Xchange>(xchangeId);
//            var filedata = await infolinkDms.GetFile(xchange.Id, XchangeFileType.Input);
//            return await Submit(xchange.SubscriberId, new XchangeFile(filedata, xchange.InputFileName));
//        }

//        public async Task<int> Submit(
//            int subscriberId,
//            XchangeFile file,
//            IEnumerable<string> references = null,
//            bool ignoreSchedule = false)
//        {

//            if (file == null) throw new InfolinkException("Invalid file");

//            var subscriber = await dbContext.FindAsync<Subscriber>(subscriberId);
//            if (subscriber == null) throw new InfolinkException("Subscriber Id not found");

//            var document = await dbContext.FindAsync<Document>(subscriber.DocumentId);
//            if (document == null) throw new InfolinkException("Document Id not found");

//            var xchange = new Xchange
//            (
//                subscriberId: subscriberId,
//                file: file,
//                references: references?.ToArray(),
//                documentId: subscriber.DocumentId
//            );

//            dbContext.Add(xchange);
//            await dbContext.SaveChangesAsync();

//            try
//            {
//                //TODO check doc size
//                //TODO check for duplicates
//                await infolinkDms.AddFile(xchange.Id, XchangeFileType.Input, file);

//                var deliverOn = subscriber.Schedules.Next();

//                if (!ignoreSchedule && deliverOn != null)
//                    //xchange.DeliverOn = subscriber.NextSchedule();
//                    xchange.SetScheduled(deliverOn.Value);

//                else
//                {

//                    xchange.MapperId = subscriber.MapperId;
//                    xchange.HandlerId = subscriber.HandlerId;
//                    xchange.SetSubmitted(subscriber);
//                }
//                //}
//                //    else
//                //    {
//                //        xchange.SetSuccess();
//                //    }

//                //xchange.AssignAdapters(subscriber.MapperId, subscriber.HandlerId);

//                //else


//            }
//            catch (Exception ex)
//            {
//                xchange.SetFailure(ex);
//            }

//            //await repo.Update(xchange);
//            await dbContext.SaveChangesAsync();
//            return xchange.Id;
//        }

//        async public Task Process(PipelineFinishedMessage message)
//        {
//            var xchange = await dbContext.FindAsync<Xchange>(message.XchangeId);

//            try
//            {

//                var subscriber = await dbContext.FindAsync<Subscriber>(xchange.SubscriberId);
//                if (subscriber.ResponseSubscriberId != 0)
//                {
//                    var file = await infolinkDms.GetFile(xchange.Id, XchangeFileType.Response);
//                    xchange.ResponseXchangeId = await Submit(subscriber.ResponseSubscriberId, new XchangeFile(file, message.ResponseFileName));
//                }
//                if (message.Success)
//                {
//                    xchange.SetSuccess();
//                }
//                else
//                {
//                    xchange.SetFailure(message.ErrorMessage);
//                }

//            }
//            catch (Exception ex)
//            {

//                xchange.SetFailure(ex);
//            }

//            await dbContext.SaveChangesAsync();
//        }

//    }

//}



