using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.EfCoreExtensions;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Notifications
{
    public class Search(BitweenDbContext dbContext, RequestContext requestContext) : ISearchyHandler
    {
        private readonly BitweenDbContext dbContext = dbContext;
        private readonly RequestContext requestContext = requestContext;

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
                    Suppressed = notification.Suppressed,
                    Exception = notification.Exception,
                    FinishedOn = notification.FinishedOn,
                    NotifierName = notification.NotifierName,
                    XchangeId = notification.XchangeId,
                    ChannelId = notification.ChannelId
                };

            query = query.AsNoTracking();

            var page = await query.OrderByDescending(p => p.FinishedOn)
                .Search(searchyRequest.Conditions, searchyRequest.Sorts, searchyRequest.PageSize, searchyRequest.PageIndex)
                .ToListAsync();

            // Which subscription each send was about, for this page alone: joined into the query, it
            // was paid for on every row the count went through. A send can outlive its exchange,
            // and still happened, so one without stays in the list unnamed.
            var xchangeIds = page.Select(p => p.XchangeId).Where(id => id != null).Distinct().ToList();
            var about = await (from xchange in dbContext.Set<Xchange>().AsNoTracking()
                    where xchangeIds.Contains(xchange.Id)
                    join subscription in dbContext.Set<Subscription>() on xchange.SubscriptionId equals (int?)subscription.Id
                    select new { xchange.Id, SubscriptionId = subscription.Id, subscription.Name })
                .ToDictionaryAsync(x => x.Id);
            foreach (var row in page)
                if (row.XchangeId != null && about.TryGetValue(row.XchangeId, out var subscription))
                    (row.SubscriptionId, row.SubscriptionName) = (subscription.SubscriptionId, subscription.Name);

            return new SearchyResponse<NotificationsSearch>
            {
                TotalCount = await query.Search(searchyRequest.Conditions).CountAsync(),
                Result = page
            };
        }
    }
}