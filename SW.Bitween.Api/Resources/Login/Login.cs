using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using SW.HttpExtensions;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Login
{
    [Unprotect]
    class Login : ICommandHandler<UserLogin,object>
    {
        private readonly BitweenDbContext dbContext;
        private readonly BitweenOptions BitweenSettings;
        private readonly JwtTokenParameters jwtTokenParameters;

        public Login(BitweenDbContext dbContext, BitweenOptions BitweenSettings, JwtTokenParameters jwtTokenParameters)
        {
            this.dbContext = dbContext;
            this.BitweenSettings = BitweenSettings;
            this.jwtTokenParameters = jwtTokenParameters;
        }

        public Task<object> Handle(UserLogin request)
        {

            var cred = BitweenSettings.AdminCredentials.Split(":");

            if (cred[0].Equals(request.Username, StringComparison.OrdinalIgnoreCase))
            {
                if (cred[1].Equals(request.Password))
                {
                    var claims = new List<Claim>
                    {
                        new Claim(ClaimTypes.Name, cred[0]),
                    };

                    return Task.FromResult<object>(new
                    {
                        Jwt = jwtTokenParameters.WriteJwt(new ClaimsIdentity(claims))
                    });
                }
            }
            throw new SWUnauthorizedException();
        }
    }
}