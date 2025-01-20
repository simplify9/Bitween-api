using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using System.Threading.Tasks;
using SW.Bitween.Domain.Accounts;

namespace SW.Bitween.Resources.Subscriptions
{
    [HandlerName("receivenow")]
    class ReceiveNow : ICommandHandler<int, SubscriptionReceiveNow,object>
    {
        private readonly BitweenDbContext _dbContext;
        private readonly RequestContext _requestContext;

        public ReceiveNow(BitweenDbContext dbContext, RequestContext requestContext)
        {
            _dbContext = dbContext;
            _requestContext = requestContext;
        }

        async public Task<object> Handle(int key, SubscriptionReceiveNow request)
        {
            _requestContext.EnsureAccess(AccountRole.Admin, AccountRole.Member);

            var entity = await _dbContext.FindAsync<Subscription>(key);
            entity.SetReceiveNow();
            await _dbContext.SaveChangesAsync();
            return null;
        }
    }
}