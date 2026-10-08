using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.NativeAdapters;
using SW.PrimitiveTypes;

namespace SW.Bitween.UnitTests;

/// <summary>
/// The HTTP adapters share one client per origin across every subscription that calls it. These
/// pin that one subscription's credentials never ride on another's request, and that a message
/// cannot steer a request to another host.
/// </summary>
[TestClass]
public class HttpAdapterIsolationTests
{
    [TestMethod]
    public async Task A_subscription_without_auth_does_not_send_the_previous_subscriptions_token()
    {
        var capture = new CapturingHandler();
        var proxy = new SharedClientProxy(new HttpClient(capture));

        await Handler(proxy, new() { ["Url"] = "https://partner.test/orders", ["AuthType"] = "Bearer", ["LoginPassword"] = "token-of-a" })
            .Handle(new XchangeFile("{}"));
        await Handler(proxy, new() { ["Url"] = "https://partner.test/orders" })
            .Handle(new XchangeFile("{}"));

        Assert.AreEqual("Bearer token-of-a", capture.Requests[0].Headers.Authorization?.ToString());
        Assert.IsNull(capture.Requests[1].Headers.Authorization);
    }

    [TestMethod]
    public async Task An_api_key_is_sent_once_and_not_carried_to_the_next_call()
    {
        var capture = new CapturingHandler();
        var proxy = new SharedClientProxy(new HttpClient(capture));
        var settings = new Dictionary<string, string>
            { ["Url"] = "https://partner.test/orders", ["AuthType"] = "ApiKey", ["ApiKey"] = "key-of-a" };

        await Handler(proxy, settings).Handle(new XchangeFile("{}"));
        await Handler(proxy, settings).Handle(new XchangeFile("{}"));
        await Handler(proxy, new() { ["Url"] = "https://partner.test/orders" }).Handle(new XchangeFile("{}"));

        CollectionAssert.AreEqual(new[] { "key-of-a" }, new List<string>(capture.Requests[1].Headers.GetValues("ApiKey")));
        Assert.IsFalse(capture.Requests[2].Headers.Contains("ApiKey"));
    }

    [TestMethod]
    public void A_rendered_url_on_the_template_host_is_allowed() =>
        NativeHttpHandler.EnsureSameOrigin("https://partner.test/orders/{{id}}", new Uri("https://partner.test/orders/42"));

    [TestMethod]
    public void A_message_cannot_move_the_request_to_another_host()
    {
        Assert.ThrowsException<SWException>(() =>
            NativeHttpHandler.EnsureSameOrigin("https://partner.test{{path}}", new Uri("https://partner.test.attacker.example/x")));
        // A tenant placeholder fills one label; a value carrying its own host does not fit it.
        Assert.ThrowsException<SWException>(() =>
            NativeHttpHandler.EnsureSameOrigin("https://{{tenant}}.partner.test/orders", new Uri("https://attacker.example/x")));
        Assert.ThrowsException<SWException>(() =>
            NativeHttpHandler.EnsureSameOrigin("https://{{tenant}}.partner.test/orders", new Uri("https://a.b.partner.test/orders")));
        // Nor may a value add credentials or a port the template did not have.
        Assert.ThrowsException<SWException>(() =>
            NativeHttpHandler.EnsureSameOrigin("https://partner.test{{path}}", new Uri("https://partner.test:8443/x")));
    }

    [TestMethod]
    public void A_tenant_placeholder_still_reaches_any_tenant()
    {
        NativeHttpHandler.EnsureSameOrigin("https://{{tenant}}.partner.test/orders", new Uri("https://acme.partner.test/orders"));
        NativeHttpHandler.EnsureSameOrigin("https://partner.test{{path}}", new Uri("https://partner.test/orders/42"));
        NativeHttpHandler.EnsureSameOrigin("https://partner.test/{{id}}", new Uri("https://partner.test/42"));
    }

    [DataTestMethod]
    [DataRow("169.254.169.254", false, true)]
    [DataRow("fd00:ec2::254", false, true)]
    [DataRow("fe80::1", false, true)]
    [DataRow("10.1.2.3", false, false)]
    [DataRow("10.1.2.3", true, true)]
    [DataRow("192.168.1.10", true, true)]
    [DataRow("172.20.0.5", true, true)]
    [DataRow("127.0.0.1", true, true)]
    [DataRow("::ffff:169.254.169.254", false, true)]
    [DataRow("8.8.8.8", true, false)]
    [DataRow("2606:4700::1111", true, false)]
    public void Addresses_are_refused_as_configured(string address, bool blockPrivate, bool blocked) =>
        Assert.AreEqual(blocked, OutboundAddressGuard.IsBlocked(IPAddress.Parse(address), blockPrivate));

    private static NativeHttpHandler Handler(IDynamicHttpProxy proxy, Dictionary<string, string> settings)
    {
        var handler = new NativeHttpHandler(proxy);
        handler.InitializeStartupValues(settings);
        return handler;
    }

    private class SharedClientProxy(HttpClient client) : IDynamicHttpProxy
    {
        public HttpClient GetClient(string fullUrl) => client;
    }

    private class CapturingHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            // Copy what the client actually sent, default headers included, as the wire would see it.
            var sent = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers)
                sent.Headers.TryAddWithoutValidation(header.Key, header.Value);
            Requests.Add(sent);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        }
    }
}
