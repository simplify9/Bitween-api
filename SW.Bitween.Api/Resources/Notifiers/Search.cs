using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.EfCoreExtensions;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Notifiers
{
    public class Search: ISearchyHandler
    {
        private readonly BitweenDbContext dbContext;
        private readonly RequestContext requestContext;

        public Search(BitweenDbContext dbContext, RequestContext requestContext)
        {
            this.dbContext = dbContext;
            this.requestContext = requestContext;
        }
        
        public async Task<object> Handle(SearchyRequest searchyRequest, bool lookup = false, string searchPhrase = null)
        {
            // Lookup returns only id/name pairs, which pickers across the app rely on;
            // the full list is the data, so that's what the view permission covers.
            if (!lookup)
                await requestContext.EnsurePermission(dbContext, Model.Permissions.Notifiers.View);

            var query = from notifier in dbContext.Set<Notifier>()
                select new NotifierSearch()
                {
                    Id = notifier.Id,
                    Name=notifier.Name,
                    HandlerId = notifier.HandlerId,
                    RunOnBadResult = notifier.RunOnBadResult,
                    RunOnFailedResult = notifier.RunOnFailedResult,
                    RunOnSuccessfulResult = notifier.RunOnSuccessfulResult,
                    Inactive = notifier.Inactive,
                    RunOnSubscriptions = notifier.RunOnSubscriptions
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