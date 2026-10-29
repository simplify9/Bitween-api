using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain.Gateway;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using System.Threading.Tasks;

namespace SW.Bitween.Resources.BusGateways
{
public class Update(BitweenDbContext dbContext, RequestContext requestContext, IInfolinkCache cache)
        : ICommandHandler<int, BusGatewayUpdate, object>
    {
        private readonly BitweenDbContext _dbContext = dbContext;

        public async Task<object> Handle(int key, BusGatewayUpdate model)
        {
            await requestContext.EnsurePermission(_dbContext, Model.Permissions.BusGateways.Edit);

            var entity = await _dbContext.Set<BusGateway>()
                .FirstOrDefaultAsync(bg => bg.Id == key);

            if (entity == null)
                throw new SWNotFoundException($"BusGateway with Id {key} not found");

            await EnsureEndpointFreeAsync(_dbContext, key, model);

            // The bound document is still fixed at creation - routes' subscriptions belong to it -
            // but where the messages COME from is exactly the thing an operator needs to change
            // without rebuilding the gateway and all of its routes.
            entity.Name = model.Name;
            entity.Inactive = model.Inactive;
            entity.DataSourceId = model.DataSourceId;
            entity.Endpoint = model.DataSourceId == null ? null : model.Endpoint;
            // A secret the form sent back masked keeps what is stored; one never stored is dropped.
            entity.EndpointProperties = DataSources.Secrets.Merge(entity.EndpointProperties, model.EndpointProperties ?? new());

            await _dbContext.SaveChangesAsync();
            await cache.BroadcastRevoke();
            return null;
        }

        private static async Task EnsureEndpointFreeAsync(
            BitweenDbContext dbContext, int key, BusGatewayUpdate model)
        {
            if (model.DataSourceId == null) return;

            // FirstOrDefaultAsync rather than a projection: FluentValidation is in scope here and
            // its own Where extension wins the overload.
            var dataSource = await dbContext.Set<Domain.DataSources.DataSource>()
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == model.DataSourceId);
            if (dataSource == null)
                throw new SWNotFoundException($"DataSource with Id {model.DataSourceId} not found");

            // A data source is not only a broker connection - a resident adapter holding a database
            // session is one too - and only a broker has queues to consume. Reading this from the
            // kind rather than from the adapter id keeps the rule true for providers nobody has
            // written yet.
            if (dataSource.Kind != Domain.DataSources.DataSourceKind.Broker)
                throw new SWException(
                    $"Data source {model.DataSourceId} is a {dataSource.Kind} connection. A bus "
                    + "gateway reads messages from a queue or topic, so it can only use a Broker.");

            if (string.IsNullOrWhiteSpace(model.Endpoint))
                throw new SWException(
                    "An external bus gateway needs an endpoint - the queue, topic or subscription "
                    + "on that data source it reads from.");

            var endpointTaken = await dbContext.Set<BusGateway>()
                .AnyAsync(g => g.DataSourceId == model.DataSourceId
                               && g.Endpoint == model.Endpoint
                               && g.Id != key);
            if (endpointTaken)
                throw new SWException(
                    $"Another bus gateway on this data source already reads '{model.Endpoint}'.");
        }

        private class Validate : AbstractValidator<BusGatewayUpdate>
        {
            public Validate()
            {
                RuleFor(i => i.Name).NotEmpty().MaximumLength(200);
                RuleFor(i => i.Endpoint).MaximumLength(500);
            }
        }
    }
}
