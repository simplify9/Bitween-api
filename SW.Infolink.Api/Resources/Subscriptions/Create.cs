using SW.EfCoreExtensions;
using SW.Infolink.Domain;
using SW.Infolink.Model;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SW.Infolink.Api.Resources.Subscribers
{
    class Create : ICommandHandler<SubscriptionConfig>
    {
        private readonly InfolinkDbContext dbContext;

        public Create(InfolinkDbContext dbContext)
        {
            this.dbContext = dbContext;
        }

        async public Task<object> Handle(SubscriptionConfig model)
        {
            var entity = new Subscription(model.Name, model.DocumentId)
            {
                Aggregate = model.Aggregate,
                DocumentFilter = model.DocumentFilter.ToDictionary(),
                HandlerId = model.HandlerId,
                Inactive = model.Inactive,
                //KeySetId = model.KeySetId,
                MapperId = model.MapperId,
                HandlerProperties = model.HandlerProperties.ToDictionary(),
                MapperProperties = model.MapperProperties.ToDictionary(),
                Temporary = model.Temporary,
                ResponseSubscriptionId = model.ResponseSubscriptionId

            };
            var updatedSchedules = model.Schedules.Select(dto => new Schedule((Recurrence)dto.Recurrence, dto.On, dto.Backwards)).ToList();
            entity.Schedules.Update(updatedSchedules);

            await dbContext.Create(entity);
            return entity.Id;
        }
    }
}
