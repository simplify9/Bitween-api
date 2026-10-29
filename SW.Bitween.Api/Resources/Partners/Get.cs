using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.PrimitiveTypes;
using SW.EfCoreExtensions;
using System.Linq;
using System.Threading.Tasks;
using SW.Bitween.Model;

namespace SW.Bitween.Resources.Partners
{
    /// <summary>A partner as its page reads it: the update model, and when each key was last used.</summary>
    public class PartnerDetail : PartnerUpdate
    {
        /// <summary>By key name. A key missing here hasn't had a call through since this was recorded.</summary>
        public System.Collections.Generic.Dictionary<string, System.DateTime> KeysLastUsedOn { get; set; } = [];
    }

    public class Get(BitweenDbContext dbContext, RequestContext requestContext) : IGetHandler<int,object>
    {
        private readonly BitweenDbContext dbContext = dbContext;
        private readonly RequestContext requestContext = requestContext;

        async public Task<object> Handle(int key)
        {
            await requestContext.EnsurePermission(dbContext, Model.Permissions.Partners.View);

            var partner = await dbContext.Set<Partner>().AsNoTracking().
                Search("Id", key).
                Select(partner => new PartnerDetail
                {
                    Name = partner.Name,

                    ApiCredentials = partner.ApiCredentials.Select(cred => new KeyAndValue
                    {
                        Key = cred.Name,
                        // Keys are stored hashed, so the prefix kept beside the hash is all there is
                        // to show. A short key used to throw here, too: Remove(5) on fewer than five.
                        Value = (cred.KeyPrefix ?? "") + "...(hidden)"
                    }).ToList(),

                    Subscriptions = partner.Subscriptions.Select(sub => new SubscriptionSearch
                    {
                        Id = sub.Id,
                        Name = sub.Name,
                        Type = sub.Type,
                        DocumentId = sub.DocumentId,

                    }).ToList(),

                    AdapterProperties = partner.AdapterProperties,
                    SecretProperties = partner.SecretProperties,
                    LoginIdentity = partner.LoginIdentity

                }).AsNoTracking().SingleOrDefaultAsync();

            // The names come back so the form can draw the locks; the values behind them do not.
            if (partner != null)
            {
                partner.AdapterProperties =
                    AdapterSecretProperties.Mask(partner.AdapterProperties, partner.SecretProperties);
                partner.KeysLastUsedOn = await dbContext.Set<ApiKeyUse>().AsNoTracking()
                    .Where(u => u.PartnerId == key)
                    .ToDictionaryAsync(u => u.KeyName, u => u.LastUsedOn);
            }

            return partner;
        }
    }
}
