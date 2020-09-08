using SW.EfCoreExtensions;
using SW.Infolink.Domain;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SW.Infolink.Resources.Partners
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
            if (key == Partner.SystemId)
                throw new SWException("System partner can not be deleted.");

            await dbContext.DeleteByKeyAsync<Partner>(key);
            return null;
        }
    }
}
