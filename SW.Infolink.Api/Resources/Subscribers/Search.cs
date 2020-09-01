using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using SW.EfCoreExtensions;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using SW.Infolink.Domain;

namespace SW.Infolink.Api.Resources.Subscribers
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
            var sr = new SearchyResponse<SubscriberRow>();

            var query = from subscriber in dbContext.Set<Subscriber>()
                        join document in dbContext.Set<Document>() on subscriber.DocumentId equals document.Id
                        //join mapper in dbContext.Set<Adapter>() on subscriber.MapperId equals mapper.Id into sm
                        //from mapper in sm.DefaultIfEmpty()
                        //join handler in dbContext.Set<Adapter>() on subscriber.HandlerId equals handler.Id into sh
                        //from handler in sh.DefaultIfEmpty()
                        select new SubscriberRow
                        {
                            Id = subscriber.Id,
                            Name = subscriber.Name,
                            Properties = subscriber.Properties.ToDictionary(),
                            DocumentId = subscriber.DocumentId,
                            DocumentName = document.Name,
                            HandlerId = subscriber.HandlerId,
                            Inactive = subscriber.Inactive,
                            //HandlerName = handler.Name,
                            MapperId = subscriber.MapperId,
                            //MapperName = mapper.Name,
                            Aggregate = subscriber.Aggregate,
                            Temporary = subscriber.Temporary,
                            Schedules = subscriber.Schedules

                        };

            sr.TotalCount = await query.AsNoTracking().Search(searchyRequest.Conditions).CountAsync();

            var results = query.AsNoTracking().Search(searchyRequest.Conditions, searchyRequest.Sorts, searchyRequest.PageSize, searchyRequest.PageIndex);

            sr.Result = await results.ToListAsync();
            return sr;
        }
    }
}
