using System.Data.Common;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Subscriptions
{
    [HandlerName("pause")]
public class Pause(BitweenDbContext dbContext, RequestContext requestContext, IInfolinkCache cache)
        : ICommandHandler<int, SubscriptionPause,object>
    {
        public async Task<object> Handle(int key, SubscriptionPause request)
        {
            await requestContext.EnsurePermission(dbContext, Model.Permissions.Subscriptions.Operate);

            var entity = await dbContext.FindAsync<Subscription>(key)
                         ?? throw new SWNotFoundException(key.ToString());
            if (entity!.PausedOn == null)
                entity.Pause();
            else
                entity.UnPause();

            await dbContext.SaveChangesAsync();
            // The receiving path reads PausedOn off the cached copy, so without this a paused
            // integration keeps taking messages for the rest of the cache's ten minutes. Resuming
            // has the mirror problem: its handler re-reads the cache, finds the copy still paused
            // and returns early, leaving everything it held on hold.
            await cache.BroadcastRevoke();
            return new
            {
                entity.Id
            };
        }
    }
}