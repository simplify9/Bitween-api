using System;
using System.Security.Cryptography;
using System.Text;
using SW.PrimitiveTypes;

namespace SW.Bitween.Domain.Accounts;

/// <summary>
/// A credential a service account signs in with, unattended: a job applying configuration, a
/// pipeline publishing adapters. Traded for an ordinary short session at
/// <c>POST /api/accounts/servicetoken</c>, so everything a session can do is checked the same way.
/// </summary>
/// <remarks>
/// Only a hash is kept; the token itself is shown once, when it is made. Every token expires, and
/// can be revoked on its own or with its account, by disabling it.
/// </remarks>
public class ServiceToken : BaseEntity, IAudited
{
    public const string Prefix = "bwt_";
    public const int RandomLength = 40;
    public const int MaxLifetimeDays = 365;
    public const int DefaultLifetimeDays = 90;

    /// <summary>Unrevoked, unexpired tokens one account may hold: two, so one can replace the other.</summary>
    public const int MaxActivePerAccount = 2;

    /// <summary>How much of the token is kept to tell tokens apart on screen: the prefix and 8 more.</summary>
    public const int ShownLength = 12;

    const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    private ServiceToken()
    {
    }

    ServiceToken(int accountId, string name, string hash, string shown, DateTime expiresOn)
    {
        AccountId = accountId;
        Name = name;
        Hash = hash;
        Shown = shown;
        ExpiresOn = expiresOn;
    }

    /// <summary>A new token for the account, and the only time its value is known.</summary>
    public static (ServiceToken Token, string Value) Issue(int accountId, string name, DateTime expiresOn)
    {
        var value = Prefix + RandomNumberGenerator.GetString(Alphabet, RandomLength);
        return (new ServiceToken(accountId, name, HashOf(value), value[..ShownLength], expiresOn), value);
    }

    public static string HashOf(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    /// <summary>Whether a string is shaped like a token at all, before looking it up.</summary>
    public static bool LooksLikeOne(string value) =>
        value is { Length: 44 } && value.StartsWith(Prefix, StringComparison.Ordinal);

    public int AccountId { get; private set; }
    public string Name { get; private set; }

    /// <summary>SHA-256 of the token, hex. The tokens are random, so a plain hash is enough.</summary>
    public string Hash { get; private set; }

    /// <summary>The token's first characters, for telling it apart on screen and in logs.</summary>
    public string Shown { get; private set; }

    public DateTime ExpiresOn { get; private set; }
    public DateTime? RevokedOn { get; private set; }
    public string RevokedBy { get; private set; }

    /// <summary>When it was last traded for a session, and from which address. Not audited: it moves on every use.</summary>
    public DateTime? LastUsedOn { get; private set; }
    public string LastUsedFrom { get; private set; }

    public bool IsActive(DateTime nowUtc) => RevokedOn is null && ExpiresOn > nowUtc;

    public void Revoke(string by)
    {
        if (RevokedOn is not null) return;
        RevokedOn = DateTime.UtcNow;
        RevokedBy = by;
    }

    public DateTime CreatedOn { get; set; }
    public string CreatedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
    public string ModifiedBy { get; set; }
}
