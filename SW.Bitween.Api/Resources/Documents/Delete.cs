using SW.EfCoreExtensions;
using SW.Bitween.Domain;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SW.Bitween.Resources.Documents
{
    public class Delete : IDeleteHandler<int,object>
    {
        private readonly BitweenDbContext _dbContext;
        private readonly RequestContext _requestContext;
        private readonly IInfolinkCache _cache;

        public Delete(BitweenDbContext dbContext, RequestContext requestContext, IInfolinkCache cache)
        {
            _dbContext = dbContext;
            _requestContext = requestContext;
            _cache = cache;
        }

        async public Task<object> Handle(int key)
        {
            await _requestContext.EnsurePermission(_dbContext, Model.Permissions.Documents.Delete);

            await _dbContext.DeleteByKeyAsync<Document>(key);
            await _cache.BroadcastRevoke();
            return null;
        }
    }
}