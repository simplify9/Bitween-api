using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.Bitween.Services;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// Single sign-on with an OpenID Connect provider through the start handler and the whole login
/// handler, against a provider played here: its metadata, its keys and its token endpoint.
/// </summary>
[Collection("Bitween")]
public class OidcSignInTests : IDisposable
{
    const string Issuer = "https://id.test/realms/main";
    const string ClientId = "bitween";
    static readonly RsaSecurityKey Key = new(RSA.Create(2048)) { KeyId = "provider" };
    static int _seq;

    readonly BitweenFixture fixture;
    readonly (string, string, string, bool) before;

    /// <summary>What the provider puts in the next ID token, and what it was last sent.</summary>
    sealed class Provider
    {
        public string Subject = Guid.NewGuid().ToString();
        public string Email;
        public bool? EmailVerified = true;
        public string Nonce;
        public string IssuerOverride;
        public RsaSecurityKey SignWith = Key;
        public Dictionary<string, string> Exchanged;
    }

    readonly Provider provider = new();

    public OidcSignInTests(BitweenFixture fixture)
    {
        this.fixture = fixture;
        var options = fixture.App.Services.GetRequiredService<BitweenOptions>();
        before = (options.OidcAuthority, options.OidcClientId, options.OidcClientSecret, options.DisableEmailPasswordLogin);
        options.OidcAuthority = Issuer;
        options.OidcClientId = ClientId;
        options.OidcClientSecret = null;
        OidcSignIn.HandlerOverride = () => new FakeProvider(provider);
    }

    public void Dispose()
    {
        OidcSignIn.HandlerOverride = null;
        var options = fixture.App.Services.GetRequiredService<BitweenOptions>();
        (options.OidcAuthority, options.OidcClientId, options.OidcClientSecret, options.DisableEmailPasswordLogin) = before;
    }

