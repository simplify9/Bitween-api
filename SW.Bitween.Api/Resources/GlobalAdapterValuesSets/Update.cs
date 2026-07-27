using System.Threading.Tasks;
using FluentValidation;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.GlobalAdapterValuesSets
{
    public class Update : ICommandHandler<string, GlobalAdapterValuesSetUpdate, object>
    {
        private readonly BitweenDbContext _dbContext;
        private readonly RequestContext _requestContext;

        public Update(BitweenDbContext dbContext, RequestContext requestContext)
        {
            _dbContext = dbContext;
            _requestContext = requestContext;
        }

        public async Task<object> Handle(string key, GlobalAdapterValuesSetUpdate request)
        {
            await _requestContext.EnsurePermission(_dbContext, Model.Permissions.GlobalValues.Edit);

            var entity = await _dbContext.Set<GlobalAdapterValuesSet>().FindAsync(key);
            if (entity is null)
                throw new SWValidationException("NOT_FOUND", $"GlobalAdapterValuesSet with id {key} was not found");

            entity.Name = request.Name;
            entity.Values = request.Values;

            await _dbContext.SaveChangesAsync();
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
