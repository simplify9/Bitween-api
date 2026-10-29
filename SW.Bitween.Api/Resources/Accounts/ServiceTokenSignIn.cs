using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.Model;
using SW.HttpExtensions;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Accounts;

/// <summary>
/// Where a service account signs in: its token, traded for an ordinary short session. Nothing else
/// accepts a token, so everything the session does is checked the way a member's is: permissions
/// from its roles on every call, disabling it ends it at once.
/// </summary>
/// <remarks>
/// No refresh token: the caller holds the token and trades it again when the session runs out.
/// Tokens that match nothing count against the address they came from, as wrong passwords do: that
/// is what guessing looks like. A real token that has expired or been revoked doesn't; that's a job
/// nobody has updated yet, and locking its address out would stop every other job beside it.
/// </remarks>
[HandlerName("servicetoken")]
[Unprotect]
public class ServiceTokenSignIn(BitweenDbContext dbContext, JwtTokenParameters jwtTokenParameters, BitweenOptions options,
    IHttpContextAccessor httpContextAccessor, SignInThrottle throttle, ILogger<ServiceTokenSignIn> logger)
    : ICommandHandler<ServiceTokenSignInModel, object>
{
    /// <summary>What the throttle counts unknown tokens against: no account has this id.</summary>
    internal const int NoAccount = 0;

    const string Refused = "This token isn't valid: it's unknown, expired or revoked, or its account is disabled.";

    public async Task<object> Handle(ServiceTokenSignInModel request)
    {
        var now = DateTime.UtcNow;
        var address = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        var lockedUntil = throttle.LockedUntil(NoAccount, address, now);
        if (lockedUntil is not null)
        {
            var minutes = (int)Math.Ceiling((lockedUntil.Value - now).TotalMinutes);
            throw new SWException($"Too many invalid tokens from this address. Try again in {minutes} minute{(minutes == 1 ? "" : "s")}.");
        }

        var value = request?.Token?.Trim();
        var hash = ServiceToken.LooksLikeOne(value) ? ServiceToken.HashOf(value) : null;
        var token = hash is null
            ? null
            : await dbContext.Set<ServiceToken>().AsNoTracking().SingleOrDefaultAsync(t => t.Hash == hash);
        var account = token is null
            ? null
            : await dbContext.Set<Account>().AsNoTracking().SingleOrDefaultAsync(a => a.Id == token.AccountId);

        if (token is null || !token.IsActive(now) || account is null || account.Deleted || account.Disabled || !account.IsService)
        {
            if (token is null) throttle.RegisterFailure(NoAccount, address, now);
            logger.LogWarning("Service token sign-in refused from {Address}: {Reason}.", address,
                token is null ? "no such token" : !token.IsActive(now) ? $"token {token.Shown} expired or revoked" : "account disabled");
            throw new SWException(Refused);
        }

        // Straight to the row: who used it last is not a change anyone needs in the audit trail.
        await dbContext.Set<ServiceToken>().Where(t => t.Id == token.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(t => t.LastUsedOn, now).SetProperty(t => t.LastUsedFrom, address));
        await dbContext.Set<Account>().Where(a => a.Id == account.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(a => a.LastSignInOn, now));

        var lifetime = TimeSpan.FromMinutes(options.JwtExpiryMinutes);
        return new
        {
            Jwt = account.CreateJwt(LoginMethod.ApiKey, jwtTokenParameters, lifetime),
            ExpiresIn = (int)lifetime.TotalSeconds,
            account.Id,
            Name = account.DisplayName,
        };
    }
}
