using SW.EfCoreExtensions;
using SW.Bitween.Domain;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using SW.Bitween.Domain.Accounts;

namespace SW.Bitween.Resources.Subscriptions
{
    public class Delete : IDeleteHandler<int,object>
    {
        private readonly BitweenDbContext _dbContext;
        private readonly RequestContext _requestContext;


        public Delete(BitweenDbContext dbContext, RequestContext requestContext)
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