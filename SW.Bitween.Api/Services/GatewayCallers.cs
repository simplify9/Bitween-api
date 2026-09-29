using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Gateway;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween;

/// <summary>
/// Works out which partner is calling an API gateway, the way that gateway asks callers to prove
/// it. Whatever the method, it ends at one partner: partner values, global values and the
/// exchange's references all depend on having one.
/// </summary>
public class GatewayCallers(BitweenDbContext dbContext, IGatewayIssuers issuers, ILogger<GatewayCallers> logger)
{
    /// <summary>
    /// Signatures made with a private key only the login server holds. The shared-secret kind
    /// (HS256 and friends) is left out: with it, anyone holding the key a token is checked with
    /// could mint tokens of their own.
    /// </summary>
    static readonly string[] SigningAlgorithms =
    [
        SecurityAlgorithms.RsaSha256, SecurityAlgorithms.RsaSha384, SecurityAlgorithms.RsaSha512,
        SecurityAlgorithms.RsaSsaPssSha256, SecurityAlgorithms.RsaSsaPssSha384, SecurityAlgorithms.RsaSsaPssSha512,
        SecurityAlgorithms.EcdsaSha256, SecurityAlgorithms.EcdsaSha384, SecurityAlgorithms.EcdsaSha512,
    ];

    /// <returns>The partner, and the reference the exchange records for who called.</returns>
    public async Task<(bool Authorized, Partner Partner, string Reference)> Identify(
        ApiGateway gateway, RequestContext requestContext)
    {
        if (gateway.AuthMethod != GatewayAuthMethod.Jwt)
        {
            var (authorized, keyHolder, keyName) = await dbContext.CheckPartnerAuthorized(requestContext);
            return (authorized, keyHolder, $"partnerkey: {keyName}");
        }

        var identity = await IdentityFromToken(gateway, requestContext);
        if (identity == null)
            return (false, null, null);

        var partner = await dbContext.Set<Partner>().AsNoTracking()
            .SingleOrDefaultAsync(p => p.LoginIdentity == identity);
        if (partner == null)
        {
            // A genuine token, so most likely a partner whose identity hasn't been set yet.
            logger.LogInformation("Token on gateway {Gateway} names {Identity}, which no partner has",
                gateway.UrlName, identity);
            return (false, null, null);
        }

        return (true, partner, $"jwt: {identity}");
    }

    async Task<string> IdentityFromToken(ApiGateway gateway, RequestContext requestContext)
    {
        var (scheme, token) = BitweenDbContextExtensions.ReadAuthorization(requestContext);
        if (!"Bearer".Equals(scheme, StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(token))
            return null;

        BaseConfigurationManager keys;
        try
        {
            // Inside the try too: a gateway row edited by hand can be JWT with no login server.
            keys = issuers.For(gateway.JwtIssuer);
            // Read from the cache after the first time. Asked for here only so that a login server
            // we can't reach is logged as that: the token check swallows the failure and reports
            // it as a token signed with no keys, which sends whoever reads it after the partner.
            await keys.GetBaseConfigurationAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Couldn't read the keys of login server {Issuer} for gateway {Gateway}",
                gateway.JwtIssuer, gateway.UrlName);
            return null;
        }

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = gateway.JwtIssuer,
            ValidAudience = gateway.JwtAudience,
            // Keys come from the login server, and are fetched again when a token is signed with
            // one not seen yet — which is what a login server rotating its keys looks like here.
            ConfigurationManager = keys,
            ValidAlgorithms = SigningAlgorithms,
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.FromMinutes(2),
        });

        if (!result.IsValid)
        {
            // The partner only ever sees a 401, so this is where the reason can be found.
            logger.LogInformation("Token refused on gateway {Gateway}: {Reason}",
                gateway.UrlName, result.Exception?.Message);
            return null;
        }

        var claim = string.IsNullOrWhiteSpace(gateway.JwtPartnerClaim) ? "sub" : gateway.JwtPartnerClaim;
        return result.ClaimsIdentity.FindFirst(claim)?.Value;
    }
}
