using System.Threading.Tasks;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.GlobalAdapterValuesSets
{
    public class Create : ICommandHandler<GlobalAdapterValuesSetCreate, object>
    {
        private readonly BitweenDbContext _dbContext;
        private readonly RequestContext _requestContext;

        public Create(BitweenDbContext dbContext, RequestContext requestContext)
        {
            _dbContext = dbContext;
            _requestContext = requestContext;
        }

        public async Task<object> Handle(GlobalAdapterValuesSetCreate request)
        {
            await _requestContext.EnsurePermission(_dbContext, Model.Permissions.GlobalValues.Create);

            var exists = await _dbContext.Set<GlobalAdapterValuesSet>().AnyAsync(x => x.Id == request.Id);
            if (exists)
                throw new SWValidationException("ID_EXISTS", $"GlobalAdapterValuesSet with id '{request.Id}' already exists");

            var entity = new GlobalAdapterValuesSet
            {
                Id = request.Id,
                Name = request.Name,
                Values = request.Values
            };

            _dbContext.Add(entity);
            await _dbContext.SaveChangesAsync();
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
