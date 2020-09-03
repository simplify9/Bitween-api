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
            var entity = new Document(model.Id, model.Name);
            dbContext.Add(new Document(model.Id, model.Name));
            await dbContext.SaveChangesAsync();
            return entity.Id;
        }
    }
}
