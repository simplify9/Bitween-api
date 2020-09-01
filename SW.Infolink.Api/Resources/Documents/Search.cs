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

namespace SW.Infolink.Api.Resources.Documents
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
            var query = from document in dbContext.Set<Document>()

                        select new DocumentRow
                        {
                            Id = document.Id,
                            Name = document.Name,
                            BusMessageTypeName = document.BusMessageTypeName,
                            BusEnabled = document.BusEnabled,
                            DuplicateInterval = document.DuplicateInterval,
                            PromotedProperties = document.PromotedProperties.ToDictionary()
                        };

            var searchyResponse = new SearchyResponse<DocumentRow>
            {
                Result = await query.AsNoTracking().Search(searchyRequest.Conditions, searchyRequest.Sorts, searchyRequest.PageSize, searchyRequest.PageIndex).ToListAsync(),
                TotalCount = await query.AsNoTracking().Search(searchyRequest.Conditions).CountAsync()
            };

            return searchyResponse;
        }
    }
}
