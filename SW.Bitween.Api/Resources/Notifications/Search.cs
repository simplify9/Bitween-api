using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.EfCoreExtensions;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Notifications
{
    public class Search:ISearchyHandler
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

            var query = from notification in dbContext.Set<XchangeNotification>()
                select new NotificationsSearch()
                {
                    Id = notification.Id,
                    Success = notification.Success,
                    Exception = notification.Exception,
                    FinishedOn = notification.FinishedOn,
                    NotifierName = notification.NotifierName,
                    XchangeId = notification.XchangeId
                };

            query = query.AsNoTracking();

            return new SearchyResponse<NotificationsSearch>
            {
                TotalCount = await query.Search(searchyRequest.Conditions).CountAsync(),
                Result = await query.OrderByDescending(p => p.FinishedOn).Search(searchyRequest.Conditions, searchyRequest.Sorts, searchyRequest.PageSize, searchyRequest.PageIndex).ToListAsync()
            };
        }
    }
}