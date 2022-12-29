using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.HttpExtensions;
using SW.Infolink.Domain.Accounts;
using SW.Infolink.Model;
using SW.PrimitiveTypes;

namespace SW.Infolink.Resources.Accounts
{
    [HandlerName("login")]
    [Unprotect]
    public class Login : ICommandHandler<UserLogin>
    {
        private readonly InfolinkDbContext _dbContext;
        private readonly InfolinkOptions _infolinkSettings;
        private readonly JwtTokenParameters _jwtTokenParameters;

        public Login(JwtTokenParameters jwtTokenParameters, InfolinkDbContext dbContext,
            InfolinkOptions infolinkSettings)
        {
            this._jwtTokenParameters = jwtTokenParameters;
            this._dbContext = dbContext;
            this._infolinkSettings = infolinkSettings;
        }

        public async Task<object> Handle(UserLogin request)
        {
            var jwtExpiryTimeSpan = TimeSpan.FromMinutes(_infolinkSettings.JwtExpiryMinutes);


            var accountQ = _dbContext
                .Set<Account>()
                .AsQueryable();

            if (!string.IsNullOrEmpty(request.RefreshToken))
            {
                var refreshToken = await _dbContext.Set<RefreshToken>()
                    .SingleOrDefaultAsync(x => x.Id == request.RefreshToken);
                if (refreshToken is null)
                {
                    throw new SWException("Invalid refreshToken.");
                }

                _dbContext.Remove(refreshToken);
                accountQ = accountQ.Where(u => u.Id == refreshToken.AccountId && !u.Disabled);
            }
            else
            {
                accountQ = accountQ.Where(u => u.Email.ToLower() == request.Username.ToLower() && !u.Disabled);
            }

            var account = await accountQ
                .SingleOrDefaultAsync();

            if (account is null)
                throw new SWValidationException(request.Username, request.Username);


            if (string.IsNullOrEmpty(request.RefreshToken))
            {
                if (request.Password == null ||
                    !SecurePasswordHasher.Verify(request.Password, account.Password))
                    throw new SWException("Invalid password.");
            }


            var result = new AccountLoginResult
            {
                Jwt = account.CreateJwt(LoginMethod.EmailAndPassword, _jwtTokenParameters, jwtExpiryTimeSpan),
                RefreshToken = CreateRefreshToken(account, LoginMethod.EmailAndPassword)
            };

            await _dbContext.SaveChangesAsync();

            return result;
        }

        private string CreateRefreshToken(Account account, LoginMethod loginMethod)
        {
            var refreshToken = new RefreshToken(account.Id, loginMethod);
            _dbContext.Add(refreshToken);
            return refreshToken.Id;
        }
    }
}