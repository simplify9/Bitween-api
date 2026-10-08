using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using System.Threading.Tasks;

namespace SW.Bitween.Resources.Subscriptions
{
    [HandlerName("receivenow")]
    public class ReceiveNow(BitweenDbContext dbContext, RequestContext requestContext,
        SubscriptionSchedulerService subScheduler) : ICommandHandler<int, SubscriptionReceiveNow,object>
    {
        async public Task<object> Handle(int key, SubscriptionReceiveNow request)
        {
            await requestContext.EnsurePermission(dbContext, Model.Permissions.Subscriptions.Operate);

            var entity = await dbContext.FindAsync<Subscription>(key)
                         ?? throw new SWNotFoundException(key.ToString());
            entity.SetReceiveNow();
            await dbContext.SaveChangesAsync();

            await subScheduler.RunNow(entity);
            return null;
        }
    }
}