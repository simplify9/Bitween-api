using System;
using System.Linq;
using System.Threading.Tasks;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Accounts;

/// <summary>
/// Sets a member's password on their behalf. Bitween has no outbound mail, so there's no
/// self-service reset — without this, anyone who forgets their password is locked out for good.
/// Changing your own password goes through ChangePassword, which asks for the current one.
/// </summary>
[HandlerName("setPassword")]
public class SetPassword(BitweenDbContext dbContext, RequestContext requestContext)
    : ICommandHandler<int, SetAccountPasswordModel, object>
{
    public async Task<object> Handle(int key, SetAccountPasswordModel request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Users.Edit);

        if (key == Convert.ToInt32(requestContext.GetNameIdentifier()))
            throw new SWValidationException("USE_CHANGE_PASSWORD",
                "Use Change password to set your own, so the current one is still required.");

        var account = await dbContext.Set<Account>().FindAsync(key);
        if (account is null)
            throw new SWValidationException("ACCOUNT_NOT_FOUND", $"No account exists with the id {key}");

        account.SetPassword(request.Password);
        await dbContext.SaveChangesAsync();

        // Signing in with a refresh token skips password verification entirely — it looks the
        // account up by id and issues a fresh token from whatever state it is now in. So a session
        // opened with the old password outlives the change unless the tokens go with it, and
        // "change the password" would not actually remove whoever you changed it because of.
        await dbContext.Set<RefreshToken>()
            .Where(t => t.AccountId == account.Id)
            .ExecuteDeleteAsync();

        return null;
    }

    private class Validate : AbstractValidator<SetAccountPasswordModel>
    {
        public Validate()
        {
            RuleFor(i => i.Password).NotEmpty().MinimumLength(8);
        }
    }
}
