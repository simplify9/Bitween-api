using Microsoft.EntityFrameworkCore;
using SW.Infolink.Domain;
using SW.PrimitiveTypes;
using SW.EfCoreExtensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SW.Infolink.Model;

namespace SW.Infolink.Api.Resources.Receivers
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
            return await dbContext.Set<Receiver>().AsNoTracking().
                Search("Id", key).
                Select(entity => new ReceiverConfig
                {
                    Name = entity.Name,
                    Properties = entity.Properties.ToDictionary(),
                    ReceiverId = entity.ReceiverId,
                    Schedules = entity.Schedules

                }).SingleOrDefaultAsync();
        }
    }
}
