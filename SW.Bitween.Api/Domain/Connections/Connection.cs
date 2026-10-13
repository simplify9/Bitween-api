using System;
using SW.PrimitiveTypes;

namespace SW.Bitween.Domain.Connections;

/// <summary>How a connection gets its access tokens.</summary>
public enum ConnectionGrant
{
    /// <summary>OAuth2 client credentials: the client id and secret alone. Nobody has to approve anything.</summary>
    ClientCredentials = 1,

    /// <summary>
    /// OAuth2 authorization code: someone approves access once at the provider, and the refresh token
    /// that brings keeps the connection going, as Salesforce, Zoho, Shopify and Dynamics apps need.
    /// </summary>
    AuthorizationCode = 2,

    /// <summary>OAuth2 password grant: a service user's name and password, for providers that still offer it.</summary>
    Password = 3,
}

/// <summary>Where a connection stands, for the Connections page and the error an exchange reports.</summary>
public enum ConnectionStatus
{
    /// <summary>Saved, never yet used or authorized.</summary>
    New = 0,

    Connected = 1,

    /// <summary>An authorization code connection with no refresh token, or one the provider stopped honouring.</summary>
    NeedsAuthorizing = 2,

    /// <summary>The last attempt to get a token failed; <see cref="Connection.LastError"/> says why.</summary>
    Error = 3,
}

/// <summary>
/// Credentials Bitween keeps fresh for adapters: an OAuth2 client whose access tokens Bitween asks
/// for, renews and rotates, so that any adapter - built in or published, in any language - can write
/// <c>{{connections.NAME.accessToken}}</c> in a setting and get a valid token when it runs.
/// </summary>
/// <remarks>
/// The token is resolved when the adapter starts, never when an exchange is made, so no exchange
/// stores one and a retry hours later still gets a token that works.
/// </remarks>
public class Connection : BaseEntity, IAudited
{
    /// <summary>What settings write after <c>connections.</c>: letters, digits, '_' and '-'.</summary>
    public string Name { get; set; }

    public ConnectionGrant Grant { get; set; }

    /// <summary>For <see cref="ConnectionGrant.AuthorizationCode"/>: the provider's page that asks for approval.</summary>
    public string AuthorizeUrl { get; set; }

    public string TokenUrl { get; set; }
    public string ClientId { get; set; }
    public string ClientSecret { get; set; }

    /// <summary>Separated by spaces.</summary>
    public string Scopes { get; set; }

    /// <summary>
    /// Extra query parameters for the approval page, as <c>access_type=offline&amp;prompt=consent</c>,
    /// which Google needs before it hands out a refresh token.
    /// </summary>
    public string AuthorizeParameters { get; set; }

    /// <summary><c>post</c> sends the client id and secret in the token request's form; <c>basic</c> in an Authorization header.</summary>
    public string TokenAuth { get; set; } = "post";

    /// <summary>For <see cref="ConnectionGrant.Password"/>.</summary>
    public string Username { get; set; }

    public string Password { get; set; }

    public string RefreshToken { get; set; }
    public string AccessToken { get; set; }
    public DateTime? AccessTokenExpiresOn { get; set; }

    /// <summary>
    /// Set while one node renews the token, so another waits for its result rather than renewing too:
    /// a provider that rotates refresh tokens honours only the first renewal, and the second would
    /// leave the connection needing authorizing again.
    /// </summary>
    public DateTime? RenewingUntil { get; set; }

    public ConnectionStatus Status { get; set; }
    public string LastError { get; set; }
    public string AuthorizedBy { get; set; }
    public DateTime? AuthorizedOn { get; set; }

    public DateTime CreatedOn { get; set; }
    public string CreatedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
    public string ModifiedBy { get; set; }

    /// <summary>Whether a token is in hand that is good for at least <paramref name="margin"/> more.</summary>
    public bool HasFreshToken(DateTime now, TimeSpan margin) =>
        !string.IsNullOrEmpty(AccessToken) && AccessTokenExpiresOn is { } expires && expires - margin > now;

    /// <summary>Forgets the tokens, as when the client or the provider changes: they belonged to the old one.</summary>
    public void ForgetTokens()
    {
        AccessToken = null;
        AccessTokenExpiresOn = null;
        RefreshToken = null;
        Status = Grant == ConnectionGrant.AuthorizationCode ? ConnectionStatus.NeedsAuthorizing : ConnectionStatus.New;
        LastError = null;
    }
}
