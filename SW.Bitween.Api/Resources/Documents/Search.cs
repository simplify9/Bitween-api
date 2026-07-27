using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using SW.EfCoreExtensions;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Model;

namespace SW.Bitween.Resources.Documents
{
    public class Search : ISearchyHandler
    {
        private readonly BitweenDbContext dbContext;
        private readonly RequestContext requestContext;

        public Search(BitweenDbContext dbContext, RequestContext requestContext)
        {
            this.dbContext = dbContext;
            this.requestContext = requestContext;
        }

        async public Task<object> Handle(SearchyRequest searchyRequest, bool lookup = false, string searchPhrase = null)
        {
            // Lookup returns only id/name pairs, which pickers across the app rely on;
            // the full list is the data, so that's what the view permission covers.
            if (!lookup)
                await requestContext.EnsurePermission(dbContext, Model.Permissions.Documents.View);

            var query = from document in dbContext.Set<Document>()

                        select new DocumentRow
                        {
                            Id = document.Id,
                            Code = document.Code,
                            Name = document.Name,
                            BusMessageTypeName = document.BusMessageTypeName,
                            BusEnabled = document.BusEnabled,
                            DuplicateInterval = document.DuplicateInterval,
                            PromotedProperties = document.PromotedProperties.ToKeyAndValueCollection(),
                            DocumentFormat = document.DocumentFormat
                        };

            query = query.AsNoTracking();

            if (lookup)
            {
                return await query.Search(searchyRequest.Conditions).ToDictionaryAsync(k => k.Id.ToString(), v => v.Name);
            }

            var searchyResponse = new SearchyResponse<DocumentRow>
            {
                Result = await query.Search(searchyRequest.Conditions, searchyRequest.Sorts, searchyRequest.PageSize, searchyRequest.PageIndex).ToListAsync(),
                TotalCount = await query.Search(searchyRequest.Conditions).CountAsync()
            };

            return searchyResponse;
        }
    }
}
