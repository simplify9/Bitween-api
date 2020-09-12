using SW.Infolink.Domain;
using SW.Infolink.Model;
using SW.PrimitiveTypes;
using System.Threading.Tasks;

namespace SW.Infolink.Resources.Subscriptions
{
    [HandlerName("aggregatenow")]
    class AggregateNow : ICommandHandler<int, SubscriptionAggregateNow>
    {
        private readonly InfolinkDbContext dbContext;

        public AggregateNow(InfolinkDbContext dbContext)
        {
            this.dbContext = dbContext;
        }

        async public Task<object> Handle(int key, SubscriptionAggregateNow request)
        {
            var entity = await dbContext.FindAsync<Subscription>(key);
            entity.SetAggregateNow();
            await dbContext.SaveChangesAsync();
            return null;
        }

    }
}
