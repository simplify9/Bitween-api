using SW.Infolink.Api;
using SW.Infolink.Domain;
using SW.Infolink.Model;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SW.Infolink
{
    public class XchangeService : IConsume<PipelineFinishedMessage>
    {
        readonly BlobService infolinkDms;
        private readonly InfolinkDbContext dbContext;

        public XchangeService(
            BlobService infolinkDms,
            InfolinkDbContext dbContext)
        {
            this.infolinkDms = infolinkDms;
            this.dbContext = dbContext;
        }

        public async Task<int> Retry(int xchangeId)
        {
            var xchange = await dbContext.FindAsync<Xchange>(xchangeId);
            var filedata = await infolinkDms.GetFile(xchange.Id, XchangeFileType.Input);
            return await Submit(xchange.SubscriberId, new XchangeFile(filedata, xchange.InputFileName));
        }

        public async Task<int> Submit(
        int subscriberId,
        XchangeFile file,
        IEnumerable<string> references = null,
        bool ignoreSchedule = false)
        {

            if (file == null) throw new InfolinkException("Invalid file");

            var subscriber = await dbContext.FindAsync<Subscriber>(subscriberId);
            if (subscriber == null) throw new InfolinkException("Subscriber Id not found");

            var document = await dbContext.FindAsync<Document>(subscriber.DocumentId);
            if (document == null) throw new InfolinkException("Document Id not found");

            var xchange = new Xchange
            (
                subscriberId: subscriberId,
                file: file,
                references: references?.ToArray(),
                documentId: subscriber.DocumentId
            );

            dbContext.Add(xchange);
            await dbContext.SaveChangesAsync();

            try
            {
                //TODO check doc size
                //TODO check for duplicates
                await infolinkDms.AddFile(xchange.Id, XchangeFileType.Input, file);

                var deliverOn = subscriber.Schedules.Next();

                if (!ignoreSchedule && deliverOn != null)
                    //xchange.DeliverOn = subscriber.NextSchedule();
                    xchange.SetScheduled(deliverOn.Value);

                else
                {

                    xchange.MapperId = subscriber.MapperId;
                    xchange.HandlerId = subscriber.HandlerId;
                    xchange.SetSubmitted(subscriber);
                }
                //}
                //    else
                //    {
                //        xchange.SetSuccess();
                //    }

                //xchange.AssignAdapters(subscriber.MapperId, subscriber.HandlerId);

                //else


            }
            catch (Exception ex)
            {
                xchange.SetFailure(ex);
            }

            //await repo.Update(xchange);
            await dbContext.SaveChangesAsync();
            return xchange.Id;
        }

        async public Task Process(PipelineFinishedMessage message)
        {
            var xchange = await dbContext.FindAsync<Xchange>(message.XchangeId);

            try
            {
                
                var subscriber = await dbContext.FindAsync<Subscriber>(xchange.SubscriberId);
                if (subscriber.ResponseSubscriberId != 0)
                {
                    var file = await infolinkDms.GetFile(xchange.Id, XchangeFileType.Response);
                    xchange.ResponseXchangeId = await Submit(subscriber.ResponseSubscriberId, new XchangeFile(file, message.ResponseFileName));
                }
                if (message.Success)
                {
                    xchange.SetSuccess();
                }
                else
                {
                    xchange.SetFailure(message.ErrorMessage);
                }
                
            }
            catch (Exception ex)
            {

                xchange.SetFailure(ex);
            }

            await dbContext.SaveChangesAsync();
        }

    }

}



