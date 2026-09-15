using System;
using System.Linq;
using System.Threading.Tasks;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Accounts;

[HandlerName("changePassword")]
public class ChangePassword(BitweenDbContext dbContext, RequestContext requestContext)
    : ICommandHandler<ChangePasswordModel, object>
{
    public async Task<object> Handle(ChangePasswordModel request)
    {
        // Self-service: this only ever changes the caller's own password, and the old one has to
        // be supplied. The guard it replaces listed every role, so it granted nothing.
        var accountId = Convert.ToInt32(requestContext.GetNameIdentifier());
        var account = await dbContext.Set<Account>().FindAsync(accountId);

        if (!SecurePasswordHasher.Verify(request.OldPassword, account!.Password))
        {
            throw new SWValidationException("AUTHENTICATION_ERROR",
                "The Password entered does not match the user's password");
        }

        account.SetPassword(request.NewPassword);
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

    private class Validate : AbstractValidator<ChangePasswordModel>
    {
        public Validate()
        {
            RuleFor(i => i.NewPassword).Password();
        }
    }
}