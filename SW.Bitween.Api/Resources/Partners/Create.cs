using System.Linq;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using System.Threading.Tasks;

namespace SW.Bitween.Resources.Partners
{
public class Create(BitweenDbContext dbContext, RequestContext requestContext)
        : ICommandHandler<PartnerCreate,object>
    {
        public async Task<object> Handle(PartnerCreate model)
        {
            await requestContext.EnsurePermission(dbContext, Model.Permissions.Partners.Create);

            PartnerValidation.EnsureAcceptedResponseStatusCode(model.AcceptedResponseStatusCode);

            var entity = new Partner(model.Name);
            // Same field the update handler writes, applied in the same transaction as
            // the insert, so a partner is never created half-configured.
            if (model.AdapterProperties != null)
                entity.AdapterProperties = model.AdapterProperties;
            entity.SecretProperties = model.SecretProperties?.ToList() ?? [];
            entity.AcceptedResponseStatusCode = model.AcceptedResponseStatusCode;
            dbContext.Add(entity);
            await dbContext.SaveChangesAsync();
            return entity.Id;
        }
    }
}