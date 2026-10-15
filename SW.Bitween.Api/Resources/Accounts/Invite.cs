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
/// Invites a member who has no password yet to choose one: emailed when there is a mail server,
/// otherwise handed back for whoever invited to pass on. Sending it again replaces the last one.
/// </summary>
/// <remarks>
/// On an instance that signs in only with Microsoft or single sign-on there is no password to
/// choose, so the invite is an email saying where to sign in, and needs a mail server.
/// </remarks>
[HandlerName("invite")]
public class Invite(BitweenDbContext dbContext, RequestContext requestContext, BitweenOptions options,
    AccountMail mail) : ICommandHandler<int, InviteAccountModel, object>
{
    public async Task<object> Handle(int key, InviteAccountModel request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Users.Create);

        var account = await Members.FindAsync(dbContext, key);
        if (account is null)
            throw new SWValidationException("ACCOUNT_NOT_FOUND", $"No account exists with the id {key}");

        // The link chooses their password, which is signing in as them: as with Set password, only
        // someone who holds everything they hold may have it.
        await GrantLimits.EnsureCallerOutranks(dbContext, requestContext, key);

        if (account.Disabled)
            throw new SWValidationException("ACCOUNT_DISABLED",
                $"{account.DisplayName} is disabled. Re-enable them before inviting them.");

        var callerId = Convert.ToInt32(requestContext.GetNameIdentifier());
        var inviter = await dbContext.Set<Account>().Where(a => a.Id == callerId)
            .Select(a => a.DisplayName).FirstOrDefaultAsync() ?? "An administrator";
        var linkBase = mail.SignedInBase;

        if (options.DisableEmailPasswordLogin)
        {
            if (!mail.CanSend)
                throw new SWValidationException("NO_MAIL_SERVER",
                    "Bitween can't email an invite until a mail server is set up in Settings, under Mail server.");

            var sent = await mail.SendAsync(account.Email, AccountEmails.InviteToSignIn(inviter, account.Email, linkBase));
            return new InviteResultModel { Emailed = sent is null, EmailError = sent };
        }

        if (!string.IsNullOrEmpty(account.Password))
            throw new SWValidationException("ALREADY_HAS_PASSWORD",
                $"{account.DisplayName} already has a password. Set a new one for them, " +
                "or have them use Forgot password on the sign-in page.");

        // Only the newest invite works, so a resend makes any earlier email useless.
        await dbContext.Set<AccountLink>()
            .Where(l => l.AccountId == key && l.Purpose == AccountLinkPurpose.Invite)
            .ExecuteDeleteAsync();

        var now = DateTime.UtcNow;
        var (link, token) = AccountLink.Issue(key, AccountLinkPurpose.Invite, now);

        string error = null;
        if (mail.CanSend)
        {
            error = await mail.SendAsync(account.Email, AccountEmails.Invite(inviter, account.Email, linkBase, token));
            if (error is null) link.Emailed(now);
            else link.NotEmailed(error);
        }

        dbContext.Add(link);
        await dbContext.SaveChangesAsync();

        var emailed = mail.CanSend && error is null;
        return new InviteResultModel
        {
            Emailed = emailed,
            EmailError = error,
            ExpiresOn = link.ExpiresOn,
            // Handed back only when nobody else will get it to them.
            Link = emailed ? null : $"{linkBase}/set-password?token={token}"
        };
    }
}
