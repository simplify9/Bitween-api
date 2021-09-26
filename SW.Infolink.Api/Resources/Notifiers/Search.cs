using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.EfCoreExtensions;
using SW.Infolink.Domain;
using SW.Infolink.Model;
using SW.PrimitiveTypes;

namespace SW.Infolink.Resources.Notifiers
{
    public class Search: ISearchyHandler
    {
        private readonly InfolinkDbContext dbContext;

        public Search(InfolinkDbContext dbContext)
        {
            this.dbContext = dbContext;
        }
        
        public async Task<object> Handle(SearchyRequest searchyRequest, bool lookup = false, string searchPhrase = null)
        {
            var query = from notifier in dbContext.Set<Notifier>()
                select new NotifierSearch()
                {
                    Id = notifier.Id,
                    Name=notifier.Name,
                    HandlerId = notifier.HandlerId,
                    RunOnBadResult = notifier.RunOnBadResult,
                    RunOnFailedResult = notifier.RunOnFailedResult,
                    RunOnSuccessfulResult = notifier.RunOnSuccessfulResult,
                    Inactive = notifier.Inactive
                };

            query = query.AsNoTracking();

            if (lookup)
            {
                return await query.Search(searchyRequest.Conditions).ToDictionaryAsync(k => k.Id.ToString(), v => v.Name);
            }
            
            return new SearchyResponse<NotifierSearch>
            {
                TotalCount = await query.Search(searchyRequest.Conditions).CountAsync(),
                Result = await query.Search(searchyRequest.Conditions, searchyRequest.Sorts, searchyRequest.PageSize, searchyRequest.PageIndex).ToListAsync()
            };

        }
    }
}