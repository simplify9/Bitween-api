using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Connections;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.Bitween.Services.Adapters;
using SW.Bitween.Services.Connections;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// Connections: tokens asked for, kept, renewed and rotated against a provider played here, and
/// put into the settings of whatever adapter refers to them.
/// </summary>
[Collection("Bitween")]
public class ConnectionTests : IDisposable
{
    readonly BitweenFixture fixture;
    readonly Provider provider = new();
    static int _seq;

    public ConnectionTests(BitweenFixture fixture)
    {
        this.fixture = fixture;
        ConnectionTokens.HandlerOverride = () => new FakeProvider(provider);
    }

    public void Dispose() => ConnectionTokens.HandlerOverride = null;

    /// <summary>A token endpoint: hands out numbered tokens, and can rotate refresh tokens, honouring each once.</summary>
    sealed class Provider
    {
        int issued;
        public readonly ConcurrentQueue<Dictionary<string, string>> Requests = new();
        public readonly ConcurrentQueue<string> Authorizations = new();
        public bool Rotate;
        public int ExpiresIn = 3600;
        public bool GiveRefreshToken = true;
        public HttpStatusCode? Refuse;
        public string RefuseWith = "invalid_client";
        public TimeSpan Delay = TimeSpan.Zero;
        readonly ConcurrentDictionary<string, bool> spent = new();

        public int Issued => issued;

        public async Task<HttpResponseMessage> Answer(HttpRequestMessage request)
        {
            var form = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync()).ToDictionary(kv => kv.Key, kv => kv.Value.ToString());
            Requests.Enqueue(form);
            if (request.Headers.Authorization is { } auth) Authorizations.Enqueue(auth.ToString());
            if (Delay > TimeSpan.Zero) await Task.Delay(Delay);
            if (Refuse is { } status) return Json(status, new { error = RefuseWith, error_description = "refused here" });

            if (form["grant_type"] == "refresh_token" && Rotate && !spent.TryAdd(form["refresh_token"], true))
                return Json(HttpStatusCode.BadRequest, new { error = "invalid_grant", error_description = "refresh token already used" });

            var n = Interlocked.Increment(ref issued);
            var body = new Dictionary<string, object> { ["access_token"] = $"access-{n}", ["token_type"] = "Bearer", ["expires_in"] = ExpiresIn };
            if (GiveRefreshToken && (form["grant_type"] == "authorization_code" || Rotate)) body["refresh_token"] = $"refresh-{n}";
            return Json(HttpStatusCode.OK, body);
        }

        static HttpResponseMessage Json(HttpStatusCode status, object body) =>
            new(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
    }

