using SW.EfCoreExtensions;
using SW.Infolink.Domain;
using SW.Infolink.Model;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SW.Infolink.Api.Resources.Documents
{
    class Create : ICommandHandler<DocumentConfig>
    {
        private readonly InfolinkDbContext dbContext;

        public Create(InfolinkDbContext dbContext)
        {
            this.dbContext = dbContext;
        }

        async public Task<object> Handle(DocumentConfig model)
        {
            var entity = new Document(model.Id, model.Name)
            {
                //Aggregate = model.Aggregate,
                //DocumentFilter = model.DocumentFilter.ToDictionary(),
                //HandlerId = model.HandlerId,
                //Inactive = model.Inactive,
                ////KeySetId = model.KeySetId,
                //MapperId = model.MapperId,
                //Properties = model.Properties.ToDictionary(),
                //Temporary = model.Temporary,
                //ResponseSubscriberId = model.ResponseSubscriberId

            };

            dbContext.Add(entity);
            await dbContext.SaveChangesAsync();

            return entity.Id;
        }
    }
}
