using System;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SW.Bitween.Domain.Accounts;
using SW.PrimitiveTypes;

namespace SW.Bitween.Services.Mcp;

/// <summary>
/// Who an AI app at /mcp is acting as, from the bearer token it sends on every request: a service
/// account's <c>bwt_</c> token, checked as signing in with it is, or an access token issued to the app
/// when a member signed it in with OAuth (see <see cref="McpAccessTokens"/>). Used for /mcp alone; the
/// API's own scheme never sees these requests.
/// </summary>
public class McpTokenHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ServiceTokens serviceTokens,
    McpAccessTokens accessTokens) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "McpToken";

    /// <summary>The path the MCP endpoint answers at.</summary>
    public const string Path = "/mcp";

    /// <summary>Carried on the identity, so what an AI app does can be told from what its person did.</summary>
    public const string ViaClaim = "bitween_via";

    /// <summary>The bearer token a request carries, or null; the rate limiter budgets by it too.</summary>
    public static string BearerOf(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;
        var token = header["Bearer ".Length..].Trim();
        return token.Length == 0 ? null : token;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = BearerOf(Request);
        if (token is null) return AuthenticateResult.NoResult();

        if (token.StartsWith(ServiceToken.Prefix, StringComparison.Ordinal))
        {
            try
            {
                var address = Context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                var account = await serviceTokens.Verify(token, address, DateTime.UtcNow);
                var identity = account.CreateClaimsIdentity(LoginMethod.ApiKey);
                identity.AddClaim(new Claim(ViaClaim, "MCP"));
                return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
            }
            catch (SWException e)
            {
                return AuthenticateResult.Fail(e.Message);
            }
        }

        // An AI app signed in with OAuth: one of the tokens issued for /mcp, and nothing else.
        var app = await accessTokens.Validate(token);
        return app is null
            ? AuthenticateResult.Fail("This token is expired, or wasn't issued for Bitween's MCP server.")
            : AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(app), Scheme.Name));
    }
}
