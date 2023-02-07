using System.Threading.Tasks;
using Newtonsoft.Json;
using SW.Infolink.Domain;
using SW.Infolink.Model;
using SW.PrimitiveTypes;

namespace SW.Infolink.Resources.Subscriptions
{
    [HandlerName("pause")]
    public class Pause : ICommandHandler<int, SubscriptionPause>
    {
        private readonly InfolinkDbContext dbContext;

        public Pause(InfolinkDbContext dbContext)
        {
            this.dbContext = dbContext;
        }

        public async Task<object> Handle(int key, SubscriptionPause request)
        {
            var entity = await dbContext.FindAsync<Subscription>(key);
            var trail = new SubscriptionTrail(SubscriptionTrialCode.Paused, entity);
            if (entity!.PausedOn == null) entity.Pause();
            else entity.UnPause();

            trail.SetAfter(entity);
            await dbContext.SaveChangesAsync();
            return null;
        }
    }
}