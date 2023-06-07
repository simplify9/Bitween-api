using FluentValidation;
using SW.Infolink.Domain;
using SW.Infolink.Model;
using SW.PrimitiveTypes;
using System.Threading.Tasks;
using SW.Infolink.Domain.Accounts;

namespace SW.Infolink.Resources.Subscriptions
{
    class Create : ICommandHandler<SubscriptionCreate>
    {
        private readonly InfolinkDbContext _dbContext;
        private readonly RequestContext _requestContext;

        public Create(InfolinkDbContext dbContext, RequestContext requestContext)
        {
            this._dbContext = dbContext;
            _requestContext = requestContext;
        }

        public async Task<object> Handle(SubscriptionCreate model)
        {
            _requestContext.EnsureAccess(AccountRole.Admin, AccountRole.Member);

            Subscription entity;

            switch (model.Type)
            {
                case SubscriptionType.Receiving:
                    entity = new Subscription(model.Name, model.DocumentId);
                    break;
                case SubscriptionType.Aggregation:
                    entity = new Subscription(model.Name, model.AggregationForId!.Value, model.PartnerId!.Value);
                    break;
                case SubscriptionType.ApiCall:
                case SubscriptionType.Internal:
                    entity = new Subscription(model.Name, model.DocumentId, model.Type, model.PartnerId!.Value);
                    break;
                case SubscriptionType.Unknown:
                default:
                    throw new InfolinkException();
            }

            var trail = new SubscriptionTrail(SubscriptionTrialCode.Created, entity, true);

            _dbContext.Add(trail);
            await _dbContext.SaveChangesAsync();
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