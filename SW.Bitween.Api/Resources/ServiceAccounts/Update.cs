using System.Linq;
using System.Threading.Tasks;
using FluentValidation;
using SW.Bitween.Model;
using SW.Bitween.Resources.Accounts;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.ServiceAccounts;

/// <summary>Renames a service account and replaces its roles.</summary>
public class Update(BitweenDbContext dbContext, RequestContext requestContext)
    : ICommandHandler<int, UpdateServiceAccountModel, object>
{
    public async Task<object> Handle(int key, UpdateServiceAccountModel request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.ServiceAccounts.Edit);

        var account = await ServiceAccountStore.Load(dbContext, key);
        var name = request.Name.Trim();
        var roleIds = (request.RoleIds ?? []).Distinct().ToList();
        await ServiceAccountStore.EnsureNameFree(dbContext, name, key);
        await GrantLimits.EnsureCallerOutranks(dbContext, requestContext, key);
        await GrantLimits.EnsureCallerHoldsRoles(dbContext, requestContext, roleIds);

        account.UpdateProfile(name);
        account.SetRole(AccountRoles.LegacyRoleFor(roleIds));
        await AccountRoles.Set(dbContext, key, roleIds);
        await dbContext.SaveChangesAsync();
        return null;
    }

    private class Validate : AbstractValidator<UpdateServiceAccountModel>
    {
        public Validate()
        {
            RuleFor(i => i.Name).NotEmpty().MaximumLength(200);
        }
    }
}
