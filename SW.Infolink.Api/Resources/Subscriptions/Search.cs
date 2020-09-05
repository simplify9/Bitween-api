using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using SW.EfCoreExtensions;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using SW.Infolink.Domain;
using SW.Infolink.Model;

namespace SW.Infolink.Api.Resources.Subscriptions
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

            var query = from subscriber in dbContext.Set<Subscription>()
                        join document in dbContext.Set<Document>() on subscriber.DocumentId equals document.Id
                        //join mapper in dbContext.Set<Adapter>() on subscriber.MapperId equals mapper.Id into sm
                        //from mapper in sm.DefaultIfEmpty()
                        //join handler in dbContext.Set<Adapter>() on subscriber.HandlerId equals handler.Id into sh
                        //from handler in sh.DefaultIfEmpty()
                        select new SubscriptionRow
                        {
                            Id = subscriber.Id,
                            Name = subscriber.Name,
                            Type = subscriber.Type,
                            DocumentId = subscriber.DocumentId,
                            DocumentName = document.Name,
                            HandlerId = subscriber.HandlerId,
                            Inactive = subscriber.Inactive,
                            MapperId = subscriber.MapperId,
                            Aggregate = subscriber.Aggregate,
                            Temporary = subscriber.Temporary,
                            //Schedules = subscriber.Schedules

                        };

            query = query.AsNoTracking();

            if (lookup)
            {
                return await query.Search(searchyRequest.Conditions).ToDictionaryAsync(k => k.Id.ToString(), v => v.Name);
            }

            var sr = new SearchyResponse<SubscriptionRow>
            {
                TotalCount = await query.Search(searchyRequest.Conditions).CountAsync(),
                Result = await query.Search(searchyRequest.Conditions, searchyRequest.Sorts, searchyRequest.PageSize, searchyRequest.PageIndex).ToListAsync()
            };


            return sr;
        }
    }
}
