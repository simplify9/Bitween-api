using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using System.Threading.Tasks;
using SW.Bitween.Domain.Accounts;

namespace SW.Bitween.Resources.Subscriptions
{
    [HandlerName("aggregatenow")]
    class AggregateNow : ICommandHandler<int, SubscriptionAggregateNow,object>
    {
        private readonly BitweenDbContext _dbContext;
        private readonly RequestContext _requestContext;


        public AggregateNow(BitweenDbContext dbContext, RequestContext requestContext)
        {
            _dbContext = dbContext;
            _requestContext = requestContext;
        }

        public async Task<object> Handle(int key, SubscriptionAggregateNow request)
        {
            _requestContext.EnsureAccess(AccountRole.Admin, AccountRole.Member);

            var entity = await _dbContext.FindAsync<Subscription>(key);
            entity.SetAggregateNow();
            await _dbContext.SaveChangesAsync();
            return null;
        }
    }
}