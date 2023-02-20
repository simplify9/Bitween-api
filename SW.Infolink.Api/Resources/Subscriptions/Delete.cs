using SW.EfCoreExtensions;
using SW.Infolink.Domain;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using SW.Infolink.Domain.Accounts;

namespace SW.Infolink.Resources.Subscriptions
{
    class Delete : IDeleteHandler<int>
    {
        private readonly InfolinkDbContext _dbContext;
        private readonly RequestContext _requestContext;


        public Delete(InfolinkDbContext dbContext, RequestContext requestContext)
        {
            this._dbContext = dbContext;
            _requestContext = requestContext;
        }

        public async Task<object> Handle(int key)
        {
            _requestContext.EnsureAccess(AccountRole.Admin, AccountRole.Viewer);

            await _dbContext.DeleteByKeyAsync<Subscription>(key);
            return null;
        }
    }
}