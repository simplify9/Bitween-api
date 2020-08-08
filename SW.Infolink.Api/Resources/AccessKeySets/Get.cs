using Microsoft.EntityFrameworkCore;
using SW.Infolink.Domain;
using SW.PrimitiveTypes;
using SW.EfCoreExtensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SW.Infolink.Api.Resources.AccessKeySets
{
    class Get : IGetHandler<int>
    {
        private readonly InfolinkDbContext dbContext;

        public Get(InfolinkDbContext dbContext)
        {
            this.dbContext = dbContext;
        }

        async public Task<object> Handle(int key, bool lookup = false)
        {
            return await dbContext.Set<AccessKeySet>().
                Search("Id", key).
                Select(entity => new AccessKeySetConfig
                {
                    Name = entity.Name,
                    Key1 = entity.Key1,
                    Key2 = entity.Key2

                }).SingleOrDefaultAsync();
        }
    }
}
