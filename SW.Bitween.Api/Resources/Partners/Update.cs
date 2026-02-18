using SW.EfCoreExtensions;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SW.Bitween.Domain.Accounts;

namespace SW.Bitween.Resources.Partners
{
    public class Update : ICommandHandler<int, PartnerUpdate,object>
    {
        private readonly BitweenDbContext _dbContext;
        private readonly RequestContext _requestContext;


        public Update(BitweenDbContext dbContext, RequestContext requestContext)
        {
            _dbContext = dbContext;
            _requestContext = requestContext;
        }

        public async Task<object> Handle(int key, PartnerUpdate model)
        {
            _requestContext.EnsureAccess(AccountRole.Admin, AccountRole.Member);

            var entity = await _dbContext.FindAsync<Partner>(key);
            entity.SetApiCredentials(model.ApiCredentials.Select(kv => new ApiCredential(kv.Key, kv.Value)));
            entity.AdapterProperties = model.AdapterProperties;
            _dbContext.Entry(entity).SetProperties(model);
            await _dbContext.SaveChangesAsync();
            return null;
        }
    }
}