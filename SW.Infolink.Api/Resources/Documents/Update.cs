using SW.Infolink.Domain;
using SW.Infolink.Model;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SW.Infolink.Api.Resources.Documents
{
    class Update : ICommandHandler<int, DocumentConfig>
    {
        private readonly InfolinkDbContext dbContext;

        public Update(InfolinkDbContext dbContext)
        {
            this.dbContext = dbContext;
        }

        async public Task<object> Handle(int key, DocumentConfig model)
        {
            var newEntity = new Document(key, model.Name)
            {
                BusEnabled = model.BusEnabled,
                BusMessageTypeName = model.BusMessageTypeName,
                DuplicateInterval = model.DuplicateInterval,
                PromotedProperties = model.PromotedProperties.ToDictionary()
            };
            await dbContext.CreateOrUpdate(key, model, newEntity);
            return null;
        }
    }
}
