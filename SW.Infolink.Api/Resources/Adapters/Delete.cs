using Microsoft.EntityFrameworkCore;
using SW.Infolink.Domain;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SW.Infolink.Api.Resources.Adapters
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
            dbContext.Database.SetCommandTimeout(TimeSpan.FromMinutes(5));
            await dbContext.Delete<Adapter>(key);
            return null;
        }
    }
}
