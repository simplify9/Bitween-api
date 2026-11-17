using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SW.Bitween.Domain.Accounts;

namespace SW.Bitween.Services;

/// <summary>
/// The sessions behind refresh tokens: starting one, and spending its token to renew it. One place,
/// so every way of renewing - the browser's cookie, the CLI, an app signed in with OAuth - follows
/// the same rules.
/// </summary>
public class Sessions(BitweenDbContext dbContext, ILogger<Sessions> logger)
{
    /// <summary>
    /// Each renewal issues a new token, so this is an idle limit: a session in use keeps renewing,
    /// one left alone this long ends.
    /// </summary>
    public static readonly TimeSpan IdleLimit = TimeSpan.FromDays(30);

    static readonly TimeSpan Grace = TimeSpan.FromSeconds(30);

    /// <summary>How long past its expiry a used sign-in code is still remembered: longer than any request takes.</summary>
    static readonly TimeSpan UsedCodesKept = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The name a session the CLI signed in carries, and keeps through every renewal, so what it
    /// changes shows in the audit trail as made through the CLI.
    /// </summary>
    public const string Cli = "CLI";

    /// <summary>A new session for the account: its refresh token, added for the caller's save.</summary>
    /// <param name="clientId">The app an OAuth sign-in was for; null for the browser and the CLI.</param>
    /// <param name="clientName">The app's name, or <see cref="Cli"/>; null for the browser.</param>
    public RefreshToken Start(int accountId, LoginMethod loginMethod, string clientId = null, string clientName = null)
    {
        var token = new RefreshToken(accountId, loginMethod, clientId, clientName);
        dbContext.Add(token);
        return token;
    }

    /// <summary>
    /// Records a sign-in code's first redemption: false when it was redeemed before, so each code
    /// works once. Codes well past their expiry are cleared on the way: not right at it, or a
    /// redemption that checked the expiry just before could find its code's record gone.
    /// </summary>
    public async Task<bool> FirstUse(string nonce, DateTime expiresOn, DateTime now)
    {
        await dbContext.Set<SignInCodeUse>().Where(u => u.ExpiresOn < now - UsedCodesKept).ExecuteDeleteAsync();
        if (await dbContext.Set<SignInCodeUse>().AnyAsync(u => u.Nonce == nonce)) return false;
        dbContext.Add(new SignInCodeUse(nonce, expiresOn));
        try
        {
            await dbContext.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException)
        {
            // Two redemptions at once: the other one won.
            dbContext.ChangeTracker.Clear();
            return false;
        }
    }

    /// <summary>
    /// Spends a refresh token to renew its session: the token, or null when it's unknown, was
    /// replaced too long ago, or has gone unused past <see cref="IdleLimit"/>.
    /// </summary>
    /// <remarks>
    /// Marked superseded rather than deleted, with one conditional UPDATE so two requests with the
    /// same token can't both be the first. A token superseded moments ago still works, for the
    /// caller whose copy of the new one went missing; one superseded longer ago is refused. Deleting
    /// it outright used to make the loser of a race throw a concurrency error - a 500 from the
    /// sign-in endpoint - and a lost response sign out.
    /// </remarks>
    /// <param name="clientId">The app renewing, as it signed in; null for the browser and the CLI. A token
    /// issued to another app, or to none, isn't spent.</param>
    public async Task<RefreshToken> Spend(string value, DateTime now, string clientId = null)
    {
        var token = await dbContext.Set<RefreshToken>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == value);
        if (token is null || token.ClientId != clientId) return null;

        var spent = await dbContext.Set<RefreshToken>()
            .Where(t => t.Id == value && t.SupersededOn == null)
            .ExecuteUpdateAsync(u => u.SetProperty(t => t.SupersededOn, now)) == 1;

        if (!spent)
            spent = await dbContext.Set<RefreshToken>().AsNoTracking()
                .AnyAsync(t => t.Id == value && t.SupersededOn > now - Grace);

        // Superseded ones past their grace are of no further use to anyone.
        var graceEnded = now - Grace;
        await dbContext.Set<RefreshToken>()
            .Where(t => t.AccountId == token.AccountId && t.SupersededOn < graceEnded)
            .ExecuteDeleteAsync();

        if (spent && token.CreatedOn < now - IdleLimit)
        {
            logger.LogInformation("Refresh token older than {Days} days refused.", IdleLimit.TotalDays);
            return null;
        }

        return spent ? token : null;
    }
}
