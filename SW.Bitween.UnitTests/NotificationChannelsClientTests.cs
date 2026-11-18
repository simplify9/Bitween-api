using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.Client;
using SW.Bitween.Model;

namespace SW.Bitween.UnitTests;

/// <summary>
/// A notification channel's own quiet window through the client: sent on create and update, read back on get.
/// What Bitween does with it (suppressing within the window) is NotificationChannelTests'.
/// </summary>
[TestClass]
public class NotificationChannelsClientTests
{
    sealed class Stub(Func<HttpRequestMessage, string?, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path, string? Body)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellation);
            if (request.RequestUri!.AbsolutePath.EndsWith("api/accounts/servicetoken"))
                return Json("""{"jwt":"session","expiresIn":3600}""");
            Calls.Add((request.Method, request.RequestUri.PathAndQuery, body));
            return answer(request, body);
        }
    }

    static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    static BitweenClient ClientFor(Stub stub) =>
        new(new HttpClient(stub) { BaseAddress = new Uri("https://bitween.example.com/") }, "bwt_test");

    [TestMethod]
    public async Task Create_sends_the_quiet_window_including_zero()
    {
        var stub = new Stub((_, _) => Json("7"));
        using var bitween = ClientFor(stub);

        var id = await bitween.NotificationChannels.CreateAsync(new NotificationChannelCreate
        {
            Name = "Results", HandlerId = "NativeHttpHandler", QuietMinutes = 0,
            HandlerProperties = new() { ["Url"] = "https://host.example/results" },
        });

        Assert.AreEqual(7, id);
        var call = stub.Calls.Single();
        Assert.AreEqual("/api/notificationchannels", call.Path);
        Assert.AreEqual(0, JsonDocument.Parse(call.Body!).RootElement.GetProperty("quietMinutes").GetInt32(), "0 is sent, not dropped as a default.");
    }

    [TestMethod]
    public async Task Update_can_set_and_clear_it()
    {
        var stub = new Stub((_, _) => new HttpResponseMessage(HttpStatusCode.NoContent));
        using var bitween = ClientFor(stub);

        await bitween.NotificationChannels.UpdateAsync(7, new NotificationChannelUpdate { Name = "Results", HandlerId = "NativeHttpHandler", QuietMinutes = 30 });
        await bitween.NotificationChannels.UpdateAsync(7, new NotificationChannelUpdate { Name = "Results", HandlerId = "NativeHttpHandler", QuietMinutes = null });

        Assert.AreEqual(30, JsonDocument.Parse(stub.Calls[0].Body!).RootElement.GetProperty("quietMinutes").GetInt32());
        Assert.AreEqual(JsonValueKind.Null, JsonDocument.Parse(stub.Calls[1].Body!).RootElement.GetProperty("quietMinutes").ValueKind,
            "Cleared: the channel follows the instance's window again.");
    }

    [TestMethod]
    public async Task Get_reads_it_back_and_a_channel_without_one_reads_null()
    {
        var stub = new Stub((request, _) => request.RequestUri!.AbsolutePath.EndsWith("/1")
            ? Json("""{"id":1,"name":"A","handlerId":"NativeHttpHandler","quietMinutes":0,"handlerProperties":{},"usedBy":[]}""")
            : Json("""{"id":2,"name":"B","handlerId":"NativeHttpHandler","handlerProperties":{},"usedBy":[]}"""));
        using var bitween = ClientFor(stub);

        Assert.AreEqual(0, (await bitween.NotificationChannels.GetAsync(1))!.QuietMinutes);
        Assert.IsNull((await bitween.NotificationChannels.GetAsync(2))!.QuietMinutes);
    }
}
