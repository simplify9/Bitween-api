using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.NativeAdapters;
using SW.Bitween.NativeAdapters.HttpReceiver;
using SW.PrimitiveTypes;

namespace SW.Bitween.UnitTests;

/// <summary>The HTTP receiver reading an API that pages its results.</summary>
[TestClass]
public class HttpReceiverPagingTests
{
    [TestMethod]
    public async Task Without_paging_settings_it_makes_one_request_as_before()
    {
        var server = new Scripted(_ => Json("""{"items":[1,2],"next":"/orders?page=2"}"""));
        var files = await Receiver(server, new() { ["ArrayPath"] = "items" }).ListFiles();
        Assert.AreEqual(2, files.Count());
        Assert.AreEqual(1, server.Requests.Count);
    }

    [TestMethod]
    public async Task Next_links_are_followed_until_there_is_none()
    {
        var server = new Scripted(request => request.RequestUri!.Query switch
        {
            "" => Json("""{"items":[1,2],"next":"/orders?cursor=b"}"""),
            "?cursor=b" => Json("""{"items":[3],"next":"https://partner.test/orders?cursor=c"}"""),
            _ => Json("""{"items":[4],"next":null}"""),
        });
        var receiver = Receiver(server, new() { ["ArrayPath"] = "items", ["NextLinkPath"] = "next" });

        var files = (await receiver.ListFiles()).ToList();

        Assert.AreEqual(3, server.Requests.Count);
        CollectionAssert.AreEqual(new[] { "1", "2", "3", "4" },
            (await Task.WhenAll(files.Select(receiver.GetFile))).Select(f => f.Data).ToArray());
    }

    [TestMethod]
    public async Task Numbered_pages_are_asked_for_until_one_is_empty()
    {
        var server = new Scripted(request => request.RequestUri!.Query switch
        {
            "?page=1" => Json("[1,2]"),
            "?page=2" => Json("[3]"),
            _ => Json("[]"),
        });
        var files = await Receiver(server, new() { ["PageParameter"] = "page" }).ListFiles();
        Assert.AreEqual(3, files.Count());
        Assert.AreEqual(3, server.Requests.Count);
    }

    [TestMethod]
    public async Task A_run_reads_at_most_max_pages()
    {
        var server = new Scripted(_ => Json("[1]"));
        var files = await Receiver(server, new() { ["PageParameter"] = "page", ["PageStart"] = "0", ["MaxPages"] = "3" }).ListFiles();
        Assert.AreEqual(3, files.Count());
        Assert.AreEqual("?page=0", server.Requests[0].RequestUri!.Query);
    }

    [TestMethod]
    public async Task A_next_link_to_another_server_is_not_followed()
    {
        var server = new Scripted(_ => Json("""{"items":[1],"next":"https://elsewhere.test/steal"}"""));
        var receiver = Receiver(server, new() { ["ArrayPath"] = "items", ["NextLinkPath"] = "next", ["AuthType"] = "Bearer", ["LoginPassword"] = "secret" });

        await Assert.ThrowsExceptionAsync<SWException>(() => receiver.ListFiles());
        Assert.AreEqual(1, server.Requests.Count);
    }

    static NativeHttpReceiver Receiver(Scripted server, Dictionary<string, string> extra)
    {
        var settings = new Dictionary<string, string> { ["Url"] = "https://partner.test/orders" };
        foreach (var kv in extra) settings[kv.Key] = kv.Value;
        var receiver = new NativeHttpReceiver(new Proxy(new HttpClient(server)));
        receiver.InitializeStartupValues(settings);
        return receiver;
    }

    static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    sealed class Proxy(HttpClient client) : IDynamicHttpProxy
    {
        public HttpClient GetClient(string fullUrl) => client;
    }

    sealed class Scripted(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(answer(request));
        }
    }
}
