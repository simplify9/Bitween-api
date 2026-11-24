using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.NativeAdapters;
using SW.PrimitiveTypes;

namespace SW.Bitween.UnitTests;

[TestClass]
public class HttpHandlerBehaviourTests
{
    [TestInitialize]
    public void ForgetTokens()
    {
        OAuthTokens.Clear();
        HttpLogin.Clear();
    }

    [DataTestMethod]
    [DataRow(HttpStatusCode.TooManyRequests)]
    [DataRow(HttpStatusCode.RequestTimeout)]
    public async Task Not_now_answers_fail_the_delivery_so_it_can_be_retried(HttpStatusCode status)
    {
        var server = new Scripted(_ =>
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent("slow down") };
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
            return response;
        });

        var ex = await Assert.ThrowsExceptionAsync<Exception>(() => Handler(server).Handle(new XchangeFile("{}")));
        StringAssert.Contains(ex.Message, "Retry-After: 30s");
    }

    [TestMethod]
    public async Task A_client_error_is_still_a_bad_response_not_a_failure()
    {
        var server = new Scripted(_ => new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("no") });
        var result = await Handler(server).Handle(new XchangeFile("{}"));
        Assert.IsTrue(result.BadData);
    }

    [TestMethod]
    public async Task Patch_is_sent_as_patch()
    {
        var server = new Scripted(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        await Handler(server, new() { ["Verb"] = "patch" }).Handle(new XchangeFile("{}"));
        Assert.AreEqual(HttpMethod.Patch, server.Requests[0].Method);
    }

    [TestMethod]
    public async Task A_header_value_may_contain_a_colon()
    {
        var server = new Scripted(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        await Handler(server, new() { ["Headers"] = "X-Callback:https://me.example/hook" }).Handle(new XchangeFile("{}"));
        CollectionAssert.AreEqual(new[] { "https://me.example/hook" },
            new List<string>(server.Requests[0].Headers.GetValues("X-Callback")));
    }

    [TestMethod]
    public async Task An_oauth_token_is_fetched_once_and_reused()
    {
        var tokenCalls = 0;
        var server = new Scripted(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/token")
            {
                tokenCalls++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                    { Content = new StringContent("{\"access_token\":\"tok\",\"expires_in\":3600}") };
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        });
        var settings = new Dictionary<string, string>
        {
            ["AuthType"] = "OAuth2", ["LoginUrl"] = "https://partner.test/token", ["ClientId"] = "c", ["ClientSecret"] = "s",
        };

        await Handler(server, settings).Handle(new XchangeFile("{}"));
        await Handler(server, settings).Handle(new XchangeFile("{}"));

        Assert.AreEqual(1, tokenCalls);
        Assert.AreEqual("Bearer tok", server.Requests[^1].Headers.Authorization?.ToString());
    }

    [TestMethod]
    public async Task A_failed_token_fetch_is_an_error_not_bearer_null()
    {
        var server = new Scripted(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("{}") });
        var settings = new Dictionary<string, string>
        {
            ["AuthType"] = "OAuth2", ["LoginUrl"] = "https://partner.test/token", ["ClientId"] = "c", ["ClientSecret"] = "bad",
        };

        var ex = await Assert.ThrowsExceptionAsync<SWException>(() => Handler(server, settings).Handle(new XchangeFile("{}")));
        StringAssert.Contains(ex.Message, "token endpoint answered 401");
        Assert.AreEqual(1, server.Requests.Count, "nothing was sent with a missing token");
    }

    private static readonly Dictionary<string, string> LoginSettings = new()
    {
        ["AuthType"] = "Login", ["LoginUrl"] = "https://partner.test/token", ["LoginUsername"] = "c", ["LoginPassword"] = "s",
        ["LoginTokenPath"] = "access_token",
        ["LoginBody"] = "{\"grant_type\":\"client_credentials\",\"client_id\":\"{{username}}\",\"client_secret\":\"{{password}}\"}",
    };

    [TestMethod]
    public async Task A_login_token_is_fetched_once_and_reused()
    {
        var logins = 0;
        var server = new Scripted(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/token")
                return new HttpResponseMessage(HttpStatusCode.OK)
                    { Content = new StringContent($"{{\"access_token\":\"tok{++logins}\",\"expires_in\":3600}}") };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        });

        await Handler(server, LoginSettings).Handle(new XchangeFile("{}"));
        await Handler(server, LoginSettings).Handle(new XchangeFile("{}"));

        Assert.AreEqual(1, logins);
        Assert.AreEqual("Bearer tok1", server.Requests[^1].Headers.Authorization?.ToString());
    }

    [TestMethod]
    public async Task A_reused_login_token_that_is_refused_signs_in_again_and_resends_once()
    {
        var logins = 0;
        var server = new Scripted(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/token")
                return new HttpResponseMessage(HttpStatusCode.OK)
                    { Content = new StringContent($"{{\"access_token\":\"tok{++logins}\",\"expires_in\":3600}}") };
            return request.Headers.Authorization?.Parameter == "tok1"
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("revoked") }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true}") };
        });

        await Handler(server, LoginSettings).Handle(new XchangeFile("{}")); // signs in, tok1 refused (fresh): bad response
        var result = await Handler(server, LoginSettings).Handle(new XchangeFile("{\"n\":2}"));

        Assert.IsFalse(result.BadData);
        Assert.AreEqual(2, logins);
        Assert.AreEqual("Bearer tok2", server.Requests[^1].Headers.Authorization?.ToString());
        Assert.AreEqual("{\"n\":2}", await server.Requests[^1].Content!.ReadAsStringAsync(), "the message itself is sent again");
    }

    [TestMethod]
    public async Task A_fresh_login_token_that_is_refused_is_the_partners_answer()
    {
        var logins = 0;
        var server = new Scripted(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/token")
                return new HttpResponseMessage(HttpStatusCode.OK)
                    { Content = new StringContent($"{{\"access_token\":\"tok{++logins}\",\"expires_in\":3600}}") };
            return new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("not for you") };
        });

        var result = await Handler(server, LoginSettings).Handle(new XchangeFile("{}"));

        Assert.IsTrue(result.BadData);
        Assert.AreEqual(1, logins);
        Assert.AreEqual(2, server.Requests.Count, "one sign-in, one send");
    }

    [TestMethod]
    public async Task A_login_token_can_go_in_a_header_of_its_own()
    {
        var server = new Scripted(request => request.RequestUri!.AbsolutePath == "/token"
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"access_token\":\"tok\"}") }
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        var settings = new Dictionary<string, string>(LoginSettings) { ["LoginTokenHeader"] = "X-Auth-Token: {{token}}" };

        await Handler(server, settings).Handle(new XchangeFile("{}"));

        var sent = server.Requests[^1];
        Assert.IsNull(sent.Headers.Authorization);
        CollectionAssert.AreEqual(new[] { "tok" }, sent.Headers.GetValues("X-Auth-Token").ToList());
    }

    private static NativeHttpHandler Handler(Scripted server, Dictionary<string, string>? extra = null)
    {
        var settings = new Dictionary<string, string> { ["Url"] = "https://partner.test/orders" };
        foreach (var kv in extra ?? new()) settings[kv.Key] = kv.Value;
        var handler = new NativeHttpHandler(new Proxy(new HttpClient(server)));
        handler.InitializeStartupValues(settings);
        return handler;
    }

    private sealed class Proxy(HttpClient client) : IDynamicHttpProxy
    {
        public HttpClient GetClient(string fullUrl) => client;
    }

    private sealed class Scripted(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(answer(request));
        }
    }
}