    sealed class FakeProvider(Provider p) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            object body;
            if (path.EndsWith("/.well-known/openid-configuration"))
                body = new
                {
                    issuer = Issuer,
                    authorization_endpoint = $"{Issuer}/auth",
                    token_endpoint = $"{Issuer}/token",
                    jwks_uri = $"{Issuer}/certs",
                    token_endpoint_auth_methods_supported = new[] { "client_secret_basic", "client_secret_post" },
                };
            else if (path.EndsWith("/certs"))
            {
                var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(Key.Rsa.ExportParameters(false)) { KeyId = Key.KeyId });
                body = new { keys = new[] { new { kty = jwk.Kty, kid = jwk.Kid, use = "sig", alg = "RS256", n = jwk.N, e = jwk.E } } };
            }
            else if (path.EndsWith("/token"))
            {
                p.Exchanged = (await request.Content!.ReadAsStringAsync(cancellationToken)).Split('&')
                    .Select(kv => kv.Split('=')).ToDictionary(kv => kv[0], kv => Uri.UnescapeDataString(kv[1].Replace('+', ' ')));
                body = new { id_token = IdToken(p), access_token = "unused", token_type = "Bearer" };
            }
            else return new HttpResponseMessage(HttpStatusCode.NotFound);

            return new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        }
    }

    static string IdToken(Provider p)
    {
        var claims = new List<Claim> { new("sub", p.Subject), new("nonce", p.Nonce ?? "") };
        if (p.Email != null) claims.Add(new Claim("email", p.Email));
        if (p.EmailVerified != null) claims.Add(new Claim("email_verified", p.EmailVerified.Value ? "true" : "false", ClaimValueTypes.Boolean));
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: p.IssuerOverride ?? Issuer, audience: ClientId, claims: claims,
            notBefore: DateTime.UtcNow.AddMinutes(-1), expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: new SigningCredentials(p.SignWith, SecurityAlgorithms.RsaSha256)));
    }

    /// <summary>Starts a sign-in: the provider's address, and the flow cookie the browser keeps.</summary>
    async Task<(Dictionary<string, string> Query, string Cookie)> Start()
    {
        await using var scope = fixture.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext();
        accessor.HttpContext.Request.Scheme = "https";
        accessor.HttpContext.Request.Host = new HostString("bitween.test");
        var handler = ActivatorUtilities.CreateInstance<Resources.Accounts.OidcStart>(scope.ServiceProvider);
        var url = (await handler.Handle(new OidcStartRequest())).Url;

        var setCookie = accessor.HttpContext.Response.Headers.SetCookie.ToString();
        var cookie = setCookie.Split(';')[0].Split('=', 2)[1];
        var query = QueryHelpers.ParseQuery(new Uri(url).Query).ToDictionary(kv => kv.Key, kv => kv.Value.ToString());
        provider.Nonce = query["nonce"];
        return (query, cookie);
    }

    async Task<object> Finish(string state, string cookie, string code = "the-code")
    {
        await using var scope = fixture.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext();
        accessor.HttpContext.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.10");
        if (cookie != null) accessor.HttpContext.Request.Headers.Cookie = $"{OidcSignIn.FlowCookie}={cookie}";
        var handler = ActivatorUtilities.CreateInstance<Resources.Accounts.Login>(scope.ServiceProvider);
        return await handler.Handle(new UserLogin { OidcCode = code, OidcState = state });
    }

    async Task<object> SignIn()
    {
        var (query, cookie) = await Start();
        return await Finish(query["state"], cookie);
    }

    async Task<string> Refused()
    {
        var (query, cookie) = await Start();
        return (await Assert.ThrowsAsync<SWException>(() => Finish(query["state"], cookie))).Message;
    }

    async Task<Account> Member(bool disabled = false)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var account = new Account("SSO Member", $"sso{Interlocked.Increment(ref _seq)}-{Guid.NewGuid():N}@example.com"[..36],
            null, AccountRole.Member);
        if (disabled) account.SetDisabled(true);
        db.Add(account);
        await db.SaveChangesAsync();
        return account;
    }

    async Task<string> BoundIdentity(int accountId)
    {
        await using var scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<BitweenDbContext>().Set<Account>()
            .Where(a => a.Id == accountId).Select(a => a.OidcIdentity).SingleAsync();
    }

    [Fact]
    public async Task The_start_asks_for_a_code_with_PKCE_and_the_exchange_proves_it()
    {
        var account = await Member();
        provider.Email = account.Email;
        var (query, cookie) = await Start();

        Assert.Equal("code", query["response_type"]);
        Assert.Equal(ClientId, query["client_id"]);
        Assert.Equal("https://bitween.test/oidc-callback", query["redirect_uri"]);
        Assert.Equal("openid email profile", query["scope"]);
        Assert.Equal("S256", query["code_challenge_method"]);

        Assert.NotNull(await Finish(query["state"], cookie));
        var verifier = provider.Exchanged["code_verifier"];
        Assert.Equal(query["code_challenge"], Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
        Assert.Equal("the-code", provider.Exchanged["code"]);
        Assert.Equal("https://bitween.test/oidc-callback", provider.Exchanged["redirect_uri"]);
        Assert.False(provider.Exchanged.ContainsKey("client_secret"));
    }

    [Fact]
    public async Task The_first_sign_in_binds_the_identity_which_then_signs_in_whatever_its_address()
    {
        var account = await Member();
        provider.Email = account.Email;

        Assert.NotNull(await SignIn());
        Assert.Equal($"{provider.Subject}@{Issuer}", await BoundIdentity(account.Id));

        provider.Email = "renamed@example.com";
        Assert.NotNull(await SignIn());
    }

    [Fact]
    public async Task Another_identity_with_the_same_address_is_refused_once_one_is_bound()
    {
        var account = await Member();
        provider.Email = account.Email;
        await SignIn();

        provider.Subject = Guid.NewGuid().ToString();
        Assert.Contains("different single sign-on identity", await Refused());
    }

    [Fact]
    public async Task An_address_the_provider_hasnt_verified_matches_no_account()
    {
        var account = await Member();
        provider.Email = account.Email;
        provider.EmailVerified = false;
        Assert.Contains("not registered", await Refused());
        Assert.Null(await BoundIdentity(account.Id));
    }

    [Fact]
    public async Task Unknown_and_disabled_accounts_are_refused()
    {
        provider.Email = "nobody-at-all@example.com";
        Assert.Contains("not registered", await Refused());

        var disabled = await Member(disabled: true);
        provider.Email = disabled.Email;
        Assert.Contains("disabled", await Refused());
    }

    [Fact]
    public async Task A_state_or_cookie_from_another_attempt_signs_nobody_in()
    {
        var account = await Member();
        provider.Email = account.Email;

        var (query, cookie) = await Start();
        Assert.Contains("didn't come back", (await Assert.ThrowsAsync<SWException>(() => Finish("forged", cookie))).Message);
        Assert.Contains("another browser", (await Assert.ThrowsAsync<SWException>(() => Finish(query["state"], null))).Message);
        Assert.Contains("another browser", (await Assert.ThrowsAsync<SWException>(() => Finish(query["state"], cookie[..^4] + "AAAA"))).Message);
    }

    [Fact]
    public async Task A_token_signed_by_another_key_or_issuer_or_for_another_nonce_is_refused()
    {
        var account = await Member();
        provider.Email = account.Email;

        provider.SignWith = new RsaSecurityKey(RSA.Create(2048)) { KeyId = "provider" };
        Assert.Contains("Could not sign you in", await Refused());
        provider.SignWith = Key;

        provider.IssuerOverride = "https://elsewhere.test";
        Assert.Contains("Could not sign you in", await Refused());
        provider.IssuerOverride = null;

        var (query, cookie) = await Start();
        provider.Nonce = "another";
        Assert.Contains("Could not sign you in", (await Assert.ThrowsAsync<SWException>(() => Finish(query["state"], cookie))).Message);
        Assert.Null(await BoundIdentity(account.Id));
    }

    [Fact]
    public async Task A_confidential_client_sends_its_secret_and_password_sign_in_can_be_turned_off()
    {
        var options = fixture.App.Services.GetRequiredService<BitweenOptions>();
        options.OidcClientSecret = "s3cret";
        var account = await Member();
        provider.Email = account.Email;

        Assert.NotNull(await SignIn());
        Assert.Equal("s3cret", provider.Exchanged["client_secret"]);

        options.DisableEmailPasswordLogin = true;
        Assert.NotNull(await SignIn());
    }

    [Fact]
    public async Task Turned_off_nothing_starts()
    {
        var options = fixture.App.Services.GetRequiredService<BitweenOptions>();
        options.OidcAuthority = null;
        await using var scope = fixture.CreateScope();
        var handler = ActivatorUtilities.CreateInstance<Resources.Accounts.OidcStart>(scope.ServiceProvider);
        Assert.Contains("not configured", (await Assert.ThrowsAsync<SWException>(() => handler.Handle(new OidcStartRequest()))).Message);
    }
}
