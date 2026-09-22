using System.Linq;
using System.Threading.Tasks;
using FluentValidation;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.GlobalAdapterValuesSets
{
public class Update(BitweenDbContext dbContext, RequestContext requestContext, IInfolinkCache cache)
        : ICommandHandler<string, GlobalAdapterValuesSetUpdate, object>
    {
        public async Task<object> Handle(string key, GlobalAdapterValuesSetUpdate request)
        {
            await requestContext.EnsurePermission(dbContext, Model.Permissions.GlobalValues.Edit);

            var entity = await dbContext.Set<GlobalAdapterValuesSet>().FindAsync(key);
            if (entity is null)
                throw new SWValidationException("NOT_FOUND", $"GlobalAdapterValuesSet with id {key} was not found");

            entity.Name = request.Name;
            // A secret the form did not touch comes back as the sentinel; restore it from storage
            // rather than saving the mask.
            entity.Values = AdapterSecretProperties.Merge(entity.Values, request.Values);
            entity.SecretProperties = request.SecretProperties?.ToList() ?? [];

            await dbContext.SaveChangesAsync();
            await cache.BroadcastRevoke();
            return null;
        }

        private class Validate : AbstractValidator<GlobalAdapterValuesSetUpdate>
        {
            public Validate()
            {
                RuleFor(i => i.Name).NotEmpty();
                RuleFor(i => i.Values).NotNull();
            }
        }
    }
}
