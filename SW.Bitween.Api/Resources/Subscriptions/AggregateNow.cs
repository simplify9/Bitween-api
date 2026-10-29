using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using System.Threading.Tasks;

namespace SW.Bitween.Resources.Subscriptions
{
    [HandlerName("aggregatenow")]
    public class AggregateNow(BitweenDbContext dbContext, RequestContext requestContext,
        SubscriptionSchedulerService subScheduler) : ICommandHandler<int, SubscriptionAggregateNow,object>
    {
        public async Task<object> Handle(int key, SubscriptionAggregateNow request)
        {
            await requestContext.EnsurePermission(dbContext, Model.Permissions.Subscriptions.Operate);

            var entity = await dbContext.FindAsync<Subscription>(key)
                         ?? throw new SWNotFoundException(key.ToString());
            entity.SetAggregateNow();
            await dbContext.SaveChangesAsync();

            await subScheduler.RunNow(entity);
            return null;
        }
    }
}