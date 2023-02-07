using FluentValidation;
using SW.Infolink.Domain;
using SW.Infolink.Model;
using SW.PrimitiveTypes;
using System.Threading.Tasks;

namespace SW.Infolink.Resources.Subscriptions
{
    class Create : ICommandHandler<SubscriptionCreate>
    {
        private readonly InfolinkDbContext dbContext;

        public Create(InfolinkDbContext dbContext)
        {
            this.dbContext = dbContext;
        }

        public async Task<object> Handle(SubscriptionCreate model)
        {
            Subscription entity;

            switch (model.Type)
            {
                case SubscriptionType.Receiving:
                    entity = new Subscription(model.Name, model.DocumentId);
                    break;
                case SubscriptionType.Aggregation:
                    entity = new Subscription(model.Name, model.AggregationForId.Value, model.PartnerId.Value);
                    break;
                case SubscriptionType.ApiCall:
                case SubscriptionType.Internal:
                    entity = new Subscription(model.Name, model.DocumentId, model.Type, model.PartnerId.Value);
                    break;
                default:
                    throw new InfolinkException();
            }

           // var trail = new SubscriptionTrail(SubscriptionTrialCode.Created, entity);
          //  dbContext.Add(trail);
            dbContext.Add(entity);
            await dbContext.SaveChangesAsync();
            return entity.Id;
        }

        private class Validate : AbstractValidator<SubscriptionCreate>
        {
            public Validate()
            {
                RuleFor(i => i.Name).NotEmpty();
                RuleFor(i => i.DocumentId).NotEmpty().When(i => i.Type != SubscriptionType.Aggregation);
                RuleFor(i => i.PartnerId).NotEqual(Partner.SystemId);
                RuleFor(i => i.Type).NotEqual(SubscriptionType.Unknown);

                When(i => i.Type != SubscriptionType.Receiving, () => { RuleFor(i => i.PartnerId).NotEmpty(); });

                When(i => i.Type == SubscriptionType.Aggregation,
                    () => { RuleFor(i => i.AggregationForId).NotEmpty(); });
            }
        }
    }
}