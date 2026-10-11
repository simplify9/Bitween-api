using System.Linq;
using System.Threading.Tasks;
using FluentValidation;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.Model;
using SW.Bitween.Resources.Accounts;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.ServiceAccounts;

/// <summary>A service account, with the roles it signs in with. Its tokens are issued separately.</summary>
public class Create(BitweenDbContext dbContext, RequestContext requestContext)
    : ICommandHandler<CreateServiceAccountModel, object>
{
    public async Task<object> Handle(CreateServiceAccountModel request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.ServiceAccounts.Create);

        var name = request.Name.Trim();
        var roleIds = (request.RoleIds ?? []).Distinct().ToList();
        await ServiceAccountStore.EnsureNameFree(dbContext, name);
        // Nobody makes an account that can do more than they can.
        await GrantLimits.EnsureCallerHoldsRoles(dbContext, requestContext, roleIds);

        var account = Account.ForService(name, AccountRoles.LegacyRoleFor(roleIds));
        dbContext.Add(account);
        await dbContext.SaveChangesAsync();

        await AccountRoles.Set(dbContext, account.Id, roleIds);
        await dbContext.SaveChangesAsync();
        return account.Id;
    }

    private class Validate : AbstractValidator<CreateServiceAccountModel>
    {
        public Validate()
        {
            RuleFor(i => i.Name).NotEmpty().MaximumLength(200);
        }
    }
}
