using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.HttpTests;

/// <summary>
/// A sync gateway call whose delivery went through while its output file could not be stored is
/// answered as the success it is, with the partner's response, and its exchange is listed as a
/// success with a warning and no output file.
/// </summary>
[Collection("Http")]
public class FileNotStoredTests(HttpFixture fixture)
{
    const string Rules = """
        { "version": 1, "sourceFormat": "json", "targetFormat": "json",
          "fields": [ { "target": ["reference"], "from": { "kind": "path", "path": "order.id" } } ] }
        """;

    [Fact]
    public async Task A_sync_call_whose_output_could_not_be_stored_is_answered_200_with_the_response()
    {
        // A second node whose storage refuses output files; the broker hands the lane's exchanges to
        // both nodes in turn, so some of them run there.
        await using var refusing = fixture.App.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => RefuseWritesOf(services, "/output")));
        using var partner = new SyncResultSignalTests.EchoPartner();
        var (admin, urlName, _, partnerId, subscriptionId) = await Api.GatewayAsync(fixture);
        var key = await Api.NewKeyAsync(admin);
        await Api.SetKeysAsync(admin, partnerId, ("main", key));
        var subscription = (await Api.Json(await admin.GetAsync($"/api/subscriptions/{subscriptionId}"))).AsObject();
        subscription["mapperId"] = "NativeMapper";
        subscription["mapperProperties"] = new JsonArray(new JsonObject { ["key"] = "MappingRules", ["value"] = Rules });
        subscription["handlerId"] = "NativeHttpHandler";
        subscription["handlerProperties"] = new JsonArray(
            new JsonObject { ["key"] = "Url", ["value"] = partner.Url },
            new JsonObject { ["key"] = "Verb", ["value"] = "post" },
            new JsonObject { ["key"] = "ContentType", ["value"] = "application/json" });
        await Api.Json(await admin.PostAsJsonAsync($"/api/subscriptions/{subscriptionId}", subscription));

        // Both nodes have served a call, so both consume the lane.
        await Api.CallSyncAsync(fixture, urlName, key, """{"order":{"id":"warm"}}""");
        for (var i = 0; i < 10; i++)
        {
            using var answer = await Api.CallSyncAsync(HttpFixture.ClientOf(refusing), urlName, key,
                "{\"order\":{\"id\":\"PO-" + i + "\"}}");
            Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
            // The partner echoes what it was sent: the mapped document, stored or not.
            Assert.Equal($"PO-{i}", (string?)JsonNode.Parse(await answer.Content.ReadAsStringAsync())?["reference"]);
        }

        var rows = (await Api.Json(await admin.GetAsync(
            $"/api/xchanges?filter={Uri.EscapeDataString($"SubscriptionId:1:{subscriptionId}")}&size=50")))["result"]!.AsArray();
        Assert.All(rows, row => Assert.True((bool?)row!["status"]));
        var notStored = rows.Where(row => row!["warning"] != null).ToList();
        Assert.NotEmpty(notStored);
        Assert.All(notStored, row =>
        {
            Assert.StartsWith("Delivered; the output file could not be stored: ", (string?)row!["warning"]);
            Assert.Null((string?)row["outputUrl"]);
            Assert.Null((string?)row["outputFileName"]);
            Assert.Equal(0, (int?)row["outputFileSize"]);
            Assert.NotNull((string?)row["responseUrl"]);
            Assert.Null(row["scheduledRetryOn"]);
        });
    }

    /// <summary>Wraps the app's storage so writes to keys ending in <paramref name="suffix"/> fail.</summary>
    static void RefuseWritesOf(IServiceCollection services, string suffix)
    {
        var inner = services.Last(d => d.ServiceType == typeof(ICloudFilesService));
        services.Remove(inner);
        services.Add(new ServiceDescriptor(typeof(ICloudFilesService), provider =>
        {
            var storage = (ICloudFilesService)(inner.ImplementationInstance
                ?? inner.ImplementationFactory?.Invoke(provider)
                ?? ActivatorUtilities.CreateInstance(provider, inner.ImplementationType!));
            return RefusingStorage.Wrapping(storage, suffix);
        }, inner.Lifetime));
    }

    public class RefusingStorage : DispatchProxy
    {
        ICloudFilesService inner = null!;
        string suffix = null!;

        public static ICloudFilesService Wrapping(ICloudFilesService inner, string suffix)
        {
            var proxy = Create<ICloudFilesService, RefusingStorage>();
            ((RefusingStorage)(object)proxy).inner = inner;
            ((RefusingStorage)(object)proxy).suffix = suffix;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            var key = args?.OfType<WriteFileSettings>().FirstOrDefault()?.Key;
            if (!method!.Name.StartsWith("Write") || key == null || !key.EndsWith(suffix))
                return method.Invoke(inner, args);

            var refused = new IOException("storage refused the request");
            if (!method.ReturnType.IsGenericType) return Task.FromException(refused);
            return typeof(Task).GetMethod(nameof(Task.FromException), 1, [typeof(Exception)])!
                .MakeGenericMethod(method.ReturnType.GetGenericArguments()[0]).Invoke(null, [refused]);
        }
    }
}
