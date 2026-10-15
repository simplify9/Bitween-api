using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.Model;
using SW.Bitween.Services;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Accounts;

/// <summary>
/// "Forgot password?": emails a reset link to the address, when it is a member's.
/// </summary>
/// <remarks>
/// The answer is the same whether the address has an account or not, and so is the work done
/// before it: one lookup. Making the link and sending it happen after the answer, so neither the
/// reply nor its timing says who has an account here. A member gets at most one email a minute,
/// however often anyone sends the form.
/// </remarks>
[HandlerName("forgotPassword")]
[Unprotect]
public class ForgotPassword(BitweenDbContext dbContext, AccountMail mail, IServiceScopeFactory scopes,
    ILogger<ForgotPassword> logger) : ICommandHandler<ForgotPasswordModel, object>
{
    static readonly TimeSpan OneEmailPer = TimeSpan.FromMinutes(1);

    public async Task<object> Handle(ForgotPasswordModel request)
    {
        if (!mail.ResetAvailable)
            throw new SWValidationException("PASSWORD_RESET_OFF",
                "This Bitween can't email password resets. Ask your administrator to set a new password for you.");

        var email = request.Email?.Trim();
        if (string.IsNullOrEmpty(email)) return null;

        var account = await dbContext.Set<Account>().AsNoTracking()
            .Where(a => a.Email.ToLower() == email.ToLower() && a.Kind == AccountKind.Person && !a.Disabled)
            .Select(a => new { a.Id, a.Email })
            .FirstOrDefaultAsync();

        if (account is not null)
            _ = Task.Run(() => IssueAsync(account.Id, account.Email));

        return null;
    }

    /// <summary>Makes the link and emails it, on a scope of its own: the request's is gone by then.</summary>
    async Task IssueAsync(int accountId, string email)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var now = DateTime.UtcNow;

            if (await db.Set<AccountLink>().AnyAsync(l => l.AccountId == accountId &&
                                                          l.Purpose == AccountLinkPurpose.PasswordReset &&
                                                          l.CreatedOn > now - OneEmailPer))
                return;

            // Earlier reset links keep working until they expire, and using any of them ends them all.
            // Replacing them here instead let two requests at once each delete the other's link, so
            // one of the two emails carried a link that no longer worked. Only expired ones go.
            await db.Set<AccountLink>()
                .Where(l => l.AccountId == accountId && l.ExpiresOn <= now)
                .ExecuteDeleteAsync();

            var (link, token) = AccountLink.Issue(accountId, AccountLinkPurpose.PasswordReset, now);
            db.Add(link);
            await db.SaveChangesAsync();

            await mail.SendAsync(email, AccountEmails.Reset(email, mail.PublicBase, token));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "A password reset for account {AccountId} could not be made.", accountId);
        }
    }
}
