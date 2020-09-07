using SW.EfCoreExtensions;
using SW.Infolink.Domain;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SW.Infolink.Api.Resources.Documents
{
    class Delete : IDeleteHandler<int>
    {
        private readonly InfolinkDbContext dbContext;

        public Delete(InfolinkDbContext dbContext)
        {
            this.dbContext = dbContext;
        }

        async public Task<object> Handle(int key)
        {
            await dbContext.DeleteByKeyAsync<Document>(key);
            return null;
        }
    }
}
