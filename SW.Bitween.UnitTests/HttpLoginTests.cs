using System;
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

    private class FakeHandler(HttpStatusCode status, string responseBody) : HttpMessageHandler
    {
        public string? SentBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            SentBody = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(responseBody) };
        }
    }
}
