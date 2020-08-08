using Microsoft.EntityFrameworkCore;
using SW.Infolink.Domain;
using SW.PrimitiveTypes;
using SW.EfCoreExtensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SW.Infolink.Api.Resources.Documents
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
            return await dbContext.Set<Document>().
                Search("Id", key).
                Select(document => new DocumentConfig
                {
                    Name = document.Name,
                    BusEnabled = document.BusEnabled,
                    BusMessageTypeName = document.BusMessageTypeName,
                    DuplicateInterval = document.DuplicateInterval,
                    PromotedProperties = document.PromotedProperties.ToDictionary()

                }).SingleOrDefaultAsync();
        }
    }
}
