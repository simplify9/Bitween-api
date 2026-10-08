using SW.Bitween.Resources.Accounts;
using System.Threading.Tasks;
using FluentValidation;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Roles;

public class Create(BitweenDbContext dbContext, RequestContext requestContext) : ICommandHandler<RoleCreate, object>
{
    public async Task<object> Handle(RoleCreate model)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Roles.Create);

        RoleValidation.EnsureKnownPermissions(model.Permissions);
        await GrantLimits.EnsureCallerHoldsPermissions(dbContext, requestContext, model.Permissions);
        await RoleValidation.EnsureNameIsFree(dbContext, model.Name);

        var role = new Role(model.Name, model.Description, model.Permissions);
        dbContext.Add(role);
        await dbContext.SaveChangesAsync();
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
