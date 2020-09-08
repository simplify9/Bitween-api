using FluentValidation;
using SW.Infolink.Domain;
using SW.Infolink.Model;
using SW.PrimitiveTypes;
using System.Threading.Tasks;

namespace SW.Infolink.Api.Resources.Subscriptions
{
    class Create : ICommandHandler<SubscriptionCreate>
    {
        private readonly InfolinkDbContext dbContext;

        public Create(InfolinkDbContext dbContext)
        {
            this.dbContext = dbContext;
        }

        async public Task<object> Handle(SubscriptionCreate model)
        {
            var entity = new Subscription(model.Name, model.DocumentId, model.Type, model.PartnerId);
            dbContext.Add(entity);
            await dbContext.SaveChangesAsync();
            return entity.Id;
        }

        private class Validate : AbstractValidator<SubscriptionCreate>
        {
            public Validate()
            {
                RuleFor(i => i.Name).NotEmpty();
                RuleFor(i => i.DocumentId).NotEmpty();
                RuleFor(i => i.PartnerId).NotEqual(Partner.SystemId);

                When(i => i.Type == SubscriptionType.ApiCall, () =>
                {
                    RuleFor(i => i.PartnerId).NotEmpty();
                });
            }
        }
    }
}
