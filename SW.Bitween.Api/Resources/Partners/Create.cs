using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using System.Threading.Tasks;
using SW.Bitween.Domain.Accounts;

namespace SW.Bitween.Resources.Partners
{
    class Create : ICommandHandler<PartnerCreate,object>
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
            _requestContext.EnsureAccess(AccountRole.Admin, AccountRole.Member);

            var entity = new Partner(model.Name);
            _dbContext.Add(entity);
            await _dbContext.SaveChangesAsync();
            return entity.Id;
        }
    }
}