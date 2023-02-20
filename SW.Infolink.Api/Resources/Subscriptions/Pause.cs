using System.Threading.Tasks;
using Newtonsoft.Json;
using SW.Infolink.Domain;
using SW.Infolink.Domain.Accounts;
using SW.Infolink.Model;
using SW.PrimitiveTypes;

namespace SW.Infolink.Resources.Subscriptions
{
    [HandlerName("pause")]
    public class Pause : ICommandHandler<int, SubscriptionPause>
    {
        private readonly InfolinkDbContext _dbContext;
        private readonly RequestContext _requestContext;


        public Pause(InfolinkDbContext dbContext, RequestContext requestContext)
        {
            _dbContext = dbContext;
            _requestContext = requestContext;
        }

        public async Task<object> Handle(int key, SubscriptionPause request)
        {
            _requestContext.EnsureAccess(AccountRole.Admin, AccountRole.Viewer);

            var entity = await _dbContext.FindAsync<Subscription>(key);
            var trail = new SubscriptionTrail(SubscriptionTrialCode.Paused, entity);
            if (entity!.PausedOn == null) entity.Pause();
            else entity.UnPause();

            trail.SetAfter(entity);
            await _dbContext.SaveChangesAsync();
            return null;
        }
    }
}