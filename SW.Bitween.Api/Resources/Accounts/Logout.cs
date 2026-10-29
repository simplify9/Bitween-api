using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain.Accounts;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Accounts
{
    public class UserLogout
    {
        /// <summary>
        /// The refresh token to end, for a client without cookies such as the bitween CLI. Ending
        /// one needs only the token itself, which is what renewing a session with it needs too.
        /// </summary>
        public string RefreshToken { get; set; }
    }

    [HandlerName("logout")]
    [Unprotect]
    public class Logout(BitweenDbContext dbContext, IHttpContextAccessor httpContextAccessor)
        : ICommandHandler<UserLogout, object>
    {
        public async Task<object> Handle(UserLogout request)
        {
            var httpContext = httpContextAccessor.HttpContext;
            var cookie = httpContext?.Request.Cookies["refresh_token"];
            var refreshTokenValue = string.IsNullOrEmpty(cookie) ? request?.RefreshToken : cookie;

            if (!string.IsNullOrEmpty(refreshTokenValue))
            {
                var refreshToken = await dbContext.Set<RefreshToken>()
                    .SingleOrDefaultAsync(x => x.Id == refreshTokenValue);

                if (refreshToken != null)
                {
                    dbContext.Remove(refreshToken);
                    await dbContext.SaveChangesAsync();
                }

                if (!string.IsNullOrEmpty(cookie)) httpContext.Response.Cookies.Delete("refresh_token");
            }

            // Tell the browser to wipe cookies, cache and storage for this origin on logout.
            httpContext?.Response.Headers.Append("Clear-Site-Data", "\"cache\", \"cookies\", \"storage\"");

            return new { };
        }
    }
}
