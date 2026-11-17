using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SW.Bitween.Domain.Accounts;
using SW.HttpExtensions;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Accounts;

public class CliTokenRequest
{
    public string Code { get; set; }
    public string CodeVerifier { get; set; }
}

/// <summary>
/// Where the bitween CLI trades the code from <see cref="CliGrant"/>, and the verifier only it
/// holds, for a session: an access token and a refresh token, both in the body since the CLI keeps
/// no cookies. The refresh token renews and ends like the browser's (see <see cref="Login"/>).
/// </summary>
[HandlerName("clitoken")]
[Unprotect]
public class CliToken(BitweenDbContext dbContext, JwtTokenParameters jwtTokenParameters, BitweenOptions options,
    SignInCodes codes, Services.Sessions sessions, ILogger<CliToken> logger) : ICommandHandler<CliTokenRequest, object>
{
    public async Task<object> Handle(CliTokenRequest request)
    {
        var now = DateTime.UtcNow;
        var code = codes.Redeem(request?.Code, request?.CodeVerifier, now);
        // Once only: a code redeemed before is refused like one that never was.
        if (code is not null && !await sessions.FirstUse(code.Nonce, code.ExpiresOn, now)) code = null;
        var account = code is null ? null : await Members.FindAsync(dbContext, code.AccountId);

        // Disabled or told to change its password since the code was issued: no session either way.
        if (account is null || account.Disabled || account.MustChangePassword)
        {
            logger.LogWarning("CLI sign-in refused: the code was invalid, expired, or its account can't sign in.");
            throw new SWException("This sign-in has expired or isn't valid. Run bitween login again.");
        }

        account.SignedIn();
        var refreshToken = sessions.Start(account.Id, LoginMethod.EmailAndPassword, clientName: Services.Sessions.Cli);
        await dbContext.SaveChangesAsync();

        return new
        {
            Jwt = account.CreateJwt(LoginMethod.EmailAndPassword, jwtTokenParameters, TimeSpan.FromMinutes(options.JwtExpiryMinutes),
                via: Services.Sessions.Cli),
            RefreshToken = refreshToken.Id,
            account.Email,
        };
    }
}
