using SW.EfCoreExtensions;
using SW.Bitween.Domain;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using SW.Bitween.Domain.Accounts;

namespace SW.Bitween.Resources.Documents
{
    public class Delete : IDeleteHandler<int,object>
    {
        private readonly BitweenDbContext _dbContext;
        private readonly RequestContext _requestContext;

        public Delete(BitweenDbContext dbContext, RequestContext requestContext)
        {
            _dbContext = dbContext;
            _requestContext = requestContext;
        }

        async public Task<object> Handle(int key)
        {
            _requestContext.EnsureAccess(AccountRole.Admin, AccountRole.Member);

            await _dbContext.DeleteByKeyAsync<Document>(key);
            return null;
        }
    }
}