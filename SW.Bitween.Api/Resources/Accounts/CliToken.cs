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
    CliSignInCodes codes, ILogger<CliToken> logger) : ICommandHandler<CliTokenRequest, object>
{
    public async Task<object> Handle(CliTokenRequest request)
    {
        var accountId = codes.Redeem(request?.Code, request?.CodeVerifier, DateTimeOffset.UtcNow);
        var account = accountId is null ? null : await dbContext.Set<Account>().FindAsync(accountId.Value);

        // Disabled or told to change its password since the code was issued: no session either way.
        if (account is null || account.Disabled || account.MustChangePassword)
        {
            logger.LogWarning("CLI sign-in refused: the code was invalid, expired, or its account can't sign in.");
            throw new SWException("This sign-in has expired or isn't valid. Run bitween login again.");
        }

        var refreshToken = new RefreshToken(account.Id, LoginMethod.EmailAndPassword);
        dbContext.Add(refreshToken);
        await dbContext.SaveChangesAsync();

        return new
        {
            Jwt = account.CreateJwt(LoginMethod.EmailAndPassword, jwtTokenParameters, TimeSpan.FromMinutes(options.JwtExpiryMinutes)),
            RefreshToken = refreshToken.Id,
            account.Email,
        };
    }
}
