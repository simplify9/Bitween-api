using SW.Infolink.Domain;
using SW.Infolink.Model;
using SW.PrimitiveTypes;
using System.Threading.Tasks;

namespace SW.Infolink.Resources.Subscriptions
{
    [HandlerName("receivenow")]
    class ReceiveNow : ICommandHandler<int, SubscriptionReceiveNow>
    {
        private readonly InfolinkDbContext dbContext;

        public ReceiveNow(InfolinkDbContext dbContext)
        {
            this.dbContext = dbContext;
        }

        async public Task<object> Handle(int key, SubscriptionReceiveNow request)
        {
            var entity = await dbContext.FindAsync<Subscription>(key);
            entity.SetReceiveNow();
            await dbContext.SaveChangesAsync();
            return null;
        }

    }
}
