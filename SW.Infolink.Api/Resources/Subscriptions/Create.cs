using SW.Infolink.Domain;
using SW.Infolink.Model;
using SW.PrimitiveTypes;
using System.Threading.Tasks;

namespace SW.Infolink.Api.Resources.Subscriptions
{
    class Create : ICommandHandler<SubscriptionConfig>
    {
        private readonly InfolinkDbContext dbContext;

        public Create(InfolinkDbContext dbContext)
        {
            this.dbContext = dbContext;
        }

        async public Task<object> Handle(SubscriptionConfig model)
        {
            var entity = new Subscription(model.Name, model.DocumentId, model.Type);
            dbContext.Add(entity);
            await dbContext.SaveChangesAsync();
            return entity.Id;
        }
    }
}
