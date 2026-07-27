using SW.Bitween.Domain.Gateway;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using System.Threading.Tasks;

namespace SW.Bitween.Resources.ApiGateways
{
    public class Create : ICommandHandler<ApiGatewayCreate, object>
    {
        private readonly BitweenDbContext _dbContext;
        private readonly RequestContext _requestContext;

        public Create(BitweenDbContext dbContext, RequestContext requestContext)
        {
            _dbContext = dbContext;
            _requestContext = requestContext;
        }

        public async Task<object> Handle(ApiGatewayCreate model)
        {
            await _requestContext.EnsurePermission(_dbContext, Model.Permissions.ApiGateways.Create);

            if (string.IsNullOrWhiteSpace(model.UrlName))
                throw new SWException("UrlName is required");

            var entity = new ApiGateway
            {
                Name = model.Name,
                UrlName = model.UrlName
            };

            _dbContext.Add(entity);
            await _dbContext.SaveChangesAsync();
            return entity.Id;
        }
    }
}

