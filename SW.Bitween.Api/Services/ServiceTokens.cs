using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SW.Bitween.Domain.Accounts;
using SW.PrimitiveTypes;

namespace SW.Bitween.Services;

/// <summary>
/// Checks a service account's <c>bwt_</c> token: the one check behind trading it for a session and
/// behind every other place that accepts one.
/// </summary>
/// <remarks>
/// Tokens that match nothing count against the address they came from, as wrong passwords do: that
/// is what guessing looks like. A real token that has expired or been revoked doesn't; that's a job
/// nobody has updated yet, and locking its address out would stop every other job beside it.
/// </remarks>
public class ServiceTokens(BitweenDbContext dbContext, SignInThrottle throttle, ILogger<ServiceTokens> logger)
{
    /// <summary>What the throttle counts unknown tokens against: no account has this id.</summary>
    internal const int NoAccount = 0;

    const string Refused = "This token isn't valid: it's unknown, expired or revoked, or its account is disabled.";

    /// <summary>
    /// Last-used times are written at most this often, so a caller presenting the token on every
    /// request doesn't write a row on every request.
    /// </summary>
    static readonly TimeSpan UseRecorded = TimeSpan.FromMinutes(1);

    /// <summary>The service account a token belongs to, when it's active and the account may sign in.</summary>
    /// <exception cref="SWException">Refused, or this address is locked out for guessing.</exception>
    public async Task<Account> Verify(string value, string address, DateTime now)
    {
        var lockedUntil = throttle.LockedUntil(NoAccount, address, now);
        if (lockedUntil is not null)
        {
            var minutes = (int)Math.Ceiling((lockedUntil.Value - now).TotalMinutes);
            throw new SWException($"Too many invalid tokens from this address. Try again in {minutes} minute{(minutes == 1 ? "" : "s")}.");
        }

        value = value?.Trim();
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
            logger.LogWarning("Service token refused from {Address}: {Reason}.", address,
                token is null ? "no such token" : !token.IsActive(now) ? $"token {token.Shown} expired or revoked" : "account disabled");
            throw new SWException(Refused);
        }

        // Straight to the row: who used it last is not a change anyone needs in the audit trail.
        var recordedBefore = now - UseRecorded;
        await dbContext.Set<ServiceToken>()
            .Where(t => t.Id == token.Id && (t.LastUsedOn == null || t.LastUsedOn < recordedBefore || t.LastUsedFrom != address))
            .ExecuteUpdateAsync(u => u.SetProperty(t => t.LastUsedOn, now).SetProperty(t => t.LastUsedFrom, address));
        await dbContext.Set<Account>()
            .Where(a => a.Id == account.Id && (a.LastSignInOn == null || a.LastSignInOn < recordedBefore))
            .ExecuteUpdateAsync(u => u.SetProperty(a => a.LastSignInOn, now));

        return account;
    }
}
