using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using SW.EfCoreExtensions;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using SW.Infolink.Domain;

namespace SW.Infolink.Api.Resources.AccessKeySets
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
            var sr = new SearchyResponse<AccessKeySetRow>();

            var query = from accessKeySet in dbContext.Set<AccessKeySet>()

                        select new AccessKeySetRow
                        {
                            Id = accessKeySet.Id,
                            Name = accessKeySet.Name,
                            Key1 = accessKeySet.Key1,
                            Key2 = accessKeySet.Key2
                        };

            sr.TotalCount = await query.AsNoTracking().Search(searchyRequest.Conditions).CountAsync();

            var results = query.AsNoTracking().Search(searchyRequest.Conditions, searchyRequest.Sorts, searchyRequest.PageSize, searchyRequest.PageIndex);

            sr.Result = await results.ToListAsync();
            return sr;
        }
    }
}
