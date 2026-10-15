using System;
using System.Security.Cryptography;
using SW.PrimitiveTypes;

namespace SW.Bitween.Domain.Accounts;

/// <summary>
/// A link emailed to a person to choose their password: an invite to a new member, or a reset
/// asked for from the sign-in page. It works once, and only until it expires.
/// </summary>
/// <remarks>
/// Only a hash is kept, as for service tokens: the link itself is in the email, and whoever reads
/// this table can't use it. Issuing a new one for the same purpose replaces the old one, so a
/// forwarded or resent email leaves at most one link working.
/// </remarks>
public class AccountLink : BaseEntity, IHasCreationTime
{
    public static readonly TimeSpan InviteLifetime = TimeSpan.FromHours(48);
    public static readonly TimeSpan ResetLifetime = TimeSpan.FromHours(1);

    private AccountLink()
    {
    }

    AccountLink(int accountId, AccountLinkPurpose purpose, string hash, DateTime expiresOn)
    {
        AccountId = accountId;
        Purpose = purpose;
        Hash = hash;
        ExpiresOn = expiresOn;
    }

    /// <summary>A new link for the account, and the only time its token is known.</summary>
    public static (AccountLink Link, string Token) Issue(int accountId, AccountLinkPurpose purpose, DateTime nowUtc)
    {
        // 32 random bytes, URL-safe: it goes in a link's query string.
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var lifetime = purpose == AccountLinkPurpose.Invite ? InviteLifetime : ResetLifetime;
        return (new AccountLink(accountId, purpose, HashOf(token), nowUtc + lifetime) { CreatedOn = nowUtc }, token);
    }

    public static string HashOf(string token) => ServiceToken.HashOf(token);

    public int AccountId { get; private set; }
    public AccountLinkPurpose Purpose { get; private set; }

    /// <summary>SHA-256 of the token, hex.</summary>
    public string Hash { get; private set; }

    public DateTime ExpiresOn { get; private set; }

    /// <summary>When the email carrying it was handed to the mail server; null when it wasn't sent.</summary>
    public DateTime? EmailedOn { get; private set; }

    /// <summary>Why the email couldn't be sent, for the member's page; null once one was.</summary>
    public string EmailError { get; private set; }

    public DateTime CreatedOn { get; set; }

    public bool IsUsable(DateTime nowUtc) => ExpiresOn > nowUtc;

    public void Emailed(DateTime nowUtc)
    {
        EmailedOn = nowUtc;
        EmailError = null;
    }

    public void NotEmailed(string error) =>
        EmailError = error is { Length: > 500 } ? error[..500] : error;
}

public enum AccountLinkPurpose : byte
{
    Invite = 1,
    PasswordReset = 2,
}
