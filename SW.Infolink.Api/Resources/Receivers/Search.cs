using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using SW.EfCoreExtensions;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using SW.Infolink.Domain;

namespace SW.Infolink.Api.Resources.Receivers
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
            var sr = new SearchyResponse<ReceiverRow>();

            var query = from receiver in dbContext.Set<Receiver>()
                        join adapter in dbContext.Set<Adapter>() on receiver.ReceiverId equals adapter.Id into ar
                        from adapter in ar.DefaultIfEmpty()
                        select new ReceiverRow
                        {
                            Id = receiver.Id,
                            Name = receiver.Name,
                            ReceiverId = receiver.ReceiverId,
                            Properties = receiver.Properties.ToDictionary(),
                            ReceiveOn = receiver.ReceiveOn,
                            //Schedules = receiver.Schedules,
                            AdapterName = adapter.Name
                        };

            sr.TotalCount = await query.AsNoTracking().Search(searchyRequest.Conditions).CountAsync();

            var results = query.AsNoTracking().Search(searchyRequest.Conditions, searchyRequest.Sorts, searchyRequest.PageSize, searchyRequest.PageIndex);

            sr.Result = await results.ToListAsync();

            return sr;
        }
    }
}
