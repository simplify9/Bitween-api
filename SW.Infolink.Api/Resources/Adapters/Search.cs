using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using SW.Infolink.Domain;
using SW.EfCoreExtensions;

namespace SW.Infolink.Api.Resources.Adapters
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
            var sr = new SearchyResponse<AdapterRow>();

            var query = from e in dbContext.Set<Adapter>()
                        select new AdapterRow
                        {
                            Id = e.Id,
                            Name = e.Name,
                            Properties = e.Properties.ToDictionary(),
                            Timeout = e.Timeout,
                            Type = e.Type,
                            Description = e.Description,
                            //DocumentId = e.DocumentId,
                            ServerlessId = e.ServerlessId
                        };

            sr.TotalCount = await query.AsNoTracking().Search(searchyRequest.Conditions).CountAsync();

            var results = query.AsNoTracking().Search(searchyRequest.Conditions, searchyRequest.Sorts, searchyRequest.PageSize, searchyRequest.PageIndex);

            sr.Result = await results.ToListAsync();
            return sr;
        }
    }
}
