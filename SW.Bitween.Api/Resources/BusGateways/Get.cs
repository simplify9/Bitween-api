using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Gateway;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using System.Linq;
using System.Threading.Tasks;

namespace SW.Bitween.Resources.BusGateways
{
    public class Get(BitweenDbContext dbContext, RequestContext requestContext) : IGetHandler<int, object>
    {
        public async Task<object> Handle(int key)
        {
            await requestContext.EnsurePermission(dbContext, Model.Permissions.BusGateways.View);

            var gateway = await dbContext.Set<BusGateway>()
                .AsNoTracking()
                .Include(bg => bg.Routes)
                    .ThenInclude(r => r.Subscription)
                .Include(bg => bg.Routes)
                    .ThenInclude(r => r.Partner)
                .FirstOrDefaultAsync(bg => bg.Id == key);

            if (gateway == null)
                throw new SWNotFoundException($"BusGateway with id '{key}' was not found");

            var documentName = await dbContext.Set<Document>()
                .Where(d => d.Id == gateway.DocumentId)
                .Select(d => d.Name)
                .FirstOrDefaultAsync();

            var dataSource = gateway.DataSourceId == null
                ? null
                : await dbContext.Set<Domain.DataSources.DataSource>()
                    .AsNoTracking()
                    .Where(d => d.Id == gateway.DataSourceId)
                    .Select(d => new { d.Name, d.LastKnownState, d.SecretProperties })
                    .FirstOrDefaultAsync();

            return new BusGatewayRow
            {
                Id = gateway.Id,
                Name = gateway.Name,
                DocumentId = gateway.DocumentId,
                Inactive = gateway.Inactive,
                DocumentName = documentName,
                DataSourceId = gateway.DataSourceId,
                DataSourceName = dataSource?.Name,
                DataSourceState = dataSource?.LastKnownState,
                Endpoint = gateway.Endpoint,
                // Masked as the data source's own properties are: what it declares secret, and any
                // well-known credential name. Saving the sentinel back keeps the stored value.
                EndpointProperties = DataSources.Secrets.Mask(gateway.EndpointProperties, dataSource?.SecretProperties),
                RoutesCount = gateway.Routes.Count,
                Routes = gateway.Routes.Select(r => new BusGatewayRouteDto
                {
                    Id = r.Id,
                    SubscriptionId = r.SubscriptionId,
                    SubscriptionName = r.Subscription != null ? r.Subscription.Name : null,
                    PartnerId = r.PartnerId,
                    PartnerName = r.Partner != null ? r.Partner.Name : null,
                    MatchExpression = r.MatchExpression
                }).ToList()
            };
        }
    }
}
