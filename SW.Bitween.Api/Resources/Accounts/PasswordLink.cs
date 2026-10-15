using System;
using System.Linq;
using System.Threading.Tasks;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.Model;
using SW.Bitween.Services;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Accounts;

/// <summary>The links invite and reset emails carry, and the account each one is for.</summary>
internal static class PasswordLinks
{
    public const string Invalid = "This link has expired or has already been used. Ask for a new one.";

    /// <summary>The usable link the token belongs to and its member, or a refusal.</summary>
    public static async Task<(AccountLink Link, Account Account)> FindAsync(BitweenDbContext dbContext, string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 100)
            throw new SWValidationException("LINK_INVALID", Invalid);

        var hash = AccountLink.HashOf(token.Trim());
        var link = await dbContext.Set<AccountLink>().FirstOrDefaultAsync(l => l.Hash == hash);
        var account = link is null ? null : await Members.FindAsync(dbContext, link.AccountId);

        if (link is null || !link.IsUsable(DateTime.UtcNow) || account is null || account.Disabled)
            throw new SWValidationException("LINK_INVALID", Invalid);

        return (link, account);
    }
}

/// <summary>Who a link is for, so its page can greet them and say what it does. Changes nothing.</summary>
[HandlerName("passwordLink")]
[Unprotect]
public class PasswordLink(BitweenDbContext dbContext) : ICommandHandler<PasswordLinkModel, object>
{
    public async Task<object> Handle(PasswordLinkModel request)
    {
        var (link, account) = await PasswordLinks.FindAsync(dbContext, request.Token);
        return new PasswordLinkInfoModel
        {
            Purpose = link.Purpose.ToString(),
            Name = account.DisplayName,
            Email = account.Email,
            ExpiresOn = link.ExpiresOn
        };
    }
}

/// <summary>
/// Sets the password a link was sent for. The link stops working, and so does every other one the
/// member had, and every session they had open.
/// </summary>
[HandlerName("usePasswordLink")]
[Unprotect]
public class UsePasswordLink(BitweenDbContext dbContext, SignInThrottle throttle, AccountMail mail)
    : ICommandHandler<UsePasswordLinkModel, object>
{
    public async Task<object> Handle(UsePasswordLinkModel request)
    {
        var (link, account) = await PasswordLinks.FindAsync(dbContext, request.Token);

        await using (var transaction = await dbContext.Database.BeginTransactionAsync())
        {
            // Spent first, and only once: of two requests with the same link at the same moment, one
            // deletes it and the other finds nothing to delete, rather than both setting a password.
            if (await dbContext.Set<AccountLink>().Where(l => l.Id == link.Id).ExecuteDeleteAsync() == 0)
                throw new SWValidationException("LINK_INVALID", PasswordLinks.Invalid);

            account.SetPassword(request.Password);
            // Locked out after wrong guesses is usually why someone resets; the new password is the way back in.
            account.Unlock();
            await dbContext.SaveChangesAsync();

            await dbContext.Set<AccountLink>().Where(l => l.AccountId == account.Id).ExecuteDeleteAsync();
            // A session opened with the old password outlives the change unless its refresh tokens go.
            await dbContext.Set<RefreshToken>().Where(t => t.AccountId == account.Id).ExecuteDeleteAsync();
            await transaction.CommitAsync();
        }

        throttle.ClearAccount(account.Id);

        // An invite is the member choosing their first password: nothing to warn them about.
        if (link.Purpose == AccountLinkPurpose.PasswordReset)
            mail.SendInBackground(account.Email, AccountEmails.PasswordChanged(
                AccountEmails.ChangedBy.ResetLink, account.Email, mail.PublicBase, mail.ResetAvailable));

        return new { account.Email };
    }

    private class Validate : AbstractValidator<UsePasswordLinkModel>
    {
        public Validate()
        {
            RuleFor(i => i.Password).Password();
        }
    }
}
