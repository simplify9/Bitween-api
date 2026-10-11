using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Gateway;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using System.Threading.Tasks;

namespace SW.Bitween.Resources.BusGateways
{
public class Create(BitweenDbContext dbContext, RequestContext requestContext, IInfolinkCache cache)
        : ICommandHandler<BusGatewayCreate, object>
    {
        private readonly BitweenDbContext _dbContext = dbContext;

        public async Task<object> Handle(BusGatewayCreate model)
        {
            await requestContext.EnsurePermission(_dbContext, Model.Permissions.BusGateways.Create);

            var documentExists = await _dbContext.Set<Document>().AnyAsync(d => d.Id == model.DocumentId);
            if (!documentExists)
                throw new SWNotFoundException($"Document with Id {model.DocumentId} not found");

            await EnsureDataSourceAsync(_dbContext, model);

            var entity = new BusGateway
            {
                Name = model.Name,
                DocumentId = model.DocumentId,
                Inactive = model.Inactive,
                DataSourceId = model.DataSourceId,
                Endpoint = model.DataSourceId == null ? null : model.Endpoint,
                EndpointProperties = model.EndpointProperties ?? new()
            };

            _dbContext.Add(entity);
            await _dbContext.SaveChangesAsync();
            await cache.BroadcastRevoke();
            return entity.Id;
        }

        /// <summary>
        /// Shared by Create and Update. An endpoint is what the supervisor turns into the adapter's
        /// consume list, so an external gateway without one is a gateway that can never receive
        /// anything - and it would fail silently, which is the worst way for it to fail.
        /// </summary>
        internal static async Task EnsureDataSourceAsync(BitweenDbContext dbContext, BusGatewayCreate model)
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

            // Two gateways consuming one endpoint on one data source would both be offered every
            // message, and only the one the sink happens to pick would ever run.
            var endpointTaken = await dbContext.Set<BusGateway>()
                .AnyAsync(g => g.DataSourceId == model.DataSourceId && g.Endpoint == model.Endpoint);
            if (endpointTaken)
                throw new SWException(
                    $"Another bus gateway on this data source already reads '{model.Endpoint}'.");
        }

        private class Validate : AbstractValidator<BusGatewayCreate>
        {
            public Validate()
            {
                RuleFor(i => i.Name).NotEmpty().MaximumLength(200);
                RuleFor(i => i.Endpoint).MaximumLength(500);
            }
        }
    }
}
