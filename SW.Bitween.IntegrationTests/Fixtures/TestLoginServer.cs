using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace SW.Bitween.IntegrationTests.Fixtures;

/// <summary>
/// A login server without the network: it signs tokens, and hands a JWT gateway the public key
/// to check them with, as the real one's discovery document would.
/// </summary>
public sealed class TestLoginServer : IGatewayIssuers
{
    public const string Issuer = "https://login.test.local";

    /// <summary>A login server that can't be reached: every read of its keys fails.</summary>
    public const string UnreachableIssuer = "https://down.test.local";

    private readonly RsaSecurityKey _key = new(RSA.Create(2048)) { KeyId = "published" };

    // Never published, but claiming the published key's id: a forgery that gets as far as the
    // signature check, rather than one refused because its key id is unknown.
    private readonly RsaSecurityKey _forger = new(RSA.Create(2048)) { KeyId = "published" };

    public BaseConfigurationManager For(string issuer)
    {
        if (issuer == UnreachableIssuer)
            return new Unreachable();

        var configuration = new OpenIdConnectConfiguration { Issuer = issuer };
        if (issuer == Issuer)
            configuration.SigningKeys.Add(_key);
        return new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
    }

    public string Token(string subject, string audience = "bitween", string issuer = Issuer,
        DateTime? expires = null, bool forged = false)
    {
        var until = expires ?? DateTime.UtcNow.AddMinutes(10);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Claims = new Dictionary<string, object> { ["sub"] = subject },
            IssuedAt = until.AddMinutes(-15),
            NotBefore = until.AddMinutes(-15),
            Expires = until,
            SigningCredentials = new SigningCredentials(forged ? _forger : _key, SecurityAlgorithms.RsaSha256),
        });
    }

    private sealed class Unreachable : BaseConfigurationManager
    {
        public override Task<BaseConfiguration> GetBaseConfigurationAsync(CancellationToken cancel) =>
            throw new InvalidOperationException("IDX20803: Unable to obtain configuration from the unreachable test login server.");

        public override void RequestRefresh()
        {
        }
    }
}
