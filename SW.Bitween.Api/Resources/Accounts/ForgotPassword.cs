using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.Model;
using SW.Bitween.Services;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Accounts;

/// <summary>
/// "Forgot password?": emails a reset link to the address, when it is a member's.
/// </summary>
/// <remarks>
/// The answer is the same whether the address has an account or not, and the email goes out after
/// the answer, so neither the reply nor its timing says who has an account here. A member gets at
/// most one email a minute, however often anyone sends the form.
/// </remarks>
[HandlerName("forgotPassword")]
[Unprotect]
public class ForgotPassword(BitweenDbContext dbContext, AccountMail mail) : ICommandHandler<ForgotPasswordModel, object>
{
    static readonly TimeSpan OneEmailPer = TimeSpan.FromMinutes(1);

    public async Task<object> Handle(ForgotPasswordModel request)
    {
        if (!mail.ResetAvailable)
            throw new SWValidationException("PASSWORD_RESET_OFF",
                "This Bitween can't email password resets. Ask your administrator to set a new password for you.");

        var email = request.Email?.Trim();
        if (string.IsNullOrEmpty(email)) return null;

        var account = await dbContext.Set<Account>()
            .FirstOrDefaultAsync(a => a.Email.ToLower() == email.ToLower() &&
                                      a.Kind == AccountKind.Person && !a.Disabled);
        if (account is null) return null;

        var now = DateTime.UtcNow;
        if (await dbContext.Set<AccountLink>().AnyAsync(l => l.AccountId == account.Id &&
                                                             l.Purpose == AccountLinkPurpose.PasswordReset &&
                                                             l.CreatedOn > now - OneEmailPer))
            return null;

        await dbContext.Set<AccountLink>()
            .Where(l => l.AccountId == account.Id && l.Purpose == AccountLinkPurpose.PasswordReset)
            .ExecuteDeleteAsync();

        var (link, token) = AccountLink.Issue(account.Id, AccountLinkPurpose.PasswordReset, now);
        dbContext.Add(link);
        await dbContext.SaveChangesAsync();

        mail.SendInBackground(account.Email, AccountEmails.Reset(account.Email, mail.PublicBase, token));
        return null;
    }
}
