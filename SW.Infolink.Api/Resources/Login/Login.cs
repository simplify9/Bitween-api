using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using SW.HttpExtensions;
using SW.Infolink.Model;
using SW.PrimitiveTypes;

namespace SW.Infolink.Resources.Login
{
    class Login : ICommandHandler<UserLogin>
    {
        private readonly InfolinkDbContext dbContext;
        private readonly InfolinkOptions infolinkSettings;
        private readonly JwtTokenParameters jwtTokenParameters;

        public Login(InfolinkDbContext dbContext, InfolinkOptions infolinkSettings, JwtTokenParameters jwtTokenParameters)
        {
            this.dbContext = dbContext;
            this.infolinkSettings = infolinkSettings;
            this.jwtTokenParameters = jwtTokenParameters;
        }

        public async Task<object> Handle(UserLogin request)
        {

            var cred = infolinkSettings.AdminCredentials.Split(":");

            if (cred[0].Equals(request.Username, StringComparison.OrdinalIgnoreCase))
            {
                if (cred[1].Equals(request.Password))
                {
                    var claims = new List<Claim>
                    {
                        new Claim(ClaimTypes.Name, cred[0]),
                        //new Claim("FullName", "blabla"),
                        //new Claim(ClaimTypes.Role, "Supervisor"),
                    };

                    return new
                    {
                        Jwt = jwtTokenParameters.WriteJwt(new ClaimsIdentity(claims))
                    };
                }
            }
            throw new SWUnauthorizedException();
        }
    }
}