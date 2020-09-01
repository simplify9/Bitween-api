using Microsoft.EntityFrameworkCore;
using SW.Infolink.Domain;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Globalization;
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

                var deliverOn = subscriber.NextSchedule();

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

        //public Task<IEnumerable<ISearchyFilterSetup>> GetFilterSetup()
        //{
        //    IEnumerable<ISearchyFilterSetup> result = new List<ISearchyFilterSetup>()
        //    {
        //        new SearchyFilterSetup { Field = "Id", Text = "Id", Type = SearchyDataType.Number },
        //        new SearchyFilterSetup { Field = "HandlerName", Text = "Handler Name", Type = SearchyDataType.Text },
        //        new SearchyFilterSetup { Field = "MapperName", Text = "Mapper Name", Type = SearchyDataType.Text },
        //    };

        //    return Task.FromResult(result);
        //}

        //async public Task<ISearchyResponse<XchangeRow>> Search(SearchyRequest searchyRequest)
        //{
        //    var query = from xchange in dbContext.Set<Xchange>()
        //                join mapper in dbContext.Set<Adapter>() on xchange.MapperId equals mapper.Id into xm
        //                from mapper in xm.DefaultIfEmpty()
        //                join handler in dbContext.Set<Adapter>() on xchange.HandlerId equals handler.Id into xh
        //                from handler in xh.DefaultIfEmpty()
        //                join document in dbContext.Set<Document>() on xchange.DocumentId equals document.Id
        //                join subscriber in dbContext.Set<Subscriber>() on xchange.SubscriberId equals subscriber.Id
        //                select new XchangeRow
        //                {
        //                    Id = xchange.Id,
        //                    HandlerId = xchange.HandlerId,
        //                    HandlerName = handler.Name,
        //                    MapperId = xchange.MapperId,
        //                    MapperName = mapper.Name,
        //                    DocumentId = xchange.DocumentId,
        //                    DocumentName = document.Name,
        //                    StartedOn = xchange.StartedOn,
        //                    FinishedOn =  xchange.FinishedOn,
        //                    SubscriberId = xchange.SubscriberId,
        //                    SubscriberName = subscriber.Name,
        //                    Status = xchange.Status,
        //                    StatusString = xchange.Status == XchangeStatus.Success ? "true" : "false",
        //                    Exception = xchange.Exception
        //                };

        //    var searchyResponse = new SearchyResponse<XchangeRow>
        //    {
        //        Result = await query.AsNoTracking().Search(searchyRequest.Conditions, searchyRequest.Sorts, searchyRequest.PageSize, searchyRequest.PageIndex).ToListAsync(),
        //        TotalCount = await query.AsNoTracking().Search(searchyRequest.Conditions).CountAsync()
        //    };

        //    return searchyResponse;
        //}

        //async public Task<string> Create(XchangeRequest request)
        //{
        //    if (request.SubscriberId > 0)
        //    {
        //        var id = await Submit(request.SubscriberId,
        //            request.File,
        //            request.References,
        //            request.IgnoreSchedule);

        //        return id.ToString();//CreatedAtAction(nameof(GetTodoItem), new { id = item.Id }, item);

        //    }
        //    else if (request.DocumentId > 0)
        //    {


        //        var subscribers = filterService.Filter(request.DocumentId, request.File);

        //        foreach (var sub in subscribers)
        //        {
        //            await Submit(sub, request.File);
        //        }

        //        return null;

        //    }
        //    else
        //    {
        //        throw new ArgumentException();
        //    }

        //}


    }

}



