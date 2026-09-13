using SW.EfCoreExtensions;
using SW.Bitween.Domain.Gateway;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using System.Linq;

namespace SW.Bitween.Resources.ApiGateways
{
public class Update(BitweenDbContext dbContext, RequestContext requestContext)
        : ICommandHandler<int, ApiGatewayUpdate, object>
    {
        public async Task<object> Handle(int key, ApiGatewayUpdate model)
        {
            await requestContext.EnsurePermission(dbContext, Model.Permissions.ApiGateways.Edit);

            var entity = await dbContext.Set<ApiGateway>()
                .Include(ag => ag.Partners)
                .FirstOrDefaultAsync(ag => ag.Id == key);

            if (entity == null)
                throw new SWNotFoundException($"ApiGateway with Id {key} not found");

            GatewayUrlName.Validate(model.UrlName);
            await GatewayUrlName.EnsureIsFree(dbContext, model.UrlName, key);

            entity.Name = model.Name;
            entity.UrlName = model.UrlName;
            entity.Inactive = model.Inactive;

            await dbContext.SaveChangesAsync();
            return null;
        }
    }
}

