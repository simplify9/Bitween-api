using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using SW.EfCoreExtensions;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using SW.Infolink.Domain;

namespace SW.Infolink.Api.Resources.Xchanges
{
    class Search : ISearchyHandler
    {
        private readonly InfolinkDbContext dbContext;

        public Search(InfolinkDbContext dbContext)
        {
            this.dbContext = dbContext;
        }

        async public Task<object> Handle(SearchyRequest searchyRequest, bool lookup = false, string searchPhrase = null)
        {
            var query = from xchange in dbContext.Set<Xchange>()
                        //join mapper in dbContext.Set<Adapter>() on xchange.MapperId equals mapper.Id into xm
                        //from mapper in xm.DefaultIfEmpty()
                        //join handler in dbContext.Set<Adapter>() on xchange.HandlerId equals handler.Id into xh
                        //from handler in xh.DefaultIfEmpty()
                        join document in dbContext.Set<Document>() on xchange.DocumentId equals document.Id
                        join subscriber in dbContext.Set<Subscriber>() on xchange.SubscriberId equals subscriber.Id
                        select new XchangeRow
                        {
                            Id = xchange.Id,
                            HandlerId = xchange.HandlerId,
                            //HandlerName = handler.Name,
                            MapperId = xchange.MapperId,
                            //MapperName = mapper.Name,
                            DocumentId = xchange.DocumentId,
                            DocumentName = document.Name,
                            StartedOn = xchange.StartedOn,
                            FinishedOn = xchange.FinishedOn,
                            SubscriberId = xchange.SubscriberId,
                            SubscriberName = subscriber.Name,
                            Status = xchange.Status,
                            Exception = xchange.Exception
                        };

            var searchyResponse = new SearchyResponse<XchangeRow>
            {
                Result = await query.AsNoTracking().Search(searchyRequest.Conditions, searchyRequest.Sorts, searchyRequest.PageSize, searchyRequest.PageIndex).ToListAsync(),
                TotalCount = await query.AsNoTracking().Search(searchyRequest.Conditions).CountAsync()
            };

            return searchyResponse;
        }
    }
}
