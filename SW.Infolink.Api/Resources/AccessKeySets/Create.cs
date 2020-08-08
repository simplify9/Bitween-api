using SW.Infolink.Domain;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SW.Infolink.Api.Resources.AccessKeySets
{
    class Create : ICommandHandler<AccessKeySetConfig>
    {
        private readonly InfolinkDbContext dbContext;

        public Create(InfolinkDbContext dbContext)
        {
            this.dbContext = dbContext;
        }

        async public Task<object> Handle(AccessKeySetConfig request)
        {
            var entity = new AccessKeySet(request.Name, request.Key1, request.Key2);
            await dbContext.Create(entity);
            return entity.Id;
        }
    }
}
