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

namespace SW.Infolink.Resources.Subscriptions
{
    class Get : IGetHandler<int>
    {
        private readonly InfolinkDbContext dbContext;

        public Get(InfolinkDbContext dbContext)
        {
            this.dbContext = dbContext;
        }

        public async Task<object> Handle(int key, bool lookup = false)
        {
            var subscriber =
                await dbContext.Set<Subscription>().AsNoTracking().Search("Id", key).SingleOrDefaultAsync();

            return
                new SubscriptionUpdate
                {
                    AggregationForId = subscriber.AggregationForId,
                    DocumentFilter = subscriber.DocumentFilter.ToKeyAndValueCollection(),
                    DocumentId = subscriber.DocumentId,
                    HandlerId = subscriber.HandlerId,
                    Inactive = subscriber.Inactive,
                    MapperId = subscriber.MapperId,
                    ReceiverId = subscriber.ReceiverId,
                    Name = subscriber.Name,
                    PartnerId = subscriber.PartnerId,
                    MapperProperties = subscriber.MapperProperties.ToKeyAndValueCollection(),
                    HandlerProperties = subscriber.HandlerProperties.ToKeyAndValueCollection(),
                    ReceiverProperties = subscriber.ReceiverProperties.ToKeyAndValueCollection(),
                    ValidatorProperties = subscriber.ValidatorProperties.ToKeyAndValueCollection(),
                    Type = subscriber.Type,
                    Temporary = subscriber.Temporary,
                    ResponseSubscriptionId = subscriber.ResponseSubscriptionId,
                    ReceiveOn = subscriber.ReceiveOn,
                    AggregateOn = subscriber.AggregateOn,
                    ConsecutiveFailures = subscriber.ConsecutiveFailures,
                    LastException = subscriber.LastException,
                    AggregationTarget = subscriber.AggregationTarget,
                    ValidatorId = subscriber.ValidatorId,
                    PausedOn = subscriber.PausedOn,
                    Schedules = subscriber.Schedules.Select(s => new ScheduleView
                    {
                        Backwards = s.Backwards,
                        Recurrence = s.Recurrence,
                        Days = s.On.Days,
                        Hours = s.On.Hours,
                        Minutes = s.On.Minutes
                    }).ToList()
                };
        }
    }
}