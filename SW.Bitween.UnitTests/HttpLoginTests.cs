using System;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using SW.Bitween.NativeAdapters;
using SW.PrimitiveTypes;

namespace SW.Bitween.UnitTests;

[TestClass]
public class HttpLoginTests
{
    [TestInitialize]
    public void ForgetTokens() => HttpLogin.Clear();

    // ─── Login body ─────────────────────────────────────────────────────────────

    [TestMethod]
    public void RenderBody_FillsUsernameAndPassword()
    {
        var body = HttpLogin.RenderBody("{\"email\":\"{{username}}\",\"password\":\"{{password}}\"}", "a@b.com", "secret");

        Assert.AreEqual("{\"email\":\"a@b.com\",\"password\":\"secret\"}", body);
    }

    [TestMethod]
    public void RenderBody_EscapesCredentialsSoTheBodyStaysValidJson()
    {
        var body = HttpLogin.RenderBody("{\"password\":\"{{password}}\"}", "u", "pa\"ss\\word");

        Assert.AreEqual("pa\"ss\\word", JObject.Parse(body)["password"]!.ToString());
    }

    [TestMethod]
    public async Task GetToken_SendsTheDefaultBodyWhenNoTemplateIsSet()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, "{\"jwt\":\"abc\"}");

        await HttpLogin.GetToken(new HttpClient(handler), "https://api.test/login", null, null, "u", "p",
            new UserLoginModel { Email = "u", Password = "p" });

        Assert.AreEqual("{\"Email\":\"u\",\"Password\":\"p\"}", handler.SentBody);
    }

    [TestMethod]
    public async Task GetToken_SendsTheTemplateWhenSet()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, "{\"jwt\":\"abc\"}");

        await HttpLogin.GetToken(new HttpClient(handler), "https://api.test/login", "{\"user\":\"{{username}}\"}",
            null, "u", "p", new UserLoginModel());

        Assert.AreEqual("{\"user\":\"u\"}", handler.SentBody);
    }

    // ─── Token path ─────────────────────────────────────────────────────────────

    [TestMethod]
    public void ReadToken_DefaultReadsJwtInAnyCase()
    {
        Assert.AreEqual("abc", HttpLogin.ReadToken("{\"Jwt\":\"abc\"}", null));
        Assert.AreEqual("abc", HttpLogin.ReadToken("{\"jwt\":\"abc\"}", " "));
    }

    [TestMethod]
    public void ReadToken_FollowsTheConfiguredPath()
    {
        Assert.AreEqual("abc", HttpLogin.ReadToken("{\"access_token\":\"abc\"}", "access_token"));
        Assert.AreEqual("abc", HttpLogin.ReadToken("{\"data\":{\"token\":\"abc\"}}", " data.token "));
    }

    [TestMethod]
    public void ReadToken_MissingTokenNamesThePath()
    {
        var ex = Assert.ThrowsException<SWException>(() => HttpLogin.ReadToken("{\"data\":{}}", "data.token"));

        StringAssert.Contains(ex.Message, "data.token");
    }

    [TestMethod]
    public void ReadToken_NonStringTokenIsRejected()
    {
        Assert.ThrowsException<SWException>(() => HttpLogin.ReadToken("{\"data\":{\"token\":{}}}", "data.token"));
    }

    [TestMethod]
    public void ReadToken_DefaultNonStringJwtIsRejected()
    {
        Assert.ThrowsException<SWException>(() => HttpLogin.ReadToken("{\"jwt\":123}", null));
        Assert.ThrowsException<SWException>(() => HttpLogin.ReadToken("{\"jwt\":{\"value\":\"abc\"}}", null));
    }

    [TestMethod]
    public void ReadToken_DefaultWithNoJwtSaysSo()
    {
        var ex = Assert.ThrowsException<SWException>(() => HttpLogin.ReadToken("{\"token\":\"abc\"}", null));

        StringAssert.Contains(ex.Message, "LoginTokenPath");
    }

    [TestMethod]
    public void ReadToken_NonJsonResponseSaysSo()
    {
        var ex = Assert.ThrowsException<SWException>(() => HttpLogin.ReadToken("<html>oops</html>", null));

        StringAssert.Contains(ex.Message, "not JSON");
    }

    // ─── Failures ───────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetToken_FailedLoginIncludesStatusAndBody()
    {
        var handler = new FakeHandler(HttpStatusCode.Unauthorized, "bad password");

        var ex = await Assert.ThrowsExceptionAsync<SWException>(() => HttpLogin.GetToken(new HttpClient(handler),
            "https://api.test/login", null, null, "u", "p", new UserLoginModel()));

        StringAssert.Contains(ex.Message, "401");
        StringAssert.Contains(ex.Message, "bad password");
    }

    // ─── Reuse ──────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetToken_ReusesTheTokenUntilItExpires()
    {
        var server = new CountingLogin("{\"access_token\":\"tok\",\"expires_in\":3600}");
        var client = new HttpClient(server);

        var options = new LoginOptions("https://api.test/token", new object())
        {
            Body = Body, TokenPath = "access_token", Username = "id", Password = "secret"
        };
        var first = await HttpLogin.GetTokenReused(client, options);
        var second = await HttpLogin.GetTokenReused(client, options);

        Assert.AreEqual(1, server.Logins);
        Assert.AreEqual(("tok", false), first);
        Assert.AreEqual(("tok", true), second);
    }

    [TestMethod]
    public async Task GetToken_DifferentCredentialsDoNotShareAToken()
    {
        var server = new CountingLogin("{\"access_token\":\"tok\",\"expires_in\":3600}");
        var client = new HttpClient(server);

        await HttpLogin.GetToken(client, "https://api.test/token", Body, "access_token", "id-a", "secret", new object());
        await HttpLogin.GetToken(client, "https://api.test/token", Body, "access_token", "id-b", "secret", new object());

        Assert.AreEqual(2, server.Logins);
    }

    [TestMethod]
    public async Task GetToken_MessagesArrivingTogetherShareOneSignIn()
    {
        var server = new CountingLogin("{\"access_token\":\"tok\",\"expires_in\":3600}", TimeSpan.FromMilliseconds(100));
        var client = new HttpClient(server);

        var tokens = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ =>
            HttpLogin.GetToken(client, "https://api.test/token", Body, "access_token", "id", "secret", new object())));

        Assert.AreEqual(1, server.Logins);
        Assert.IsTrue(tokens.All(t => t == "tok"));
    }

    [TestMethod]
    public async Task GetToken_AForgottenTokenSignsInAgain()
    {
        var server = new CountingLogin("{\"access_token\":\"tok\",\"expires_in\":3600}");
        var client = new HttpClient(server);

        var token = await HttpLogin.GetToken(client, "https://api.test/token", Body, "access_token", "id", "secret", new object());
        HttpLogin.Forget(token);
        await HttpLogin.GetToken(client, "https://api.test/token", Body, "access_token", "id", "secret", new object());

        Assert.AreEqual(2, server.Logins);
    }

    [TestMethod]
    public async Task GetToken_AnExpiredTokenIsNotKept()
    {
        var server = new CountingLogin("{\"access_token\":\"tok\",\"expires_in\":0}");
        var client = new HttpClient(server);

        await HttpLogin.GetToken(client, "https://api.test/token", Body, "access_token", "id", "secret", new object());
        await HttpLogin.GetToken(client, "https://api.test/token", Body, "access_token", "id", "secret", new object());

        Assert.AreEqual(2, server.Logins);
    }

    [TestMethod]
    public async Task GetToken_AFailedSignInIsNotKept()
    {
        var handler = new FakeHandler(HttpStatusCode.Unauthorized, "no");
        var client = new HttpClient(handler);

        await Assert.ThrowsExceptionAsync<SWException>(() =>
            HttpLogin.GetToken(client, "https://api.test/token", Body, "access_token", "id", "secret", new object()));
        await Assert.ThrowsExceptionAsync<SWException>(() =>
            HttpLogin.GetToken(client, "https://api.test/token", Body, "access_token", "id", "secret", new object()));
    }

    // ─── Lifetime ───────────────────────────────────────────────────────────────

    [TestMethod]
    public void Lifetime_ReadsExpiresInAsNumberOrString()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.AreEqual(TimeSpan.FromHours(1), HttpLogin.Lifetime("{\"expires_in\":3600}", "tok", now));
        Assert.AreEqual(TimeSpan.FromHours(1), HttpLogin.Lifetime("{\"expires_in\":\"3600\"}", "tok", now));
    }

    [TestMethod]
    public void Lifetime_FallsBackToTheJwtExpiry()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"exp\":1800000600}")).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        Assert.AreEqual(TimeSpan.FromMinutes(10), HttpLogin.Lifetime("{\"jwt\":\"x\"}", $"e30.{payload}.sig", now));
    }

    [TestMethod]
    public void Lifetime_DefaultsToFiveMinutes()
    {
        Assert.AreEqual(TimeSpan.FromMinutes(5), HttpLogin.Lifetime("{\"jwt\":\"opaque\"}", "opaque", DateTimeOffset.UtcNow));
        Assert.AreEqual(TimeSpan.FromMinutes(5), HttpLogin.Lifetime("{}", "a.b.c", DateTimeOffset.UtcNow));
    }

    // ─── Settings ───────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetToken_CanSendTheLoginFormEncoded()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, "{\"access_token\":\"tok\"}");

        await HttpLogin.GetTokenReused(new HttpClient(handler), new LoginOptions("https://api.test/token", new object())
        {
            Body = "{\"grant_type\":\"client_credentials\",\"scope\":\"invoices\",\"client_id\":\"{{username}}\",\"client_secret\":\"{{password}}\"}",
            ContentType = "application/x-www-form-urlencoded",
            TokenPath = "access_token", Username = "id", Password = "s&cret"
        });

        Assert.AreEqual("grant_type=client_credentials&scope=invoices&client_id=id&client_secret=s%26cret", handler.SentBody);
        Assert.AreEqual("application/x-www-form-urlencoded", handler.SentContentType);
    }

    [TestMethod]
    public async Task GetToken_AFormLoginNeedsAFlatJsonBody()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, "{\"access_token\":\"tok\"}");

        var ex = await Assert.ThrowsExceptionAsync<SWException>(() => HttpLogin.GetTokenReused(new HttpClient(handler),
            new LoginOptions("https://api.test/token", new object())
            {
                Body = "grant_type=client_credentials", ContentType = "application/x-www-form-urlencoded", TokenPath = "access_token"
            }));

        StringAssert.Contains(ex.Message, "flat JSON object");
    }

    [TestMethod]
    public async Task GetToken_ALifetimeOfZeroSignsInForEveryMessage()
    {
        var server = new CountingLogin("{\"access_token\":\"tok\",\"expires_in\":3600}");
        var client = new HttpClient(server);
        var options = new LoginOptions("https://api.test/token", new object())
        {
            Body = Body, TokenPath = "access_token", Username = "id", Password = "secret", LifetimeSeconds = 0
        };

        var first = await HttpLogin.GetTokenReused(client, options);
        var second = await HttpLogin.GetTokenReused(client, options);

        Assert.AreEqual(2, server.Logins);
        Assert.IsFalse(second.FromCache);
        Assert.IsFalse(first.FromCache);
    }

    [TestMethod]
    public void Lifetime_FollowsTheConfiguredPath()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.AreEqual(TimeSpan.FromMinutes(20), HttpLogin.Lifetime("{\"data\":{\"ttl\":1200}}", "tok", now, "data.ttl"));
    }

    [TestMethod]
    public void Lifetime_ReadsAnExpiryDate()
    {
        var now = new DateTimeOffset(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);
        Assert.AreEqual(TimeSpan.FromMinutes(30),
            HttpLogin.Lifetime("{\"expiresAt\":\"2026-11-19T10:30:00Z\"}", "tok", now, "expiresAt"));
    }

    [TestMethod]
    public void Lifetime_UsesTheConfiguredFallback()
    {
        Assert.AreEqual(TimeSpan.FromMinutes(20), HttpLogin.Lifetime("{}", "opaque", DateTimeOffset.UtcNow, null, 1200));
    }

    [TestMethod]
    public void Header_DefaultsToBearer()
    {
        Assert.AreEqual(("Authorization", "Bearer tok"), HttpLogin.Header(null, "tok"));
        Assert.AreEqual(("Authorization", "Bearer tok"), HttpLogin.Header(" ", "tok"));
    }

    [TestMethod]
    public void Header_FollowsTheTemplate()
    {
        Assert.AreEqual(("X-Auth-Token", "tok"), HttpLogin.Header("X-Auth-Token: {{token}}", "tok"));
        Assert.AreEqual(("Authorization", "Token tok"), HttpLogin.Header("Authorization: Token {{token}}", "tok"));
    }

    [TestMethod]
    public void Header_ATemplateWithoutTheTokenSaysHowToWriteIt()
    {
        var ex = Assert.ThrowsException<SWException>(() => HttpLogin.Header("X-Auth-Token", "tok"));
        StringAssert.Contains(ex.Message, "{{token}}");
        Assert.ThrowsException<SWException>(() => HttpLogin.Header("X-Auth-Token: tok", "tok"));
    }

    private const string Body = "{\"grant_type\":\"client_credentials\",\"client_id\":\"{{username}}\",\"client_secret\":\"{{password}}\"}";

    private class CountingLogin(string reply, TimeSpan? delay = null) : HttpMessageHandler
    {
        private int logins;
        public int Logins => logins;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref logins);
            if (delay is { } wait) await Task.Delay(wait, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(reply) };
        }
    }

    private class FakeHandler(HttpStatusCode status, string responseBody) : HttpMessageHandler
    {
        public string SentBody { get; private set; }
        public string SentContentType { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            SentBody = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            SentContentType = request.Content?.Headers.ContentType?.MediaType;
            return new HttpResponseMessage(status) { Content = new StringContent(responseBody) };
        }
    }
}
