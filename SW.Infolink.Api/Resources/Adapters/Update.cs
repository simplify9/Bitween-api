using Microsoft.EntityFrameworkCore;
using SW.EfCoreExtensions;
using SW.Infolink.Domain;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SW.Infolink.Api.Resources.Adapters
{
    [HandlerName("update")]
    class Update : ICommandHandler<int, AdapterConfig>
    {
        private readonly InfolinkDbContext dbContext;

        public Update(InfolinkDbContext dbContext)
        {
            this.dbContext = dbContext;
        }

        async public Task<object> Handle(int key, AdapterConfig model)
        {
            var entity = await dbContext.FindAsync<Adapter>(key);
            //if (model.Package != null)
            //{
            //    entity.UpdatePackage(model.Package);
            //    dbContext.Database.SetCommandTimeout(TimeSpan.FromMinutes(5));
            //}
            dbContext.Entry(entity).SetProperties(model);
            await dbContext.SaveChangesAsync(); 
            return null;
        }
    }
}
