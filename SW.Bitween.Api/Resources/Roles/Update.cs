using System.Linq;
using SW.Bitween.Resources.Accounts;
using System.Threading.Tasks;
using FluentValidation;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Roles;

public class Update(BitweenDbContext dbContext, RequestContext requestContext)
    : ICommandHandler<int, RoleUpdate, object>
{
    public async Task<object> Handle(int key, RoleUpdate model)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Roles.Edit);

        var role = await RoleValidation.Load(dbContext, key);

        // Built-in roles are the floor an instance can always fall back to — if Administrator
        // could be edited, an admin could lock everyone out of members and roles for good.
        if (role.IsSystem)
            throw new SWValidationException("ROLE_IS_BUILT_IN",
                $"'{role.Name}' is a built-in role and can't be changed. Create a role instead.");

        RoleValidation.EnsureKnownPermissions(model.Permissions);

        // Both what the role grants now and what it would grant: editing a role you couldn't have
        // created is as much a step past your own access as creating one.
        await GrantLimits.EnsureCallerHoldsPermissions(dbContext, requestContext,
            (role.Permissions ?? []).Concat(model.Permissions ?? []));
        await RoleValidation.EnsureNameIsFree(dbContext, model.Name, key);

        role.Update(model.Name, model.Description, model.Permissions);
        await dbContext.SaveChangesAsync();
        return null;
    }

    private class Validate : AbstractValidator<RoleUpdate>
    {
        public Validate()
        {
            RuleFor(i => i.Name).NotEmpty().MaximumLength(100);
            RuleFor(i => i.Description).MaximumLength(500);
        }
    }
}
