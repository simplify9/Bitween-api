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

        public Delete(BitweenDbContext dbContext, RequestContext requestContext)
        {
            _dbContext = dbContext;
            _requestContext = requestContext;
        }

        async public Task<object> Handle(int key)
        {
            await _requestContext.EnsurePermission(_dbContext, Model.Permissions.Documents.Delete);

            await _dbContext.DeleteByKeyAsync<Document>(key);
            return null;
        }
    }
}