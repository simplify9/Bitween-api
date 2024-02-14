using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using SW.HttpExtensions;
using SW.Infolink.Domain.Accounts;
using SW.Infolink.Model;

namespace SW.Infolink
{
    public static class AccountExtensions
    {
        public static async Task<string> GetEmailFromAzureJwtDefault(this UserLogin model)
        {
            try
            {
                var jwt = model.MsToken;
                if (string.IsNullOrEmpty(jwt))
                {
                    return null;
                }

                var configurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                    "https://login.microsoftonline.com/common/v2.0/.well-known/openid-configuration",
                    new OpenIdConnectConfigurationRetriever(),
                    new HttpDocumentRetriever()
                );
                var openIdConfig = await configurationManager.GetConfigurationAsync();
                var handler = new JwtSecurityTokenHandler();
                var tokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKeys = openIdConfig.SigningKeys
                };
                handler.ValidateToken(jwt, tokenValidationParameters, out _);
                var jwtSecurityToken = handler.ReadJwtToken(jwt);

                var email = jwtSecurityToken!.Claims.FirstOrDefault(i => i.Type.Contains("preferred_username"))?.Value;
                return string.IsNullOrWhiteSpace(email) ? null : email;
            }
            catch (Exception)
            {
                return null;
            }
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
                case LoginMethod.PhoneAndOtp:
                    claims.Add(new Claim(ClaimTypes.Name, account.Phone));
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
            if (account.Phone != null) claims.Add(new Claim(ClaimTypes.MobilePhone, account.Phone));


            return new ClaimsIdentity(claims, "Infolink");
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