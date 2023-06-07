using SW.EfCoreExtensions;
using SW.Infolink.Domain;
using SW.Infolink.Model;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SW.Infolink.Domain.Accounts;

namespace SW.Infolink.Resources.Partners
{
    class Update : ICommandHandler<int, PartnerUpdate>
    {
        private readonly InfolinkDbContext _dbContext;
        private readonly RequestContext _requestContext;


        public Update(InfolinkDbContext dbContext, RequestContext requestContext)
        {
            _dbContext = dbContext;
            _requestContext = requestContext;
        }

        public async Task<object> Handle(int key, PartnerUpdate model)
        {
            _requestContext.EnsureAccess(AccountRole.Admin, AccountRole.Member);

            var entity = await _dbContext.FindAsync<Partner>(key);
            entity.SetApiCredentials(model.ApiCredentials.Select(kv => new ApiCredential(kv.Key, kv.Value)));
            _dbContext.Entry(entity).SetProperties(model);
            await _dbContext.SaveChangesAsync();
            return null;
        }
    }
}