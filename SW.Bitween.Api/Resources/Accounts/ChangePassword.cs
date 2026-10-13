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
public class ChangePassword(BitweenDbContext dbContext, RequestContext requestContext, SignInThrottle throttle,
    Microsoft.AspNetCore.Http.IHttpContextAccessor httpContextAccessor)
    : ICommandHandler<ChangePasswordModel, object>
{
    public async Task<object> Handle(ChangePasswordModel request)
    {
        // Self-service: this only ever changes the caller's own password, and the old one has to
        // be supplied. The guard it replaces listed every role, so it granted nothing.
        var accountId = Convert.ToInt32(requestContext.GetNameIdentifier());
        var account = await Members.FindAsync(dbContext, accountId);

        // Checking the old password is guessing it, so it is limited as sign-in is: the same count of
        // failures from one address locks it out for the same time, and a stolen session can't be
        // used to try passwords until one fits.
        var nowUtc = DateTime.UtcNow;
        var address = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (throttle.LockedUntil(accountId, address, nowUtc) is { } lockedUntil)
        {
            var minutes = (int)Math.Ceiling((lockedUntil - nowUtc).TotalMinutes);
            throw new SWValidationException("TOO_MANY_ATTEMPTS",
                $"Too many wrong passwords. Try again in {minutes} minute{(minutes == 1 ? "" : "s")}.");
        }

        // A service account has no password to change, and isn't found here.
        if (account is null || string.IsNullOrEmpty(account.Password) ||
            !SecurePasswordHasher.Verify(request.OldPassword, account.Password))
        {
            throttle.RegisterFailure(accountId, address, nowUtc);
            throw new SWValidationException("AUTHENTICATION_ERROR",
                "The Password entered does not match the user's password");
        }
        throttle.Clear(accountId, address);

        account.SetPassword(request.NewPassword);
        await dbContext.SaveChangesAsync();

        // Signing in with a refresh token skips password verification entirely - it looks the
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