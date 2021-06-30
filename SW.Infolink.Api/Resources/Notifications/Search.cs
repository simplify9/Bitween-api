using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.EfCoreExtensions;
using SW.Infolink.Domain;
using SW.Infolink.Model;
using SW.PrimitiveTypes;

namespace SW.Infolink.Resources.Notifications
{
    public class Search:ISearchyHandler
    {
        private readonly InfolinkDbContext dbContext;

        public Search(InfolinkDbContext dbContext)
        {
            this.dbContext = dbContext;
        }
        
        public async Task<object> Handle(SearchyRequest searchyRequest, bool lookup = false, string searchPhrase = null)
        {
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
                Result = await query.Search(searchyRequest.Conditions, searchyRequest.Sorts, searchyRequest.PageSize, searchyRequest.PageIndex).ToListAsync()
            };
        }
    }
}