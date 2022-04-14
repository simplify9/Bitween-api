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
    public class Login: ICommandHandler<UserLogin>
    {
        private readonly InfolinkDbContext dbContext;
        private readonly InfolinkOptions infolinkSettings;
        private readonly JwtTokenParameters jwtTokenParameters;

        public Login(JwtTokenParameters jwtTokenParameters, InfolinkDbContext dbContext, InfolinkOptions infolinkSettings)
        {
            this.jwtTokenParameters = jwtTokenParameters;
            this.dbContext = dbContext;
            this.infolinkSettings = infolinkSettings;
        }

        public async Task<object> Handle(UserLogin request)
        {
            var jwtExpiryTimeSpan = TimeSpan.FromMinutes(infolinkSettings.JwtExpiryMinutes);
            var loginResult = new AccountLoginResult();
            
            var account = await dbContext
                .Set<Account>()
                .Where(u => u.Email.ToLower() == request.Username.ToLower() && !u.Disabled)
                .SingleOrDefaultAsync();
            
            if (account == null)
                throw new SWNotFoundException(request.Username);
            

            if (request.Password == null || !SecurePasswordHasher.Verify(request.Password, account.Password))
                throw new SWException("Invalid password.");

            var result = new AccountLoginResult
            {
                Jwt = account.CreateJwt(LoginMethod.EmailAndPassword, jwtTokenParameters, jwtExpiryTimeSpan),
                RefreshToken = CreateRefreshToken(account, LoginMethod.EmailAndPassword)
            };

            return result;
        }
        
        private string CreateRefreshToken(Account account, LoginMethod loginMethod)
        {
            var refreshToken = new RefreshToken(account.Id, loginMethod);
            dbContext.Add(refreshToken);
            return refreshToken.Id;
        }
    }
}