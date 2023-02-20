using SW.Infolink.Domain;
using SW.Infolink.Model;
using SW.PrimitiveTypes;
using System.Threading.Tasks;
using SW.Infolink.Domain.Accounts;

namespace SW.Infolink.Resources.Subscriptions
{
    [HandlerName("aggregatenow")]
    class AggregateNow : ICommandHandler<int, SubscriptionAggregateNow>
    {
        private readonly InfolinkDbContext _dbContext;
        private readonly RequestContext _requestContext;


        public AggregateNow(InfolinkDbContext dbContext, RequestContext requestContext)
        {
            _dbContext = dbContext;
            _requestContext = requestContext;
        }

        public async Task<object> Handle(int key, SubscriptionAggregateNow request)
        {
            _requestContext.EnsureAccess(AccountRole.Admin, AccountRole.Viewer);

            var entity = await _dbContext.FindAsync<Subscription>(key);
            entity.SetAggregateNow();
            await _dbContext.SaveChangesAsync();
            return null;
        }
    }
}