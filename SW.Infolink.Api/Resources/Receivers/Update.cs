using SW.EfCoreExtensions;
using SW.Infolink.Domain;
using SW.Infolink.Model;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SW.Infolink.Api.Resources.Receivers
{
    [HandlerName("update")]
    class Update : ICommandHandler<int, ReceiverConfig>
    {
        private readonly InfolinkDbContext dbContext;

        public Update(InfolinkDbContext dbContext)
        {
            this.dbContext = dbContext;
        }

        async public Task<object> Handle(int key, ReceiverConfig model)
        {
            var entity = await dbContext.FindAsync<Receiver>(key);

            if (entity == null)
            {
                entity = new Receiver(key, model.Name, model.ReceiverId)
                {
                    Properties = model.Properties.ToDictionary(),
                };
                dbContext.Add(entity);
            }
            else
            {
                dbContext.Entry(entity).SetProperties(model);
            }

            var updatedSchedules = model.Schedules.Select(dto => new Schedule((Recurrence)dto.Recurrence, dto.On, dto.Backwards)).ToList();
            entity.Schedules.Update(updatedSchedules);

            await dbContext.SaveChangesAsync(); 
            return null;
        }
    }
}
