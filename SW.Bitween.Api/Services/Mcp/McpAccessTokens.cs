#nullable enable
using System;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SW.Bitween.Domain.Accounts;

namespace SW.Bitween.Services.Mcp;

/// <summary>
/// The access tokens AI apps signing in with OAuth are given: good at /mcp and nowhere else. Signed
/// with a key of their own, derived from Token:Key, and addressed to the /mcp endpoint, so the API
/// refuses them and /mcp refuses the API's sessions.
/// </summary>
public class McpAccessTokens(IConfiguration configuration, FileLinks links)
{
    static readonly JsonWebTokenHandler Handler = new();

    byte[]? key;

    SymmetricSecurityKey Key => new(key ??= HKDF.DeriveKey(HashAlgorithmName.SHA256,
        Encoding.UTF8.GetBytes(configuration["Token:Key"] ?? throw new InvalidOperationException("Token:Key is not set.")), 32,
        info: Encoding.UTF8.GetBytes("bitween:mcp-access:v1")));

    /// <summary>Who issues them: this Bitween, at its public address.</summary>
    public string Issuer => links.BaseUrl;

    /// <summary>What they're for: the MCP endpoint's address, as apps are told to ask for it.</summary>
    public string Resource => links.BaseUrl + McpTokenHandler.Path;

    /// <summary>Whether an app asked for a token for this Bitween's /mcp: what it named, or nothing.</summary>
    public bool IsResource(string? resource) =>
        string.IsNullOrEmpty(resource) ||
        string.Equals(resource.TrimEnd('/'), Resource, StringComparison.OrdinalIgnoreCase);

    public string Issue(Account account, string clientId, string clientName, TimeSpan lifetime)
    {
        var identity = account.CreateClaimsIdentity(LoginMethod.EmailAndPassword);
        identity.AddClaim(new Claim("client_id", clientId));
        identity.AddClaim(new Claim(McpTokenHandler.ViaClaim, $"{clientName} via MCP"));
        return Handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Resource,
            Subject = identity,
            Expires = DateTime.UtcNow + lifetime,
            SigningCredentials = new SigningCredentials(Key, SecurityAlgorithms.HmacSha256),
        });
    }

    /// <summary>Who a token is for, when it's one of these, unexpired and for this Bitween's /mcp; otherwise null.</summary>
    public async Task<ClaimsIdentity?> Validate(string token)
    {
        var result = await Handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = Issuer,
            ValidAudience = Resource,
            IssuerSigningKey = Key,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ClockSkew = TimeSpan.FromMinutes(1),
        });
        return result.IsValid ? result.ClaimsIdentity : null;
    }
}
