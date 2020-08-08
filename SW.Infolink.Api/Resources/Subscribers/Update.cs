using SW.EfCoreExtensions;
using SW.Infolink.Domain;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SW.Infolink.Api.Resources.Subscribers
{
    [HandlerName("update")]
    class Update : ICommandHandler<int, SubscriberConfig>
    {
        private readonly InfolinkDbContext dbContext;

        public Update(InfolinkDbContext dbContext)
        {
            this.dbContext = dbContext;
        }

        async public Task<object> Handle(int key, SubscriberConfig model)
        {
            var entity = await dbContext.FindAsync<Subscriber>(key);

            dbContext.Entry(entity).SetProperties(model);

            var updatedSchedules = model.Schedules.Select(dto => new Schedule((Recurrence)dto.Recurrence, dto.On, dto.Backwards)).ToList();
            entity.Schedules.Update(updatedSchedules);

            await dbContext.SaveChangesAsync(); 
            return null;
        }
    }
}
