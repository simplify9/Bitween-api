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

namespace SW.Infolink.Api.Resources.Subscriptions
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
            return await dbContext.Set<Subscription>().AsNoTracking().
                Search("Id", key).
                Select(subscriber => new SubscriptionConfig
                {
                    Aggregate = subscriber.Aggregate,
                    DocumentFilter = subscriber.DocumentFilter.ToDictionary(),
                    DocumentId = subscriber.DocumentId,
                    HandlerId = subscriber.HandlerId,
                    Inactive = subscriber.Inactive,
                    //KeySetId = subscriber.KeySetId,
                    MapperId = subscriber.MapperId,
                    Name = subscriber.Name,
                    MapperProperties = subscriber.MapperProperties.ToDictionary(),
                    Temporary = subscriber.Temporary,
                    ResponseSubscriptionId = subscriber.ResponseSubscriptionId,
                    //Schedules = subscriber.Schedules

                }).SingleOrDefaultAsync();
        }
    }
}
