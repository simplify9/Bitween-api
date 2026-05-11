using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SW.HttpExtensions;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Accounts
{
    [HandlerName("login")]
    [Unprotect]
    public class Login : ICommandHandler<UserLogin, object>
    {
        private readonly BitweenDbContext _dbContext;
        private readonly BitweenOptions _BitweenSettings;
        private readonly JwtTokenParameters _jwtTokenParameters;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public Login(JwtTokenParameters jwtTokenParameters, BitweenDbContext dbContext,
            BitweenOptions BitweenSettings, IHttpContextAccessor httpContextAccessor)
        {
            _jwtTokenParameters = jwtTokenParameters;
            _dbContext = dbContext;
            _BitweenSettings = BitweenSettings;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<object> Handle(UserLogin request)
        {
            var jwtExpiryTimeSpan = TimeSpan.FromMinutes(_BitweenSettings.JwtExpiryMinutes);

            var accountQ = _dbContext
                .Set<Account>()
                .AsQueryable();

            // Prefer refresh token from HttpOnly cookie (secure), fall back to body (legacy)
            var refreshTokenValue = _httpContextAccessor.HttpContext?.Request.Cookies["refresh_token"];
            if (string.IsNullOrEmpty(refreshTokenValue))
                refreshTokenValue = request.RefreshToken;

            if (!string.IsNullOrEmpty(refreshTokenValue))
            {
                var refreshToken = await _dbContext.Set<RefreshToken>()
                    .SingleOrDefaultAsync(x => x.Id == refreshTokenValue);
                if (refreshToken is null)
                {
                    throw new SWException("Invalid refreshToken.");
                }

                _dbContext.Remove(refreshToken);
                accountQ = accountQ.Where(u => u.Id == refreshToken.AccountId && !u.Disabled);
            }
            else if (!string.IsNullOrEmpty(request.MsToken))
            {
                var email = await request.GetEmailFromAzureJwtDefault();
                accountQ = accountQ.Where(u => u.Email.ToLower() == email && !u.Disabled);
            }
            else
            {
                accountQ = accountQ.Where(u => u.Email.ToLower() == request.Username.ToLower() && !u.Disabled);
            }


            var account = await accountQ
                .SingleOrDefaultAsync();

            if (account is null)
            {
                if (!string.IsNullOrEmpty(request.MsToken))
                    throw new SWException("Your Microsoft account is not registered in the system. Please contact your administrator to be added.");

                var identifier = request.Username ?? "unknown";
                throw new SWValidationException(identifier, identifier);
            }


            if (string.IsNullOrEmpty(refreshTokenValue) && !string.IsNullOrEmpty(request.Username) &&
                !string.IsNullOrEmpty(request.Password) && string.IsNullOrEmpty(request.MsToken))
            {
                if (request.Password == null ||
                    !SecurePasswordHasher.Verify(request.Password, account.Password))
                    throw new SWException("Invalid password.");
            }

            var newRefreshToken = CreateRefreshToken(account, LoginMethod.EmailAndPassword);
            await _dbContext.SaveChangesAsync();

            // Set refresh token as HttpOnly cookie — not accessible to JavaScript
            var isHttps = _httpContextAccessor.HttpContext?.Request.IsHttps ?? false;
            _httpContextAccessor.HttpContext?.Response.Cookies.Append("refresh_token", newRefreshToken, new CookieOptions
            {
                HttpOnly = true,
                Secure = isHttps,
                SameSite = SameSiteMode.Lax,
                Expires = DateTimeOffset.UtcNow.AddDays(30)
            });

            // Return only the JWT — refresh token stays in the cookie, not in the response body
            return new { Jwt = account.CreateJwt(LoginMethod.EmailAndPassword, _jwtTokenParameters, jwtExpiryTimeSpan) };
        }

        private string CreateRefreshToken(Account account, LoginMethod loginMethod)
        {
            var refreshToken = new RefreshToken(account.Id, loginMethod);
            _dbContext.Add(refreshToken);
            return refreshToken.Id;
        }
    }
}