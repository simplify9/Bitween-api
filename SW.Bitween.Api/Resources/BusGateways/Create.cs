using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Gateway;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using System.Threading.Tasks;

namespace SW.Bitween.Resources.BusGateways
{
    public class Create : ICommandHandler<BusGatewayCreate, object>
    {
        private readonly BitweenDbContext _dbContext;
        private readonly RequestContext _requestContext;
        private readonly IInfolinkCache _cache;

        public Create(BitweenDbContext dbContext, RequestContext requestContext, IInfolinkCache cache)
        {
            _dbContext = dbContext;
            _requestContext = requestContext;
            _cache = cache;
        }

        public async Task<object> Handle(BusGatewayCreate model)
        {
            await _requestContext.EnsurePermission(_dbContext, Model.Permissions.BusGateways.Create);

            var documentExists = await _dbContext.Set<Document>().AnyAsync(d => d.Id == model.DocumentId);
            if (!documentExists)
                throw new SWNotFoundException($"Document with Id {model.DocumentId} not found");

            var entity = new BusGateway
            {
                Name = model.Name,
                DocumentId = model.DocumentId,
                Inactive = model.Inactive
            };

            _dbContext.Add(entity);
            await _dbContext.SaveChangesAsync();
            await _cache.BroadcastRevoke();
            return entity.Id;
        }

        private class Validate : AbstractValidator<BusGatewayCreate>
        {
            public Validate()
            {
                RuleFor(i => i.Name).NotEmpty().MaximumLength(200);
            }
        }
    }
}
