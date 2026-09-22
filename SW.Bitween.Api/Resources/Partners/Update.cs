using SW.EfCoreExtensions;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SW.Bitween.Resources.Partners
{
public class Update(BitweenDbContext dbContext, RequestContext requestContext)
        : ICommandHandler<int, PartnerUpdate,object>
    {
        public async Task<object> Handle(int key, PartnerUpdate model)
        {
            await requestContext.EnsurePermission(dbContext, Model.Permissions.Partners.Edit);

            var entity = await dbContext.FindAsync<Partner>(key);
            entity.SetApiCredentials(model.ApiCredentials.Select(kv => new ApiCredential(kv.Key, kv.Value)));

            // Read while they are still there: SetProperties copies the model's own
            // AdapterProperties over the entity's, so the stored values are gone after it runs.
            var storedProperties = entity.AdapterProperties;
            dbContext.Entry(entity).SetProperties(model);

            // A secret left untouched arrives as the sentinel, because that is all Get sent. The
            // page saves every property together, so without this an edit to the partner's name
            // would overwrite its password with a row of dots. After SetProperties, which would
            // otherwise put the mask straight back.
            entity.AdapterProperties =
                AdapterSecretProperties.Merge(storedProperties, model.AdapterProperties);
            entity.SecretProperties = model.SecretProperties?.ToList() ?? [];
            await dbContext.SaveChangesAsync();
            return null;
        }
    }
}