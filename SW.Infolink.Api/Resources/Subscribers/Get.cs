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

namespace SW.Infolink.Api.Resources.Subscribers
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
            return await dbContext.Set<Subscriber>().AsNoTracking().
                Search("Id", key).
                Select(subscriber => new SubscriberConfig
                {
                    Aggregate = subscriber.Aggregate,
                    DocumentFilter = subscriber.DocumentFilter.ToDictionary(),
                    DocumentId = subscriber.DocumentId,
                    HandlerId = subscriber.HandlerId,
                    Inactive = subscriber.Inactive,
                    //KeySetId = subscriber.KeySetId,
                    MapperId = subscriber.MapperId,
                    Name = subscriber.Name,
                    Properties = subscriber.Properties.ToDictionary(),
                    Temporary = subscriber.Temporary,
                    ResponseSubscriberId = subscriber.ResponseSubscriberId,
                    Schedules = subscriber.Schedules

                }).SingleOrDefaultAsync();
        }
    }
}
