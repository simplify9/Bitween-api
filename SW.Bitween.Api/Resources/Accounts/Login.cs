using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
        private const int MaxFailedLoginAttempts = 5;
        private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

        private readonly BitweenDbContext _dbContext;
        private readonly BitweenOptions _BitweenSettings;
        private readonly JwtTokenParameters _jwtTokenParameters;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<Login> _logger;

        public Login(JwtTokenParameters jwtTokenParameters, BitweenDbContext dbContext,
            BitweenOptions BitweenSettings, IHttpContextAccessor httpContextAccessor, ILogger<Login> logger)
        {
            _jwtTokenParameters = jwtTokenParameters;
            _dbContext = dbContext;
            _BitweenSettings = BitweenSettings;
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
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
                    _logger.LogWarning("Refresh token not found in DB, clearing cookie and falling back to credentials.");
                    _httpContextAccessor.HttpContext?.Response.Cookies.Delete("refresh_token");
                    refreshTokenValue = null;
                }
                else
                {
                    _dbContext.Remove(refreshToken);
                    accountQ = accountQ.Where(u => u.Id == refreshToken.AccountId);
                }
            }

            if (string.IsNullOrEmpty(refreshTokenValue) && string.IsNullOrEmpty(request.MsToken) &&
                _BitweenSettings.DisableEmailPasswordLogin)
            {
                _logger.LogWarning("Email/password login attempt rejected: DisableEmailPasswordLogin is enabled.");
                throw new SWException("Email and password login is disabled. Please sign in with Microsoft.");
            }

            // A credential login must carry both a username and a password. Without this guard a
            // request with a valid username but empty/missing password would skip verification
            // below and still be issued a token.
            if (string.IsNullOrEmpty(refreshTokenValue) && string.IsNullOrEmpty(request.MsToken) &&
                (string.IsNullOrEmpty(request.Username) || string.IsNullOrEmpty(request.Password)))
            {
                _logger.LogWarning("Login rejected: missing username or password on a credential login.");
                throw new SWException("Invalid username or password.");
            }

            if (!string.IsNullOrEmpty(refreshTokenValue))
            {
                // account query already filtered above
            }
            else if (!string.IsNullOrEmpty(request.MsToken))
            {
                var email = (await request.GetEmailFromAzureJwtDefault(_logger))?.ToLower();
                if (string.IsNullOrEmpty(email))
                {
                    _logger.LogWarning("MS login failed: could not extract email from token.");
                    throw new SWException("Could not retrieve your email from Microsoft. Please ensure your Microsoft account has a valid email address and try again.");
                }
                _logger.LogInformation("MS login attempt. Extracted email from token: '{Email}'", email);
                accountQ = accountQ.Where(u => u.Email.ToLower() == email);
            }
            else
            {
                accountQ = accountQ.Where(u => u.Email.ToLower() == request.Username.ToLower());
            }


            var account = await accountQ
                .SingleOrDefaultAsync();

            if (account is null)
            {
                if (!string.IsNullOrEmpty(request.MsToken))
                {
                    _logger.LogWarning("MS login failed: no account found matching the token email.");
                    throw new SWException("Your Microsoft account is not registered in the system. Please contact your administrator to be added.");
                }

                throw new SWException("Invalid username or password.");
            }

            if (account.Disabled)
            {
                if (!string.IsNullOrEmpty(request.MsToken))
                {
                    _logger.LogWarning("MS login failed: account '{Email}' is disabled.", account.Email);
                    throw new SWException("Your Microsoft account has been disabled. Please contact your administrator.");
                }

                throw new SWException("Your account has been disabled. Please contact your administrator.");
            }


            if (string.IsNullOrEmpty(refreshTokenValue) && !string.IsNullOrEmpty(request.Username) &&
                !string.IsNullOrEmpty(request.Password) && string.IsNullOrEmpty(request.MsToken))
            {
                var nowUtc = DateTime.UtcNow;
                if (account.IsLockedOut(nowUtc))
                {
                    var minutes = (int)Math.Ceiling((account.LockoutEnd!.Value - nowUtc).TotalMinutes);
                    _logger.LogWarning("Login rejected: account '{Email}' is temporarily locked.", account.Email);
                    throw new SWException(
                        $"Your account is temporarily locked due to multiple failed login attempts. " +
                        $"Please try again in {minutes} minute{(minutes == 1 ? "" : "s")}.");
                }

                if (request.Password == null ||
                    !SecurePasswordHasher.Verify(request.Password, account.Password))
                {
                    // Atomic DB-side update so concurrent wrong-password attempts can't read the
                    // same count and lose increments, which would let them slip past the lockout.
                    var lockoutEnd = nowUtc.Add(LockoutDuration);
                    await _dbContext.Set<Account>()
                        .Where(a => a.Id == account.Id)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(a => a.LockoutEnd,
                                a => a.FailedLoginCount + 1 >= MaxFailedLoginAttempts ? lockoutEnd : a.LockoutEnd)
                            .SetProperty(a => a.FailedLoginCount,
                                a => a.FailedLoginCount + 1 >= MaxFailedLoginAttempts ? 0 : a.FailedLoginCount + 1));
                    throw new SWException("Invalid username or password.");
                }

                account.RegisterSuccessfulLogin();
            }

            var newRefreshToken = CreateRefreshToken(account, LoginMethod.EmailAndPassword);
            await _dbContext.SaveChangesAsync();

            // Set refresh token as a secure, HttpOnly cookie — not accessible to JavaScript.
            // Secure is always on: the app is served over HTTPS, and TLS is terminated at the
            // reverse proxy, so Request.IsHttps would otherwise be false and drop the attribute.
            _httpContextAccessor.HttpContext?.Response.Cookies.Append("refresh_token", newRefreshToken, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
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