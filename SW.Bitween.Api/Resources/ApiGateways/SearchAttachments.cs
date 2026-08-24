using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain.Gateway;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using System.Linq;
using System.Threading.Tasks;

namespace SW.Bitween.Resources.ApiGateways
{
    /// <summary>
    /// Paged, searched view of one gateway's attachments — the full list lives on
    /// <see cref="Get"/> for callers that need every attached partner id (e.g. the
    /// attach-partner picker's exclude list), this is only for the gateway page's own table.
    /// </summary>
    [HandlerName("attachments")]
    public class SearchAttachments : IQueryHandler<SearchApiGatewayAttachmentsModel, object>
    {
        private readonly BitweenDbContext _dbContext;
        private readonly RequestContext _requestContext;

        public SearchAttachments(BitweenDbContext dbContext, RequestContext requestContext)
        {
            _dbContext = dbContext;
            _requestContext = requestContext;
        }

        public async Task<object> Handle(SearchApiGatewayAttachmentsModel request)
        {
            await _requestContext.EnsurePermission(_dbContext, Model.Permissions.ApiGateways.View);

            var offset = request.Offset ?? 0;
            var limit = request.Limit ?? 25;
            var term = request.Search?.Trim();

            var query = _dbContext.Set<ApiGatewayPartner>()
                .AsNoTracking()
                .Where(p => p.ApiGatewayId == request.ApiGatewayId);

            if (!string.IsNullOrEmpty(term))
                query = query.Where(p => p.Partner.Name.Contains(term) || p.Subscription.Name.Contains(term));

            var totalCount = await query.CountAsync();

            var result = await query
                .OrderBy(p => p.Partner.Name)
                .Skip(offset)
                .Take(limit)
                .Select(p => new ApiGatewayPartnerDto
                {
                    PartnerId = p.PartnerId,
                    SubscriptionId = p.SubscriptionId,
                    PartnerName = p.Partner.Name,
                    SubscriptionName = p.Subscription.Name
                })
                .ToListAsync();

            return new
            {
                Result = result,
                TotalCount = totalCount
            };
        }
    }
}
