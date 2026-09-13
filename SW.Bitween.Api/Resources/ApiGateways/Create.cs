using SW.Bitween.Domain.Gateway;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using System.Threading.Tasks;

namespace SW.Bitween.Resources.ApiGateways
{
public class Create(BitweenDbContext dbContext, RequestContext requestContext)
        : ICommandHandler<ApiGatewayCreate, object>
    {
        public async Task<object> Handle(ApiGatewayCreate model)
        {
            await requestContext.EnsurePermission(dbContext, Model.Permissions.ApiGateways.Create);

            GatewayUrlName.Validate(model.UrlName);
            await GatewayUrlName.EnsureIsFree(dbContext, model.UrlName);

            var entity = new ApiGateway
            {
                Name = model.Name,
                UrlName = model.UrlName,
                Inactive = model.Inactive
            };

            dbContext.Add(entity);
            await dbContext.SaveChangesAsync();
            return entity.Id;
        }
    }
}

