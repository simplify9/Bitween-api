using System.Linq;
using System.Threading.Tasks;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.GlobalAdapterValuesSets
{
public class Create(BitweenDbContext dbContext, RequestContext requestContext, IInfolinkCache cache)
        : ICommandHandler<GlobalAdapterValuesSetCreate, object>
    {
        public async Task<object> Handle(GlobalAdapterValuesSetCreate request)
        {
            await requestContext.EnsurePermission(dbContext, Model.Permissions.GlobalValues.Create);

            var exists = await dbContext.Set<GlobalAdapterValuesSet>().AnyAsync(x => x.Id == request.Id);
            if (exists)
                throw new SWValidationException("ID_EXISTS", $"GlobalAdapterValuesSet with id '{request.Id}' already exists");

            var entity = new GlobalAdapterValuesSet
            {
                Id = request.Id,
                Name = request.Name,
                Values = request.Values,
                SecretProperties = request.SecretProperties?.ToList() ?? []
            };

            dbContext.Add(entity);
            await dbContext.SaveChangesAsync();
            await cache.BroadcastRevoke();
            return new
            {
                entity.Id
            };
        }

        private class Validate : AbstractValidator<GlobalAdapterValuesSetCreate>
        {
            public Validate()
            {
                RuleFor(i => i.Id).NotEmpty();
                RuleFor(i => i.Name).NotEmpty();
                RuleFor(i => i.Values).NotNull();
            }
        }
    }
}
