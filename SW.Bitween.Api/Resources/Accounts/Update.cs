using System;
using System.Threading.Tasks;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Accounts;

public class Update : ICommandHandler<int, UpdateAccountModel, object>
{
    private readonly BitweenDbContext _dbContext;
    private readonly RequestContext _requestContext;

    public Update(BitweenDbContext dbContext, RequestContext requestContext)
    {
        _dbContext = dbContext;
        _requestContext = requestContext;
    }

    public async Task<object> Handle(int key, UpdateAccountModel request)
    {
        var loggedInUserId = Convert.ToInt32(_requestContext.GetNameIdentifier());

        // Anyone may edit their own name; editing someone else needs the grant.
        if (key != loggedInUserId)
            await _requestContext.EnsurePermission(_dbContext, Model.Permissions.Users.Edit);

        var account = await _dbContext.Set<Account>().FindAsync(key);
        if (account is null)
            throw new SWValidationException("ACCOUNT_NOT_FOUND", $"No account exists with the id {key}");

        account.UpdateProfile(request.Name);

        // Changing a role is never self-service, or anyone could promote themselves.
        if (request.Role is not null && (AccountRole)request.Role != account.Role)
        {
            await _requestContext.EnsurePermission(_dbContext, Model.Permissions.Users.Edit);
            var role = (AccountRole)request.Role;
            account.SetRole(role);
            await AccountRoles.Set(_dbContext, key, [BuiltInRoleFor(role)]);
        }

        await _dbContext.SaveChangesAsync();

        return null;
    }

    /// <summary>Maps the legacy coarse role onto the built-in role that reproduces it.</summary>
    private static int BuiltInRoleFor(AccountRole role) => role switch
    {
        AccountRole.Admin => Role.AdministratorId,
        AccountRole.Member => Role.MemberId,
        _ => Role.ViewerId
    };
}
