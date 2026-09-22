using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.PrimitiveTypes;
using SW.EfCoreExtensions;
using System.Linq;
using System.Threading.Tasks;
using SW.Bitween.Model;

namespace SW.Bitween.Resources.Partners
{
    public class Get(BitweenDbContext dbContext, RequestContext requestContext) : IGetHandler<int,object>
    {
        private readonly BitweenDbContext dbContext = dbContext;
        private readonly RequestContext requestContext = requestContext;

        async public Task<object> Handle(int key)
        {
            await requestContext.EnsurePermission(dbContext, Model.Permissions.Partners.View);

            var partner = await dbContext.Set<Partner>().AsNoTracking().
                Search("Id", key).
                Select(partner => new PartnerUpdate
                {
                    Name = partner.Name,

                    ApiCredentials = partner.ApiCredentials.Select(cred => new KeyAndValue
                    {
                        Key = cred.Name,
                        Value = $"{cred.Key.Remove(5)}...(hidden)"
                    }).ToList(),

                    Subscriptions = partner.Subscriptions.Select(sub => new SubscriptionSearch
                    {
                        Id = sub.Id,
                        Name = sub.Name,
                        Type = sub.Type,
                        DocumentId = sub.DocumentId,

                    }).ToList(),

                    AdapterProperties = partner.AdapterProperties,
                    SecretProperties = partner.SecretProperties

                }).AsNoTracking().SingleOrDefaultAsync();

            // The names come back so the form can draw the locks; the values behind them do not.
            if (partner != null)
                partner.AdapterProperties =
                    AdapterSecretProperties.Mask(partner.AdapterProperties, partner.SecretProperties);

            return partner;
        }
    }
}
