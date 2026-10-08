using FluentValidation;
using SW.EfCoreExtensions;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using System.Linq;
using System.Threading.Tasks;

namespace SW.Bitween.Resources.Subscriptions
{
    [HandlerName("savemapper")]
    public class SaveMapper(BitweenDbContext dbContext, IInfolinkCache BitweenCache,
        RequestContext requestContext) : ICommandHandler<int, SubscriptionSaveMapper, object>
    {
        public async Task<object> Handle(int key, SubscriptionSaveMapper model)
        {
            await requestContext.EnsurePermission(dbContext, Model.Permissions.Subscriptions.Edit);
            var entity = await dbContext.FindAsync<Subscription>(key)
                         ?? throw new SWNotFoundException(key.ToString());

            entity.MapperId = model.MapperId;
            entity.SetDictionaries(
                entity.HandlerProperties,
                model.MapperProperties.ToDictionary(),
                entity.ReceiverProperties,
                entity.DocumentFilter,
                entity.ValidatorProperties
            );

            await dbContext.SaveChangesAsync();
            await BitweenCache.BroadcastRevoke();
            return null;
        }

        private class Validate : AbstractValidator<SubscriptionSaveMapper>
        {
            public Validate(AdapterRequirements adapterRequirements)
            {
                RuleFor(i => i.MapperId).NotEmpty();

                When(i => i.MapperId != null, () =>
                {
                    RuleFor(i => i.MapperProperties).CustomAsync(async (i, context, _) =>
                    {
                        var mapperId = ((SubscriptionSaveMapper)context.InstanceToValidate).MapperId;

                        var missing = await adapterRequirements.MissingFor(mapperId, i);
                        if (missing.Any())
                            context.AddFailure($"Missing: {string.Join(",", missing)}");
                    });
                });
            }
        }
    }
}
