using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.Model;
using SW.HttpExtensions;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Accounts;

/// <summary>
/// Where a service account signs in: its token, traded for an ordinary short session. Everything the
/// session does is checked the way a member's is: permissions from its roles on every call, disabling
/// it ends it at once.
/// </summary>
/// <remarks>
/// No refresh token: the caller holds the token and trades it again when the session runs out. The
/// token itself is checked by <see cref="Services.ServiceTokens"/>.
/// </remarks>
[HandlerName("servicetoken")]
[Unprotect]
public class ServiceTokenSignIn(JwtTokenParameters jwtTokenParameters, BitweenOptions options,
    IHttpContextAccessor httpContextAccessor, Services.ServiceTokens tokens)
    : ICommandHandler<ServiceTokenSignInModel, object>
{
    public async Task<object> Handle(ServiceTokenSignInModel request)
    {
        var address = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var account = await tokens.Verify(request?.Token, address, DateTime.UtcNow);

        var lifetime = TimeSpan.FromMinutes(options.JwtExpiryMinutes);
        return new
        {
            Jwt = account.CreateJwt(LoginMethod.ApiKey, jwtTokenParameters, lifetime),
            ExpiresIn = (int)lifetime.TotalSeconds,
            account.Id,
            Name = account.DisplayName,
        };
    }
}
