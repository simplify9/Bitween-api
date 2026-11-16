#nullable enable
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.Resources.Accounts;
using SW.Bitween.Services;
using SW.Bitween.Services.Mcp;

namespace SW.Bitween.Controllers;

/// <summary>
/// Bitween as the OAuth server AI apps sign in with to use /mcp: where they read how to sign in, how
/// an app without a client id gets one, how a member's approval becomes a code, and how a code or a
/// refresh token becomes an access token.
/// </summary>
/// <remarks>
/// <para>
/// A controller rather than CqApi resources, because OAuth fixes the shapes: form-encoded token
/// requests, snake_case answers, and its own error codes.
/// </para>
/// <para>
/// Built on what the CLI's browser sign-in already does (<see cref="SignInCodes"/>, the consent page,
/// <see cref="Sessions"/>): the member approves in the admin UI however they signed in, and the app
/// trades the code with the verifier only it holds. What an app gets is good at /mcp alone. Everything
/// here answers 404 while Settings, MCP is off.
/// </para>
/// </remarks>
[ApiController]
public class OAuthController(
    BitweenOptions options,
    OAuthClients clients,
    McpAccessTokens tokens,
    SignInCodes codes,
    Sessions sessions,
    BitweenDbContext dbContext,
    ILogger<OAuthController> logger) : ControllerBase
{
    public const string AuthorizePage = "/oauth/authorize";

    /// <summary>The OAuth server's metadata (RFC 8414), which apps read to learn how to sign in.</summary>
    [HttpGet("/.well-known/oauth-authorization-server")]
    public IActionResult Metadata()
    {
        if (!options.McpEnabled) return NotFound();
        var issuer = tokens.Issuer;
        return Ok(new Dictionary<string, object>
        {
            ["issuer"] = issuer,
            ["authorization_endpoint"] = issuer + AuthorizePage,
            ["token_endpoint"] = issuer + "/api/oauth/token",
            ["registration_endpoint"] = issuer + "/api/oauth/register",
            ["response_types_supported"] = new[] { "code" },
            ["grant_types_supported"] = new[] { "authorization_code", "refresh_token" },
            ["code_challenge_methods_supported"] = new[] { "S256" },
            ["token_endpoint_auth_methods_supported"] = new[] { "none" },
            ["client_id_metadata_document_supported"] = true,
            ["scopes_supported"] = new[] { "mcp", "offline_access" },
        });
    }

    /// <summary>An app registering itself (RFC 7591): it's handed a client id that carries its name and addresses.</summary>
    [HttpPost("/api/oauth/register")]
    public IActionResult Register([FromBody] RegistrationRequest request)
    {
        if (!options.McpEnabled) return NotFound();
        try
        {
            var client = clients.Register(request.ClientName, request.RedirectUris);
            return StatusCode(StatusCodes.Status201Created, new Dictionary<string, object>
            {
                ["client_id"] = client.Id,
                ["client_id_issued_at"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ["client_name"] = client.Name,
                ["redirect_uris"] = client.RedirectUris,
                ["grant_types"] = new[] { "authorization_code", "refresh_token" },
                ["response_types"] = new[] { "code" },
                ["token_endpoint_auth_method"] = "none",
            });
        }
        catch (ArgumentException e)
        {
            return BadRequest(new { error = "invalid_redirect_uri", error_description = e.Message });
        }
    }

    /// <summary>For the consent page: who's asking, and where approving sends the member back to.</summary>
    [Authorize]
    [HttpGet("/api/oauth/client")]
    public async Task<IActionResult> Client([FromQuery(Name = "client_id")] string? clientId,
        [FromQuery(Name = "redirect_uri")] string? redirectUri)
    {
        if (!options.McpEnabled) return NotFound(new { message = "AI apps can't connect to this Bitween: Settings, MCP is off." });
        var client = await clients.Find(clientId);
        if (client is null) return BadRequest(new { message = "This app isn't one Bitween can identify, so it can't be signed in." });
        if (!OAuthClients.Allows(client, redirectUri))
            return BadRequest(new { message = "This app asked to send you somewhere it didn't register, so it can't be signed in." });
        return Ok(new { client.Name, RedirectHost = new Uri(redirectUri!).Authority, OnlyThisComputer = OAuthClients.OnlyLoopback(client) });
    }

    /// <summary>
    /// The member's answer on the consent page: where to send them back to, with a code for the app
    /// when they approved, or with <c>access_denied</c> when they didn't. Built here rather than in the
    /// page, so only an address the app registered is ever used.
    /// </summary>
    [Authorize]
    [HttpPost("/api/oauth/grant")]
    public async Task<IActionResult> Grant([FromBody] GrantRequest request)
    {
        if (!options.McpEnabled) return NotFound(new { message = "AI apps can't connect to this Bitween: Settings, MCP is off." });
        var client = await clients.Find(request.ClientId);
        if (client is null || !OAuthClients.Allows(client, request.RedirectUri))
            return BadRequest(new { message = "This app can't be signed in." });

        var answer = new Dictionary<string, string?> { ["state"] = request.State, ["iss"] = tokens.Issuer };
        if (!request.Approve)
            answer["error"] = "access_denied";
        else if (request.CodeChallengeMethod != "S256" || !SignInCodes.IsChallenge(request.CodeChallenge))
            answer["error"] = "invalid_request";
        else if (!tokens.IsResource(request.Resource))
            answer["error"] = "invalid_target";
        else
        {
            var accountId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var account = await Members.FindAsync(dbContext, accountId);
            if (account is null || account.Disabled || account.MustChangePassword) return Forbid();
            answer["code"] = codes.Issue(account.Id, request.CodeChallenge!, DateTimeOffset.UtcNow,
                Binding(client.Id, request.RedirectUri!));
        }

        foreach (var (name, value) in answer)
            if (value is null) answer.Remove(name);
        return Ok(new { RedirectTo = QueryHelpers.AddQueryString(request.RedirectUri!, answer) });
    }

    /// <summary>A code or a refresh token, traded for an access token for /mcp and a refresh token (RFC 6749).</summary>
    [HttpPost("/api/oauth/token")]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> Token([FromForm] TokenRequest request)
    {
        if (!options.McpEnabled) return NotFound();
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";

        var client = await clients.Find(request.ClientId);
        if (client is null) return Error("invalid_client", "This app isn't one Bitween can identify.");
        if (!tokens.IsResource(request.Resource)) return Error("invalid_target", "Bitween issues tokens for its /mcp endpoint only.");
        var now = DateTime.UtcNow;

        RefreshToken? renewed = null;
        int accountId;
        switch (request.GrantType)
        {
            case "authorization_code":
                if (!OAuthClients.Allows(client, request.RedirectUri)) return Error("invalid_grant", "The redirect address isn't the one approved.");
                var code = codes.Redeem(request.Code, request.CodeVerifier, now, Binding(client.Id, request.RedirectUri!));
                if (code is null || !await sessions.FirstUse(code.Nonce, code.ExpiresOn, now))
                    return Error("invalid_grant", "This code is expired, already used, or wasn't issued to this app.");
                accountId = code.AccountId;
                break;
            case "refresh_token":
                renewed = await sessions.Spend(request.RefreshToken, now, client.Id);
                if (renewed is null) return Error("invalid_grant", "This sign-in has ended. Connect the app again.");
                accountId = renewed.AccountId;
                break;
            default:
                return Error("unsupported_grant_type", "Use authorization_code or refresh_token.");
        }

        // Disabled, deleted or told to change its password since: no tokens either way.
        var account = await Members.FindAsync(dbContext, accountId);
        if (account is null || account.Disabled || account.MustChangePassword)
        {
            logger.LogWarning("OAuth tokens refused for {ClientName}: the account can't sign in.", client.Name);
            return Error("invalid_grant", "This account can't sign in.");
        }

        // A sign-in, not a session renewing itself: the member's last sign-in, and the trail's.
        if (renewed is null) account.SignedIn();
        var refresh = sessions.Start(account.Id, LoginMethod.EmailAndPassword, client.Id, client.Name);
        await dbContext.SaveChangesAsync();

        var lifetime = TimeSpan.FromMinutes(options.JwtExpiryMinutes);
        return Ok(new Dictionary<string, object>
        {
            ["access_token"] = tokens.Issue(account, client.Id, client.Name, lifetime),
            ["token_type"] = "Bearer",
            ["expires_in"] = (int)lifetime.TotalSeconds,
            ["refresh_token"] = refresh.Id,
            ["scope"] = "mcp",
        });
    }

    /// <summary>What a code is bound to: the app, the address it goes back to, and /mcp.</summary>
    string Binding(string clientId, string redirectUri) => $"{clientId}\n{redirectUri}\n{tokens.Resource}";

    BadRequestObjectResult Error(string error, string description) =>
        BadRequest(new { error, error_description = description });

    public class RegistrationRequest
    {
        [JsonPropertyName("client_name")]
        public string? ClientName { get; set; }

        [JsonPropertyName("redirect_uris")]
        public List<string>? RedirectUris { get; set; }
    }

    public class GrantRequest
    {
        public string? ClientId { get; set; }
        public string? RedirectUri { get; set; }
        public string? CodeChallenge { get; set; }
        public string? CodeChallengeMethod { get; set; }
        public string? Resource { get; set; }
        public string? State { get; set; }
        public bool Approve { get; set; }
    }

    public class TokenRequest
    {
        [FromForm(Name = "grant_type")] public string? GrantType { get; set; }
        [FromForm(Name = "client_id")] public string? ClientId { get; set; }
        [FromForm(Name = "code")] public string? Code { get; set; }
        [FromForm(Name = "code_verifier")] public string? CodeVerifier { get; set; }
        [FromForm(Name = "redirect_uri")] public string? RedirectUri { get; set; }
        [FromForm(Name = "refresh_token")] public string? RefreshToken { get; set; }
        [FromForm(Name = "resource")] public string? Resource { get; set; }
    }
}
