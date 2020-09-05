using Microsoft.Extensions.DependencyInjection;
using SW.Infolink.Api;
using SW.Infolink.Domain;
using SW.Infolink.Model;
using SW.PrimitiveTypes;
using System;
using System.Threading.Tasks;

namespace SW.Infolink
{
    public class XchangeService : IConsume<XchangeCreatedEvent>
    {
        readonly BlobService infolinkDms;
        private readonly InfolinkDbContext dbContext;
        private readonly FilterService filterService;
        private readonly IServiceProvider serviceProvider;

        public XchangeService(BlobService infolinkDms, InfolinkDbContext dbContext, FilterService filterService, IServiceProvider serviceProvider)
        {
            this.infolinkDms = infolinkDms;
            this.dbContext = dbContext;
            this.filterService = filterService;
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
            var xchange = await CreateXchange(documentId, file);
            await dbContext.SaveChangesAsync();
            return xchange.Id;
        }

        async Task<Xchange> CreateXchange(Subscription subscription, XchangeFile file, bool ignoreSchedule = false)
        {
            var xchange = new Xchange(subscription, file, null, ignoreSchedule);
            await infolinkDms.AddFile(xchange.Id, XchangeFileType.Input, file);
            dbContext.Add(xchange);
            return xchange;
        }

        async Task<Xchange> CreateXchange(int documentId, XchangeFile file)
        {
            var xchange = new Xchange(documentId, file);
            await infolinkDms.AddFile(xchange.Id, XchangeFileType.Input, file);
            dbContext.Add(xchange);
            return xchange;
        }

        async Task<XchangeFile> RunMapper(Xchange xchange, XchangeFile xchangeFile)
        {
            if (xchange.MapperId == null) return xchangeFile;

            var serverless = serviceProvider.GetRequiredService<IServerlessService>();
            await serverless.StartAsync(xchange.MapperId, xchange.MapperProperties.ToDictionary());
            xchangeFile = await serverless.InvokeAsync<XchangeFile>(nameof(IInfolinkHandler.Handle), xchangeFile);
            if (xchangeFile is null)
                throw new InfolinkException($"Unexpected null return value after running mapping for exchange id: {xchange.Id}, adapter id: {xchange.MapperId}");
            else
                await infolinkDms.AddFile(xchange.Id, XchangeFileType.Output, xchangeFile);

            return xchangeFile;
        }

        async Task<XchangeFile> RunHandler(Xchange xchange, XchangeFile xchangeFile)
        {
            if (xchange.HandlerId == null) return xchangeFile;

            var serverless = serviceProvider.GetRequiredService<IServerlessService>();
            await serverless.StartAsync(xchange.HandlerId, xchange.HandlerProperties.ToDictionary());
            xchangeFile = await serverless.InvokeAsync<XchangeFile>(nameof(IInfolinkHandler.Handle), xchangeFile);
            if (xchangeFile != null)
                await infolinkDms.AddFile(xchange.Id, XchangeFileType.Response, xchangeFile);
            return xchangeFile;
        }

        async Task IConsume<XchangeCreatedEvent>.Process(XchangeCreatedEvent message)
        {
            var xchange = await dbContext.FindAsync<Xchange>(message.Id);
            Xchange responseXchange = null;
            var file = new XchangeFile(await infolinkDms.GetFile(xchange.Id, XchangeFileType.Input), xchange.InputFileName);

            if (xchange.SubscriptionId != null && xchange.DeliverOn == null)
            {
                file = await RunMapper(xchange, file);

                var responseFile = await RunHandler(xchange, file);

                if (xchange.ResponseSubscriptionId != null && responseFile != null)
                {
                    var subscription = await dbContext.FindAsync<Subscription>(xchange.ResponseSubscriptionId.Value);
                    responseXchange = await CreateXchange(subscription, responseFile);
                }

            }
            else if (xchange.SubscriptionId == null)
            {
                var result = filterService.Filter(xchange.DocumentId, file);

                dbContext.Add(new XchangePromotedProperties(xchange.Id, result.Properties));

                foreach (var subscriptionId in result.Hits)
                {
                    var subscription = await dbContext.FindAsync<Subscription>(subscriptionId);
                    await CreateXchange(subscription, file);
                }
            }

            dbContext.Add(new XchangeResult(xchange.Id, responseXchange?.Id));
            await dbContext.SaveChangesAsync();

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



