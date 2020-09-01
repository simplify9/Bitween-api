using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using SW.Infolink.Domain;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SW.Infolink
{
    public class PipelineService : IConsume<XchangeSubmittedMessage>
    {
        private readonly InfolinkDbContext dbContext;

        //readonly AdapterService adapterService;
        private readonly BlobService infolinkDms;
        private readonly IPublish publish;
        private readonly IServiceProvider serviceProvider;

        public PipelineService(InfolinkDbContext dbContext, BlobService infolinkDms, IPublish publish, IServiceProvider serviceProvider)
        {
            this.dbContext = dbContext;
            //this.adapterService = adapterService;
            this.infolinkDms = infolinkDms;
            this.publish = publish;
            this.serviceProvider = serviceProvider;
        }

        async public Task Process(XchangeSubmittedMessage message)
        {

            //var xchange = await dbContext.FindAsync<Xchange>(message.Id);
            //var subscriber = await dbContext.FindAsync<Subscriber>(xchange.SubscriberId);

            try
            {
                var fileData = await infolinkDms.GetFile(message.XchangeId, XchangeFileType.Input);
                var file = new XchangeFile(fileData, message.InputFileName);

                if (message.MapperId != null)
                {
                    file = await Run(message.MapperId, message.XchangeId, file, message.SubscriberId, message.SubscriberProperties);
                }

                if (file is null)
                {
                    throw new InfolinkException($"Unexpected null return value after running mapping for exchange id: {message.XchangeId}, adapter id: {message.MapperId}");
                }
                await infolinkDms.AddFile(message.XchangeId, XchangeFileType.Output, file);

                if (message.HandlerId != null)
                {
                    var responseFile = await Run(message.HandlerId, message.XchangeId, file, message.SubscriberId, message.SubscriberProperties);

                    if (responseFile is null)
                    {
                        responseFile = new XchangeFile("");
                    }
                    await infolinkDms.AddFile(message.XchangeId, XchangeFileType.Response, responseFile);
                    //if (subscriber.ResponseSubscriberId != 0)
                    //    xchange.ResponseXchangeId = await xchangeService.Submit(subscriber.ResponseSubscriberId, responseFile);

                    await publish.Publish(new PipelineFinishedMessage
                    {
                        Success = true,
                        XchangeId = message.XchangeId,
                        ResponseFileName = responseFile.Filename
                    });
                }
                else
                {
                    await publish.Publish(new PipelineFinishedMessage
                    {
                        Success = true,
                        XchangeId = message.XchangeId,
                        //ResponseFileName = responseFile.Filename
                    });
                }


            }
            catch (Exception ex)
            {
                await publish.Publish(new PipelineFinishedMessage
                {
                    Success = false,
                    XchangeId = message.XchangeId,
                    ErrorMessage = ex.ToString()
                });
            }

        }


        async Task<XchangeFile> Run(string adapterId, int xchangeId, XchangeFile xchangeFile, int subscriberId, IDictionary<string, string> subscriberProperties)
        {


            //var preq = new PipelineRequest
            //{
            //    File = file,
            //    Xchange = new XchangeDto
            //    {
            //        Id = xchangeId
            //    },
            //    Subscriber = new SubscriberDto
            //    {
            //        Id = subscriberId,
            //        Properties = subscriberProperties
            //    }
            //};

            //var serverlessId = await dbContext.Set<Adapter>().Where(p => p.Id == adapterId).Select(p => p.ServerlessId).SingleAsync();
            var serverless = serviceProvider.GetRequiredService<IServerlessService>();
            await serverless.StartAsync(adapterId, subscriberProperties);

            var pres = await serverless.InvokeAsync<XchangeFile>(nameof(IInfolinkHandler.Handle), xchangeFile);

            return pres;
        }

    }
}
