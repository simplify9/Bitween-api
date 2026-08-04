using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using System.Threading.Tasks;

namespace SW.Bitween.Resources.Partners
{
    public class Create : ICommandHandler<PartnerCreate,object>
    {
        private readonly BitweenDbContext _dbContext;
        private readonly RequestContext _requestContext;

        public Create(BitweenDbContext dbContext, RequestContext requestContext)
        {
            this._dbContext = dbContext;
            _requestContext = requestContext;
        }

        public async Task<object> Handle(PartnerCreate model)
        {
            await _requestContext.EnsurePermission(_dbContext, Model.Permissions.Partners.Create);

            var entity = new Partner(model.Name);
            // Same field the update handler writes, applied in the same transaction as
            // the insert, so a partner is never created half-configured.
            if (model.AdapterProperties != null)
                entity.AdapterProperties = model.AdapterProperties;
            _dbContext.Add(entity);
            await _dbContext.SaveChangesAsync();
            return entity.Id;
        }
    }
}