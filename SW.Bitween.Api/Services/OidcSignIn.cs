using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace SW.Bitween.Services;

/// <summary>
/// Sign-in with any OpenID Connect provider - Keycloak, Okta, Entra ID, Google, ADFS - by the
/// authorization code flow with PKCE, the code exchanged here rather than in the browser.
/// </summary>
/// <remarks>
/// <para>
/// Exchanging the code on the server is what lets a confidential client, with a secret, work, and
/// needs no CORS at the provider's token endpoint, which many don't allow. The browser only ever
/// carries the code from the provider back to Bitween.
/// </para>
/// <para>
/// What the flow has to remember between the redirect out and the one back - the state, the nonce
/// and the PKCE verifier - travels in an HttpOnly cookie encrypted with a key derived from
/// <c>Token:Key</c>, so any node can finish a sign-in another node started, and nothing is stored.
/// </para>
/// </remarks>
public class OidcSignIn(BitweenOptions options, SealedState sealedState, IHttpContextAccessor httpContextAccessor,
    FileLinks fileLinks)
{
    public const string FlowCookie = "bitween_oidc";
    public const string CallbackPath = "/oidc-callback";
    const string FlowPurpose = "bitween:oidc-flow:v1";
    static readonly TimeSpan FlowLifetime = TimeSpan.FromMinutes(10);

    /// <summary>Who an ID token says it is: the address to match, when the provider vouches for it, and the identity to bind.</summary>
    public record OidcIdentity(string Email, string SubjectAndIssuer);

    /// <summary>Why a sign-in was refused, in words for the person signing in; the reason itself is logged.</summary>
    public class Refused(string reason) : Exception(reason);

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    static readonly ConcurrentDictionary<string, ConfigurationManager<OpenIdConnectConfiguration>> Metadata = new();

    /// <summary>For tests: answers in place of the network, as <see cref="Marketplace.RegistryClient"/>'s does.</summary>
    internal static Func<HttpMessageHandler> HandlerOverride { get; set; }

    static HttpClient Client => HandlerOverride is { } handler ? new HttpClient(handler()) : Http;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(options.OidcAuthority) && !string.IsNullOrWhiteSpace(options.OidcClientId);

    /// <summary>Where the provider sends the browser back: the setting, or this Bitween's own address.</summary>
    public string RedirectUri =>
        string.IsNullOrWhiteSpace(options.OidcRedirectUri) ? fileLinks.BaseUrl + CallbackPath : options.OidcRedirectUri.Trim();

    string Authority => options.OidcAuthority.Trim().TrimEnd('/');

    Task<OpenIdConnectConfiguration> MetadataAsync(CancellationToken cancellation)
    {
        var address = Authority + "/.well-known/openid-configuration";
        // A test's provider answers through its own handler, so its metadata is never cached.
        var manager = HandlerOverride != null
            ? new ConfigurationManager<OpenIdConnectConfiguration>(address, new OpenIdConnectConfigurationRetriever(), new HttpDocumentRetriever(Client))
            : Metadata.GetOrAdd(address, a => new ConfigurationManager<OpenIdConnectConfiguration>(
                a, new OpenIdConnectConfigurationRetriever(), new HttpDocumentRetriever(Http)));
        return manager.GetConfigurationAsync(cancellation);
    }

    /// <summary>
    /// The provider's sign-in address for one attempt, remembering the attempt in the flow cookie.
    /// </summary>
    public async Task<string> StartAsync(CancellationToken cancellation = default)
    {
        if (!IsConfigured) throw new Refused("Single sign-on is not configured.");
        var metadata = await MetadataAsync(cancellation);

        var flow = new Flow(SealedState.Random(), SealedState.Random(), SealedState.Random(48), RedirectUri, DateTimeOffset.UtcNow.Add(FlowLifetime).ToUnixTimeSeconds());
        httpContextAccessor.HttpContext?.Response.Cookies.Append(FlowCookie, sealedState.Seal(FlowPurpose, flow), new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            // Lax, not Strict: the provider's redirect back is a top-level navigation from its site,
            // and the callback page's request to finish the sign-in has to carry the cookie.
            SameSite = SameSiteMode.Lax,
            MaxAge = FlowLifetime,
            Path = "/",
        });

        var challenge = SealedState.Challenge(flow.Verifier);
        return QueryHelpers.AddQueryString(metadata.AuthorizationEndpoint, new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = options.OidcClientId.Trim(),
            ["redirect_uri"] = flow.RedirectUri,
            ["scope"] = Scopes,
            ["state"] = flow.State,
            ["nonce"] = flow.Nonce,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
        });
    }

    string Scopes
    {
        get
        {
            var scopes = (options.OidcScopes ?? "").Split([' ', ','], StringSplitOptions.RemoveEmptyEntries).ToList();
            if (!scopes.Contains("openid")) scopes.Insert(0, "openid");
            if (scopes.Count == 1) scopes.AddRange(["email", "profile"]);
            return string.Join(' ', scopes);
        }
    }

    /// <summary>
    /// Finishes the attempt the flow cookie remembers: checks the state, exchanges the code, and
    /// validates the ID token the provider returns. The cookie is spent either way.
    /// </summary>
    public async Task<OidcIdentity> CompleteAsync(string code, string state, CancellationToken cancellation = default)
    {
        if (!IsConfigured) throw new Refused("Single sign-on is not configured.");
        var context = httpContextAccessor.HttpContext;
        var sealedFlow = context?.Request.Cookies[FlowCookie];
        context?.Response.Cookies.Delete(FlowCookie, new CookieOptions { Path = "/", Secure = true, HttpOnly = true, SameSite = SameSiteMode.Lax });

        var flow = sealedState.Open<Flow>(FlowPurpose, sealedFlow);
        if (flow == null || DateTimeOffset.UtcNow.ToUnixTimeSeconds() > flow.Expires)
            throw new Refused("The sign-in took too long or was started in another browser. Please try again.");
        if (string.IsNullOrEmpty(state) || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(state), Encoding.UTF8.GetBytes(flow.State)))
            throw new Refused("The sign-in didn't come back from the request this browser made. Please try again.");

        var metadata = await MetadataAsync(cancellation);
        var idToken = await ExchangeAsync(metadata, code, flow, cancellation);
        return Validate(idToken, metadata.SigningKeys, metadata.Issuer, options.OidcClientId.Trim(), flow.Nonce);
    }

    async Task<string> ExchangeAsync(OpenIdConnectConfiguration metadata, string code, Flow flow, CancellationToken cancellation)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code ?? "",
            ["redirect_uri"] = flow.RedirectUri,
            ["client_id"] = options.OidcClientId.Trim(),
            ["code_verifier"] = flow.Verifier,
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, metadata.TokenEndpoint);
        if (!string.IsNullOrEmpty(options.OidcClientSecret))
        {
            // In the body unless the provider says it takes only Basic, as the spec's default is Basic
            // but nearly every provider takes both.
            var methods = metadata.TokenEndpointAuthMethodsSupported;
            if (methods.Count > 0 && !methods.Contains("client_secret_post") && methods.Contains("client_secret_basic"))
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(
                    $"{Uri.EscapeDataString(options.OidcClientId.Trim())}:{Uri.EscapeDataString(options.OidcClientSecret)}")));
            else
                form["client_secret"] = options.OidcClientSecret;
        }
        request.Content = new FormUrlEncodedContent(form);

        using var response = await Client.SendAsync(request, cancellation);
        var body = await response.Content.ReadAsStringAsync(cancellation);
        if (!response.IsSuccessStatusCode)
            throw new SecurityTokenException($"The provider refused the code ({(int)response.StatusCode}): {Shorten(body)}");
        using var json = JsonDocument.Parse(body);
        return json.RootElement.TryGetProperty("id_token", out var token) && token.ValueKind == JsonValueKind.String
            ? token.GetString()
            : throw new SecurityTokenException("The provider's answer had no id_token. Is the openid scope allowed for this client?");
    }

    /// <summary>The checks on an ID token, against the given keys, so they can be tested.</summary>
    public static OidcIdentity Validate(string idToken, IEnumerable<SecurityKey> signingKeys, string issuer,
        string clientId, string nonce)
    {
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        handler.ValidateToken(idToken, new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = signingKeys,
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = clientId,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(2),
        }, out var validated);
        var claims = ((JwtSecurityToken)validated).Claims.ToList();
        string Claim(string type) => claims.FirstOrDefault(c => c.Type == type)?.Value;

        if (Claim("nonce") != nonce)
            throw new SecurityTokenException("The ID token's nonce isn't the one this sign-in sent.");
        var subject = Claim("sub");
        if (string.IsNullOrEmpty(subject))
            throw new SecurityTokenException("The ID token has no subject.");

        // An address the provider says it hasn't verified could be anyone's, so it can't pick the
        // account; the bound identity still can.
        var unverified = bool.TryParse(Claim("email_verified"), out var verified) && !verified;
        var email = unverified ? null : Claim("email")?.Trim().ToLowerInvariant();
        return new OidcIdentity(string.IsNullOrEmpty(email) ? null : email, $"{subject}@{issuer}");
    }

    internal record Flow(string State, string Nonce, string Verifier, string RedirectUri, long Expires);

    static string Shorten(string text) => text.Length > 300 ? text[..300] + "..." : text;
}
