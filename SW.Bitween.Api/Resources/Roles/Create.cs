using System.Threading.Tasks;
using FluentValidation;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Roles;

public class Create : ICommandHandler<RoleCreate, object>
{
    private readonly BitweenDbContext _dbContext;
    private readonly RequestContext _requestContext;

    public Create(BitweenDbContext dbContext, RequestContext requestContext)
    {
        _dbContext = dbContext;
        _requestContext = requestContext;
    }

    public async Task<object> Handle(RoleCreate model)
    {
        await _requestContext.EnsurePermission(_dbContext, Model.Permissions.Roles.Create);

        RoleValidation.EnsureKnownPermissions(model.Permissions);
        await RoleValidation.EnsureNameIsFree(_dbContext, model.Name);

        var role = new Role(model.Name, model.Description, model.Permissions);
        _dbContext.Add(role);
        await _dbContext.SaveChangesAsync();
        return role.Id;
    }

    private class Validate : AbstractValidator<RoleCreate>
    {
        public Validate()
        {
            RuleFor(i => i.Name).NotEmpty().MaximumLength(100);
            RuleFor(i => i.Description).MaximumLength(500);
        }
    }
}
