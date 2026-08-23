using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain.Gateway;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using System.Threading.Tasks;

namespace SW.Bitween.Resources.BusGateways
{
    public class Update : ICommandHandler<int, BusGatewayUpdate, object>
    {
        private readonly BitweenDbContext _dbContext;
        private readonly RequestContext _requestContext;
        private readonly IInfolinkCache _cache;

        public Update(BitweenDbContext dbContext, RequestContext requestContext, IInfolinkCache cache)
        {
            _dbContext = dbContext;
            _requestContext = requestContext;
            _cache = cache;
        }

        public async Task<object> Handle(int key, BusGatewayUpdate model)
        {
            await _requestContext.EnsurePermission(_dbContext, Model.Permissions.BusGateways.Edit);

            var entity = await _dbContext.Set<BusGateway>()
                .FirstOrDefaultAsync(bg => bg.Id == key);

            if (entity == null)
                throw new SWNotFoundException($"BusGateway with Id {key} not found");

            // Name only; the bound document is fixed at creation (routes' subscriptions belong to it).
            entity.Name = model.Name;
            entity.Inactive = model.Inactive;

            await _dbContext.SaveChangesAsync();
            await _cache.BroadcastRevoke();
            return null;
        }

        private class Validate : AbstractValidator<BusGatewayUpdate>
        {
            public Validate()
            {
                RuleFor(i => i.Name).NotEmpty().MaximumLength(200);
            }
        }
    }
}
