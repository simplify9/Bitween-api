using System;
using System.Collections.Generic;
using System.Security.Claims;
using SW.HttpExtensions;
using SW.Infolink.Domain.Accounts;

namespace SW.Infolink
{
    public static class AccountExtensions
    {
        public static ClaimsIdentity CreateClaimsIdentity(this Account account, LoginMethod loginMethod)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, account.Id.ToString()),
                new Claim(ClaimTypes.GivenName, account.DisplayName),
                new Claim("login_methods", ((int) account.LoginMethods).ToString(), ClaimValueTypes.Integer),
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
            }

            if (account.Email != null) claims.Add(new Claim(ClaimTypes.Email, account.Email));
            if (account.Phone != null) claims.Add(new Claim(ClaimTypes.MobilePhone, account.Phone));


            return new ClaimsIdentity(claims, "Infolink");
        }

        public static string CreateJwt(this Account account, LoginMethod loginMethod,
            JwtTokenParameters jwtTokenParameters, TimeSpan jwtExpiry = default)
        {
            return jwtExpiry == default ? jwtTokenParameters.WriteJwt(CreateClaimsIdentity(account, loginMethod)) : jwtTokenParameters.WriteJwt(CreateClaimsIdentity(account, loginMethod), jwtExpiry);
        }
    }
}