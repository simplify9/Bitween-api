using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain.Accounts;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Accounts
{
    public class UserLogout { }

    [HandlerName("logout")]
    [Unprotect]
    public class Logout : ICommandHandler<UserLogout, object>
    {
        private readonly BitweenDbContext _dbContext;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public Logout(BitweenDbContext dbContext, IHttpContextAccessor httpContextAccessor)
        {
            _dbContext = dbContext;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<object> Handle(UserLogout request)
        {
            var httpContext = _httpContextAccessor.HttpContext;
            var refreshTokenValue = httpContext?.Request.Cookies["refresh_token"];

            if (!string.IsNullOrEmpty(refreshTokenValue))
            {
                var refreshToken = await _dbContext.Set<RefreshToken>()
                    .SingleOrDefaultAsync(x => x.Id == refreshTokenValue);

                if (refreshToken != null)
                {
                    _dbContext.Remove(refreshToken);
                    await _dbContext.SaveChangesAsync();
                }

                httpContext.Response.Cookies.Delete("refresh_token");
            }

            return new { };
        }
    }
}
