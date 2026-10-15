using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.HttpTests;

/// <summary>
/// A sync gateway call waits for its result holding the exchange id, not the payload: the request
/// body and the trees it was parsed into are garbage once the exchange is created and published.
/// </summary>
/// <remarks>
/// The payload text is what the gateway writes to storage as the exchange's input, and the parsed
/// file holds that very string (its trees only answer for it), so once the string can be collected
/// the file and its trees can too.
/// </remarks>
[Collection("Http")]
public class GatewayPayloadReleaseTests(HttpFixture fixture)
{
    [Fact]
    public async Task A_sync_call_holds_no_payload_while_it_waits()
    {
        using var partner = new HeldPartner();
        var (urlName, key) = await GatewayToAsync(partner.Url);

        var written = new ConcurrentQueue<WeakReference>();
        await using var node = fixture.SecondNode(services => InputWritesOf(services, written));
        // Lines to make it a payload worth parsing, under the size that travels in the created message.
        var body = $"{{\"order\":7,\"lines\":[{string.Join(",", Enumerable.Range(0, 2000).Select(i => $"{{\"sku\":\"S{i}\",\"qty\":{i}}}"))}]}}";
        var call = Api.CallSyncAsync(HttpFixture.ClientOf(node), urlName, key, body);

        // The handler is running, so the exchange is created and published, and the call is waiting.
        await partner.Called.WaitAsync(TimeSpan.FromSeconds(20));
        var input = Assert.Single(written);
        var collected = false;
        for (var i = 0; i < 50 && !collected; i++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            collected = !input.IsAlive;
            if (!collected) await Task.Delay(100);
        }

        Assert.False(call.IsCompleted, "the call stopped waiting before the payload was looked for");
        Assert.True(collected, "the payload of a waiting sync call is still reachable");

        partner.Release();
        using var answer = await call;
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal(body, await answer.Content.ReadAsStringAsync());
    }

    /// <summary>A gateway and a partner key, its subscription posting to <paramref name="partnerUrl"/> and answering with what it gets back.</summary>
    private async Task<(string UrlName, string Key)> GatewayToAsync(string partnerUrl)
    {
        var (admin, urlName, _, partnerId, subscriptionId) = await Api.GatewayAsync(fixture);
        var key = await Api.NewKeyAsync(admin);
        await Api.SetKeysAsync(admin, partnerId, ("main", key));
        var subscription = (await Api.Json(await admin.GetAsync($"/api/subscriptions/{subscriptionId}"))).AsObject();
        subscription["handlerId"] = "NativeHttpHandler";
        subscription["handlerProperties"] = new System.Text.Json.Nodes.JsonArray(
            new System.Text.Json.Nodes.JsonObject { ["key"] = "Url", ["value"] = partnerUrl },
            new System.Text.Json.Nodes.JsonObject { ["key"] = "Verb", ["value"] = "post" },
            new System.Text.Json.Nodes.JsonObject { ["key"] = "ContentType", ["value"] = "application/json" });
        await Api.Json(await admin.PostAsJsonAsync($"/api/subscriptions/{subscriptionId}", subscription));
        return (urlName, key);
    }

    /// <summary>The node's storage, noting a weak reference to the text of every exchange input it writes.</summary>
    private static void InputWritesOf(IServiceCollection services, ConcurrentQueue<WeakReference> written)
    {
        var original = services.Last(d => d.ServiceType == typeof(ICloudFilesService));
        services.Add(new ServiceDescriptor(typeof(ICloudFilesService), provider =>
        {
            var inner = (ICloudFilesService)(original.ImplementationInstance
                ?? original.ImplementationFactory?.Invoke(provider)
                ?? ActivatorUtilities.CreateInstance(provider, original.ImplementationType!));
            var probe = DispatchProxy.Create<ICloudFilesService, WriteProbe>();
            ((WriteProbe)(object)probe).Watch(inner, written);
            return probe;
        }, original.Lifetime));
    }

    public class WriteProbe : DispatchProxy
    {
        private ICloudFilesService inner = null!;
        private ConcurrentQueue<WeakReference> written = null!;

        internal void Watch(ICloudFilesService files, ConcurrentQueue<WeakReference> inputs) =>
            (inner, written) = (files, inputs);

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == nameof(ICloudFilesService.WriteTextAsync) &&
                args is [string text, WriteFileSettings { Key: { } key }, ..] && key.EndsWith("/input"))
                written.Enqueue(new WeakReference(text));
            try
            {
                return method.Invoke(inner, args);
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(ex.InnerException);
                throw;
            }
        }
    }

    /// <summary>A partner on the loopback address that answers a post with its body once released.</summary>
    private sealed class HeldPartner : IDisposable
    {
        private readonly HttpListener listener = new();
        private readonly TaskCompletionSource called = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public HeldPartner()
        {
            var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            Url = $"http://127.0.0.1:{port}/orders";
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Start();
            _ = Task.Run(ServeAsync);
        }

        public string Url { get; }

        public Task Called => called.Task;

        public void Release() => released.TrySetResult();

        private async Task ServeAsync()
        {
            while (listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync();
                }
                catch
                {
                    return;
                }

                using var reader = new System.IO.StreamReader(context.Request.InputStream);
                var body = Encoding.UTF8.GetBytes(await reader.ReadToEndAsync());
                called.TrySetResult();
                await released.Task;
                context.Response.ContentType = "application/json";
                await context.Response.OutputStream.WriteAsync(body);
                context.Response.Close();
            }
        }

        public void Dispose()
        {
            released.TrySetResult();
            listener.Close();
        }
    }
}
