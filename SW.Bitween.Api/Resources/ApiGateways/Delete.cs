using SW.EfCoreExtensions;
using SW.Bitween.Domain.Gateway;
using SW.PrimitiveTypes;
using System.Threading.Tasks;
using SW.Bitween.Domain.Accounts;

namespace SW.Bitween.Resources.ApiGateways
{
    public class Delete : IDeleteHandler<int, object>
    {
        private readonly BitweenDbContext _dbContext;
        private readonly RequestContext _requestContext;

        public Delete(BitweenDbContext dbContext, RequestContext requestContext)
        {
            _dbContext = dbContext;
            _requestContext = requestContext;
        }

        public async Task<object> Handle(int key)
        {
            _requestContext.EnsureAccess(AccountRole.Admin, AccountRole.Member);

            await _dbContext.DeleteByKeyAsync<ApiGateway>(key);
            return null;
        }
    }
}

