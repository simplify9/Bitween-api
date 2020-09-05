using SW.EfCoreExtensions;
using SW.Infolink.Domain;
using SW.Infolink.Model;
using SW.PrimitiveTypes;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SW.Infolink.Api.Resources.Subscriptions
{
    class Update : ICommandHandler<int, SubscriptionConfig>
    {
        private readonly InfolinkDbContext dbContext;

        public Update(InfolinkDbContext dbContext)
        {
            this.dbContext = dbContext;
        }

        async public Task<object> Handle(int key, SubscriptionConfig model)
        {
            var entity = await dbContext.FindAsync<Subscription>(key);
            dbContext.Entry(entity).SetProperties(model);

            entity.Schedules.Update(model.Schedules.Select(dto => new Schedule(dto.Recurrence, TimeSpan.Parse($"{dto.Days}.{dto.Hours}:{dto.Minutes}:0"), dto.Backwards)).ToList());
            entity.ReceiveSchedules.Update(model.ReceiveSchedules.Select(dto => new Schedule(dto.Recurrence, TimeSpan.Parse($"{dto.Days}.{dto.Hours}:{dto.Minutes}:0"), dto.Backwards)).ToList());

            await dbContext.SaveChangesAsync(); 
            return null;
        }
    }
}
