using Microsoft.EntityFrameworkCore;
using SW.EfCoreExtensions;
using SW.Infolink.Domain;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SW.Infolink.Api.Resources.Adapters
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
            return await dbContext.Set<Adapter>().
                Search("Id", key).
                Select(adapter => new AdapterConfig
                {
                    Description = adapter.Description,
                    DocumentId = adapter.DocumentId,
                    //Hash = adapter.Hash,
                    Name = adapter.Name,
                    //Package = null,
                    Properties = adapter.Properties.ToDictionary(),
                    Timeout = adapter.Timeout,
                    Type = adapter.Type,
                    ServerlessId = adapter.ServerlessId


                }).AsNoTracking().SingleOrDefaultAsync();
        }
    }
}
