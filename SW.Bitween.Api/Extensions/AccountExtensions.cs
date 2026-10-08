using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using SW.HttpExtensions;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.Model;

namespace SW.Bitween
{
    public static class AccountExtensions
    {
        /// <summary>Who a Microsoft ID token says it is: the address to match, and the identity to bind.</summary>
        public record MicrosoftIdentity(string Email, string ObjectAndTenant);

        // Cached and refreshed by the manager itself. It used to be built on every sign-in, which
        // fetched Microsoft's metadata and keys each time.
        private static readonly ConfigurationManager<OpenIdConnectConfiguration> MicrosoftMetadata = new(
            "https://login.microsoftonline.com/common/v2.0/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever());

        /// <summary>
        /// Where Microsoft's signing keys come from: its published metadata. Replaced only by tests,
        /// which sign their own tokens to take sign-in through the whole login handler.
        /// </summary>
        internal static Func<Task<IEnumerable<SecurityKey>>> MicrosoftSigningKeys { get; set; } =
            async () => (await MicrosoftMetadata.GetConfigurationAsync()).SigningKeys;

        /// <summary>
        /// Validates a Microsoft ID token for this Bitween's own app registration, or returns null.
        /// </summary>
        /// <remarks>
        /// The audience and issuer used to go unchecked, so a token Microsoft issued to any app, in
        /// any tenant, signed in here — one a user handed to some unrelated site would do — and the
        /// account was chosen by preferred_username, which Microsoft says not to authorize on.
        /// </remarks>
        public static async Task<MicrosoftIdentity> ValidateMicrosoftTokenAsync(string jwt, string clientId,
            string tenantId, ILogger logger)
        {
            if (string.IsNullOrEmpty(jwt) || string.IsNullOrWhiteSpace(clientId)) return null;
            try
            {
                return ValidateMicrosoftToken(jwt, await MicrosoftSigningKeys(), clientId, tenantId);
            }
            catch (Exception ex)
            {
                logger?.LogWarning("Microsoft sign-in refused: {Reason}", ex.Message);
                return null;
            }
        }

        /// <summary>The checks themselves, against the given keys, so they can be tested.</summary>
        public static MicrosoftIdentity ValidateMicrosoftToken(string jwt, IEnumerable<SecurityKey> signingKeys,
            string clientId, string tenantId)
        {
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            var parameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = signingKeys,
                ValidateLifetime = true,
                ValidateAudience = true,
                ValidAudience = clientId,
                ValidateIssuer = true,
                IssuerValidator = (issuer, token, _) =>
                {
                    var tid = ((JwtSecurityToken)token).Claims.FirstOrDefault(c => c.Type == "tid")?.Value;
                    var fromTenant = tid is not null &&
                                     (issuer == $"https://login.microsoftonline.com/{tid}/v2.0" ||
                                      issuer == $"https://sts.windows.net/{tid}/");
                    // A tenant given as an id pins sign-in to it; "common", "organizations" or a
                    // domain name can't be compared with the token's tid and leave it open.
                    var rightTenant = !Guid.TryParse(tenantId, out var pinned) ||
                                      string.Equals(tid, pinned.ToString(), StringComparison.OrdinalIgnoreCase);
                    if (!fromTenant || !rightTenant)
                        throw new SecurityTokenInvalidIssuerException("The token was not issued by the expected tenant.");
                    return issuer;
                }
            };

            handler.ValidateToken(jwt, parameters, out var validated);
            var claims = ((JwtSecurityToken)validated).Claims.ToList();

            var email = claims.FirstOrDefault(c => c.Type == "preferred_username")?.Value
                        ?? claims.FirstOrDefault(c => c.Type == "email")?.Value;
            var oid = claims.FirstOrDefault(c => c.Type == "oid")?.Value;
            var tid = claims.FirstOrDefault(c => c.Type == "tid")?.Value;
            if (string.IsNullOrWhiteSpace(email) || oid is null || tid is null) return null;

            return new MicrosoftIdentity(email.ToLowerInvariant(), $"{oid}@{tid}");
        }

        private static ClaimsIdentity CreateClaimsIdentity(this Account account, LoginMethod loginMethod)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, account.Id.ToString()),
                new(ClaimTypes.GivenName, account.DisplayName),
                new("login_methods", ((int)account.LoginMethods).ToString(), ClaimValueTypes.Integer),
                new("Role", account.Role.ToString())
            };


            switch (loginMethod)
            {
                case LoginMethod.EmailAndPassword:
                    claims.Add(new Claim(ClaimTypes.Name, account.Email));
                    break;
                case LoginMethod.ApiKey:
                    claims.Add(new Claim(ClaimTypes.Name, account.Id.ToString()));
                    break;
                case LoginMethod.None:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(loginMethod), loginMethod, null);
            }

            if (account.Email != null) claims.Add(new Claim(ClaimTypes.Email, account.Email));

            // Carried on the token rather than read per request like permissions are: it decides
            // what the token itself is worth, and a token has to keep meaning the same thing for
            // as long as it is valid.
            if (account.MustChangePassword)
                claims.Add(new Claim(RequestContextExtensions.MustChangePasswordClaim, "true"));


            return new ClaimsIdentity(claims, "Bitween");
        }

        public static string CreateJwt(this Account account, LoginMethod loginMethod,
            JwtTokenParameters jwtTokenParameters, TimeSpan jwtExpiry = default)
        {
            return jwtExpiry == default
                ? jwtTokenParameters.WriteJwt(CreateClaimsIdentity(account, loginMethod))
                : jwtTokenParameters.WriteJwt(CreateClaimsIdentity(account, loginMethod), jwtExpiry);
        }
    }
}