    sealed class FakeProvider(Provider p) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => p.Answer(request);
    }

    /// <summary>A runtime that runs nothing, and remembers the settings it was started with.</summary>
    sealed class Recording : IAdapterRuntime
    {
        public IDictionary<string, string> Started;
        public Task<bool> CanRunAsync(string adapterId) => Task.FromResult(true);

        public Task<IAdapterSession> BeginAsync(string adapterId, AdapterRole role, IDictionary<string, string> properties, string correlationId)
        {
            Started = properties;
            return Task.FromResult<IAdapterSession>(new Session());
        }

        sealed class Session : IAdapterSession
        {
            public Task<TResult> InvokeAsync<TResult>(string method, object argument = null) => Task.FromResult(default(TResult));
            public Task InvokeAsync(string method, object argument = null) => Task.CompletedTask;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    static string UniqueName() => $"conn{Interlocked.Increment(ref _seq)}{Guid.NewGuid().ToString("N")[..6]}";

    async Task<T> As<T>(Func<IServiceProvider, Task<T>> act)
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext ??= new DefaultHttpContext();
        return await act(scope.ServiceProvider);
    }

    Task<ConnectionRow> Create(ConnectionSave save) =>
        As(async sp => (ConnectionRow)await ActivatorUtilities.CreateInstance<Resources.Connections.Create>(sp).Handle(save));

    Task<ConnectionRow> Update(int id, ConnectionSave save) =>
        As(async sp => (ConnectionRow)await ActivatorUtilities.CreateInstance<Resources.Connections.Update>(sp).Handle(id, save));

    static ConnectionSave ClientCredentials(string name = null) => new()
    {
        Name = name ?? UniqueName(), Grant = "ClientCredentials", TokenUrl = "https://id.test/token",
        ClientId = "bitween", ClientSecret = "s3cret", Scopes = "orders.write",
    };

    static ConnectionSave AuthorizationCode(string name = null) => new()
    {
        Name = name ?? UniqueName(), Grant = "AuthorizationCode", TokenUrl = "https://id.test/token",
        AuthorizeUrl = "https://id.test/authorize", ClientId = "bitween", ClientSecret = "s3cret",
        Scopes = "api refresh_token", AuthorizeParameters = "prompt=consent",
    };

    Task<string> Token(string name) => As(sp => sp.GetRequiredService<ConnectionTokens>().AccessTokenAsync(name));

    async Task<Connection> Stored(int id)
    {
        await using var scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<BitweenDbContext>().Set<Connection>().AsNoTracking().SingleAsync(c => c.Id == id);
    }

    async Task Expire(int id)
    {
        await using var scope = fixture.CreateScope();
        await scope.ServiceProvider.GetRequiredService<BitweenDbContext>().Set<Connection>().Where(c => c.Id == id)
            .ExecuteUpdateAsync(u => u.SetProperty(c => c.AccessTokenExpiresOn, DateTime.UtcNow.AddSeconds(30)));
    }

    [Fact]
    public async Task A_client_credentials_token_is_asked_for_once_and_kept_until_it_nearly_expires()
    {
        var c = await Create(ClientCredentials());

        Assert.Equal("access-1", await Token(c.Name));
        Assert.Equal("access-1", await Token(c.Name.ToUpperInvariant()));
        Assert.Single(provider.Requests);
        var form = provider.Requests.Single();
        Assert.Equal("client_credentials", form["grant_type"]);
        Assert.Equal("orders.write", form["scope"]);
        Assert.Equal("s3cret", form["client_secret"]);

        // Within two minutes of expiring it is renewed first, so it can't expire under the adapter.
        await Expire(c.Id);
        Assert.Equal("access-2", await Token(c.Name));
        Assert.Equal(ConnectionStatus.Connected, (await Stored(c.Id)).Status);
    }

    [Fact]
    public async Task Every_adapter_gets_the_token_in_its_settings_when_it_starts_and_others_are_untouched()
    {
        var c = await Create(ClientCredentials());
        var runtime = new Recording();
        var settings = new Dictionary<string, string>
        {
            ["Token"] = $"{{{{connections.{c.Name}.accessToken}}}}",
            ["Headers"] = $"Authorization: {{{{connections.{c.Name}.authorization}}}}",
            ["Other"] = "{{globals.set.key}} and {{partner.Code}}",
        };

        await As(async sp =>
        {
            var invoker = new AdapterInvoker([runtime], connections: sp.GetRequiredService<ConnectionTokens>());
            await using var session = await invoker.BeginAsync("SomeHandler", AdapterRole.Handler, settings);
            return true;
        });

        Assert.Equal("access-1", runtime.Started["Token"]);
        Assert.Equal("Authorization: Bearer access-1", runtime.Started["Headers"]);
        Assert.Equal("{{globals.set.key}} and {{partner.Code}}", runtime.Started["Other"]);
        // The caller's settings - what an exchange stores - still hold the reference, not the token.
        Assert.Equal($"{{{{connections.{c.Name}.accessToken}}}}", settings["Token"]);

        // Settings with no reference are passed on as they were, without asking the database.
        var plain = new Dictionary<string, string> { ["Url"] = "https://x.test" };
        await As(async sp =>
        {
            var invoker = new AdapterInvoker([runtime], connections: sp.GetRequiredService<ConnectionTokens>());
            await using var session = await invoker.BeginAsync("SomeHandler", AdapterRole.Handler, plain);
            return true;
        });
        Assert.Same(plain, runtime.Started);
    }

    /// <summary>The invoker every pipeline step gets from DI resolves references, not only one built by hand.</summary>
    [Fact]
    public async Task The_pipeline_s_own_invoker_resolves_references()
    {
        var ex = await Assert.ThrowsAsync<ConnectionException>(() => As(async sp =>
        {
            await using var session = await sp.GetRequiredService<IAdapterInvoker>().BeginAsync("NativeTestResponder", AdapterRole.Handler,
                new Dictionary<string, string> { ["T"] = "{{connections.missing-one.accessToken}}" });
            return true;
        }));
        Assert.Contains("missing-one", ex.Message);
    }

    [Fact]
    public async Task A_reference_to_a_connection_that_doesn_t_exist_fails_the_adapter_call_saying_so()
    {
        var ex = await Assert.ThrowsAsync<ConnectionException>(() => As(sp =>
            sp.GetRequiredService<ConnectionTokens>().ResolveAsync(new Dictionary<string, string> { ["T"] = "{{connections.nothere.accessToken}}" })));
        Assert.Contains("no connection named 'nothere'", ex.Message);

        var c = await Create(ClientCredentials());
        ex = await Assert.ThrowsAsync<ConnectionException>(() => As(sp =>
            sp.GetRequiredService<ConnectionTokens>().ResolveAsync(new Dictionary<string, string> { ["T"] = $"{{{{connections.{c.Name}.password}}}}" })));
        Assert.Contains(".accessToken", ex.Message);
    }

    [Fact]
    public async Task Approving_once_brings_a_refresh_token_that_keeps_the_connection_going()
    {
        var c = await Create(AuthorizationCode());
        Assert.Equal("NeedsAuthorizing", c.Status);
        var notYet = await Assert.ThrowsAsync<ConnectionException>(() => Token(c.Name));
        Assert.Contains("needs authorizing", notYet.Message);

        var (url, cookie) = await As(async sp =>
        {
            var accessor = sp.GetRequiredService<IHttpContextAccessor>();
            accessor.HttpContext = new DefaultHttpContext();
            accessor.HttpContext.Request.Scheme = "https";
            accessor.HttpContext.Request.Host = new HostString("bitween.test");
            var result = await ActivatorUtilities.CreateInstance<Resources.Connections.Authorize>(sp).Handle(c.Id, new ConnectionAuthorize());
            var setCookie = accessor.HttpContext.Response.Headers.SetCookie.ToString();
            return (result.Url, setCookie.Split(';')[0].Split('=', 2)[1]);
        });
        var query = QueryHelpers.ParseQuery(new Uri(url).Query).ToDictionary(kv => kv.Key, kv => kv.Value.ToString());
        Assert.StartsWith("https://id.test/authorize?", url);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("https://bitween.test/oauth-callback", query["redirect_uri"]);
        Assert.Equal("api refresh_token", query["scope"]);
        Assert.Equal("consent", query["prompt"]);
        Assert.Equal("S256", query["code_challenge_method"]);

        Task<object> Complete(string state, string withCookie) => As(async sp =>
        {
            var accessor = sp.GetRequiredService<IHttpContextAccessor>();
            accessor.HttpContext = new DefaultHttpContext();
            if (withCookie != null) accessor.HttpContext.Request.Headers.Cookie = $"{ConnectionTokens.FlowCookie}={withCookie}";
            return await ActivatorUtilities.CreateInstance<Resources.Connections.Complete>(sp)
                .Handle(new ConnectionComplete { Code = "the-code", State = state });
        });

        // A state that isn't this browser's, or no cookie, authorizes nothing.
        Assert.Equal("CONNECTION_STATE", (await Assert.ThrowsAsync<SWValidationException>(() => Complete("forged", cookie))).Validations.First().Key);
        Assert.Equal("CONNECTION_FLOW", (await Assert.ThrowsAsync<SWValidationException>(() => Complete(query["state"], null))).Validations.First().Key);
        Assert.Empty(provider.Requests);

        await Complete(query["state"], cookie);
        var form = provider.Requests.Single();
        Assert.Equal("authorization_code", form["grant_type"]);
        Assert.Equal("the-code", form["code"]);
        Assert.Equal(query["code_challenge"], SW.Bitween.Services.SealedState.Challenge(form["code_verifier"]));

        var stored = await Stored(c.Id);
        Assert.Equal(ConnectionStatus.Connected, stored.Status);
        Assert.Equal("refresh-1", stored.RefreshToken);
        Assert.NotNull(stored.AuthorizedOn);
        Assert.Equal("access-1", await Token(c.Name));

        // Renewed with the refresh token, without anyone approving again.
        await Expire(c.Id);
        Assert.Equal("access-2", await Token(c.Name));
        Assert.Equal("refresh_token", provider.Requests.Last()["grant_type"]);
        Assert.Equal("refresh-1", provider.Requests.Last()["refresh_token"]);
    }

    async Task<ConnectionRow> Authorized(bool rotate)
    {
        provider.Rotate = rotate;
        var c = await Create(AuthorizationCode());
        await using (var scope = fixture.CreateScope())
            await scope.ServiceProvider.GetRequiredService<BitweenDbContext>().Set<Connection>().Where(x => x.Id == c.Id)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.RefreshToken, "refresh-0").SetProperty(x => x.Status, ConnectionStatus.Connected));
        return c;
    }

    [Fact]
    public async Task A_rotated_refresh_token_is_kept_and_the_next_renewal_uses_it()
    {
        var c = await Authorized(rotate: true);
        Assert.Equal("access-1", await Token(c.Name));
        Assert.Equal("refresh-1", (await Stored(c.Id)).RefreshToken);

        await Expire(c.Id);
        Assert.Equal("access-2", await Token(c.Name));
        Assert.Equal("refresh-1", provider.Requests.Last()["refresh_token"]);
        Assert.Equal("refresh-2", (await Stored(c.Id)).RefreshToken);
    }

    [Fact]
    public async Task Many_callers_at_once_renew_once_and_share_the_token()
    {
        // A provider that rotates refresh tokens honours each once: a second renewal beside the first
        // would be refused, and the connection would need authorizing again.
        var c = await Authorized(rotate: true);
        provider.Delay = TimeSpan.FromMilliseconds(400);

        var tokens = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => Token(c.Name))));

        Assert.All(tokens, t => Assert.Equal("access-1", t));
        Assert.Single(provider.Requests);
        Assert.Equal(ConnectionStatus.Connected, (await Stored(c.Id)).Status);
    }

    [Fact]
    public async Task A_refresh_token_the_provider_no_longer_honours_needs_authorizing_again()
    {
        var c = await Authorized(rotate: false);
        provider.Refuse = HttpStatusCode.BadRequest;
        provider.RefuseWith = "invalid_grant";

        var ex = await Assert.ThrowsAsync<ConnectionException>(() => Token(c.Name));
        Assert.Contains("needs authorizing again", ex.Message);
        var stored = await Stored(c.Id);
        Assert.Equal(ConnectionStatus.NeedsAuthorizing, stored.Status);
        Assert.Null(stored.RefreshToken);
        Assert.Null(stored.RenewingUntil);
        Assert.Contains("invalid_grant", stored.LastError);
    }

    [Fact]
    public async Task A_provider_that_refuses_the_client_is_an_error_saying_why_and_the_lease_is_let_go()
    {
        var c = await Create(ClientCredentials());
        provider.Refuse = HttpStatusCode.Unauthorized;

        var ex = await Assert.ThrowsAsync<ConnectionException>(() => Token(c.Name));
        Assert.Contains("invalid_client, refused here", ex.Message);
        var stored = await Stored(c.Id);
        Assert.Equal(ConnectionStatus.Error, stored.Status);
        Assert.Null(stored.RenewingUntil);

        // Test shows the same, then a working provider puts it right.
        var tested = await As(sp => ActivatorUtilities.CreateInstance<Resources.Connections.Test>(sp).Handle(c.Id, new ConnectionTest()));
        Assert.Equal("Error", tested.Status);
        provider.Refuse = null;
        tested = await As(sp => ActivatorUtilities.CreateInstance<Resources.Connections.Test>(sp).Handle(c.Id, new ConnectionTest()));
        Assert.Equal("Connected", tested.Status);
    }

    [Fact]
    public async Task A_password_connection_sends_the_service_user_and_basic_sends_the_client_in_a_header()
    {
        var c = await Create(new ConnectionSave
        {
            Name = UniqueName(), Grant = "Password", TokenUrl = "https://id.test/token", ClientId = "app", ClientSecret = "s3cret",
            Username = "svc", Password = "pw", TokenAuth = "basic",
        });
        await Token(c.Name);

        var form = provider.Requests.Single();
        Assert.Equal("password", form["grant_type"]);
        Assert.Equal("svc", form["username"]);
        Assert.Equal("pw", form["password"]);
        Assert.False(form.ContainsKey("client_secret"));
        Assert.Equal("Basic " + Convert.ToBase64String("app:s3cret"u8.ToArray()), provider.Authorizations.Single());
    }

    [Fact]
    public async Task Secrets_are_masked_kept_when_unchanged_and_tokens_forgotten_when_the_client_changes()
    {
        var c = await Create(ClientCredentials());
        Assert.Equal("__private__", c.ClientSecret);
        Assert.DoesNotContain("s3cret", JsonSerializer.Serialize(c));
        await Token(c.Name);

        // The masked secret sent back as it came keeps the stored one, and the token with it.
        var same = ClientCredentials(c.Name);
        same.ClientSecret = "__private__";
        await Update(c.Id, same);
        var stored = await Stored(c.Id);
        Assert.Equal("s3cret", stored.ClientSecret);
        Assert.Equal("access-1", stored.AccessToken);

        // Another client id: the token was the old client's.
        var other = ClientCredentials(c.Name);
        other.ClientId = "someone-else";
        other.ClientSecret = "__private__";
        await Update(c.Id, other);
        stored = await Stored(c.Id);
        Assert.Null(stored.AccessToken);
        Assert.Equal("s3cret", stored.ClientSecret);
    }

    [Fact]
    public async Task What_can_t_work_is_refused_when_saved()
    {
        async Task<string> Refused(ConnectionSave save) =>
            (await Assert.ThrowsAsync<SWValidationException>(() => Create(save))).Validations.First().Value;

        var bad = ClientCredentials("has spaces");
        Assert.Contains("A name is letters", await Refused(bad));
        bad = ClientCredentials();
        bad.TokenUrl = "http://id.test/token";
        Assert.Contains("https", await Refused(bad));
        // Plain http only to this machine, for a provider played locally.
        var local = ClientCredentials();
        local.TokenUrl = "http://127.0.0.1:9000/token";
        Assert.Equal("New", (await Create(local)).Status);
        bad = AuthorizationCode();
        bad.AuthorizeUrl = null;
        Assert.Contains("authorize URL", await Refused(bad));
        bad = ClientCredentials();
        bad.Grant = "Implicit";
        Assert.Contains("Grant is", await Refused(bad));

        var taken = await Create(ClientCredentials());
        var again = ClientCredentials(taken.Name.ToUpperInvariant());
        Assert.Equal("CONNECTION_NAME_TAKEN", (await Assert.ThrowsAsync<SWValidationException>(() => Create(again))).Validations.First().Key);
    }

    [Fact]
    public async Task A_connection_in_use_is_neither_deleted_nor_renamed()
    {
        var c = await Create(ClientCredentials());
        int notifierId;
        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var notifier = new Notifier(UniqueName()) { HandlerId = "NativeHttpHandler" };
            notifier.SetDictionaries(new Dictionary<string, string> { ["ApiKey"] = $"{{{{connections.{c.Name}.accessToken}}}}" });
            db.Add(notifier);
            await db.SaveChangesAsync();
            notifierId = notifier.Id;
        }

        var delete = await Assert.ThrowsAsync<SWValidationException>(() =>
            As(sp => ActivatorUtilities.CreateInstance<Resources.Connections.Delete>(sp).Handle(c.Id)));
        Assert.Contains("notifier", delete.Validations.First().Value);
        var renamed = ClientCredentials(c.Name + "x");
        Assert.Contains("can't be renamed", (await Assert.ThrowsAsync<SWValidationException>(() => Update(c.Id, renamed))).Validations.First().Value);

        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            db.Remove(await db.Set<Notifier>().SingleAsync(n => n.Id == notifierId));
            await db.SaveChangesAsync();
        }
        await As(sp => ActivatorUtilities.CreateInstance<Resources.Connections.Delete>(sp).Handle(c.Id));
        await using (var scope = fixture.CreateScope())
            Assert.False(await scope.ServiceProvider.GetRequiredService<BitweenDbContext>().Set<Connection>().AnyAsync(x => x.Id == c.Id));
    }

    [Fact]
    public async Task A_connection_a_notification_channel_refers_to_is_neither_deleted_nor_renamed()
    {
        var c = await Create(ClientCredentials());
        var channel = new NotificationChannel
        {
            Name = UniqueName(),
            HandlerId = "NativeHttpHandler",
            HandlerProperties = new Dictionary<string, string> { ["Url"] = "https://ops.example/hook", ["ApiKey"] = $"{{{{connections.{c.Name}.authorization}}}}" },
        };
        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            db.Add(channel);
            await db.SaveChangesAsync();
        }
        try
        {
            var delete = await Assert.ThrowsAsync<SWValidationException>(() =>
                As(sp => ActivatorUtilities.CreateInstance<Resources.Connections.Delete>(sp).Handle(c.Id, new ConnectionDelete())));
            Assert.Contains($"notification channel '{channel.Name}'", delete.Validations.First().Value);
            var renamed = ClientCredentials(c.Name + "x");
            Assert.Contains("can't be renamed", (await Assert.ThrowsAsync<SWValidationException>(() => Update(c.Id, renamed))).Validations.First().Value);
        }
        finally
        {
            await using var scope = fixture.CreateScope();
            await scope.ServiceProvider.GetRequiredService<BitweenDbContext>().Set<NotificationChannel>()
                .Where(x => x.Id == channel.Id).ExecuteDeleteAsync();
        }
        await As(sp => ActivatorUtilities.CreateInstance<Resources.Connections.Delete>(sp).Handle(c.Id, new ConnectionDelete()));
    }

    [Fact]
    public async Task With_a_key_the_secret_and_the_tokens_are_encrypted_even_as_they_are_renewed()
    {
        SecretColumnCipher.Configure("integration test passphrase");
        try
        {
            var c = await Authorized(rotate: true);
            await Token(c.Name);

            await using var scope = fixture.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var raw = await db.Database.SqlQueryRaw<string>(
                    "SELECT concat_ws('|', client_secret, refresh_token, access_token) AS \"Value\" FROM infolink.\"Connections\" WHERE id = {0}", c.Id)
                .SingleAsync();
            Assert.DoesNotContain("s3cret", raw);
            Assert.DoesNotContain("refresh-1", raw);
            Assert.DoesNotContain("access-1", raw);
            Assert.StartsWith(SecretColumnCipher.Prefix, raw);

            var stored = await Stored(c.Id);
            Assert.Equal("refresh-1", stored.RefreshToken);
            Assert.Equal("access-1", stored.AccessToken);
        }
        finally
        {
            // Left encrypted, this row would be unreadable to the rest of the collection.
            await using var scope = fixture.CreateScope();
            await scope.ServiceProvider.GetRequiredService<BitweenDbContext>().Set<Connection>().ExecuteDeleteAsync();
            SecretColumnCipher.Configure(null);
        }
    }

    [Fact]
    public void An_exchange_s_settings_keep_the_reference_when_globals_and_partner_values_are_filled()
    {
        var filled = new Dictionary<string, string> { ["Token"] = "{{connections.crm.accessToken}}", ["Org"] = "{{partner.Org}}" }
            .Fill(new Partner("p") { AdapterProperties = new Dictionary<string, string> { ["Org"] = "acme" } }, []);
        Assert.Equal("{{connections.crm.accessToken}}", filled["Token"]);
        Assert.Equal("acme", filled["Org"]);
    }
}
