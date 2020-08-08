using SW.Infolink.Domain;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SW.Infolink.Api.Resources.AccessKeySets
{
    [HandlerName("update")]
    class Update : ICommandHandler<int, AccessKeySetConfig>
    {
        private readonly InfolinkDbContext dbContext;

        public Update(InfolinkDbContext dbContext)
        {
            this.dbContext = dbContext;
        }

        async public Task<object> Handle(int key, AccessKeySetConfig request)
        {
            await dbContext.Update<AccessKeySet>(key, request);
            return null;
        }
    }
}
