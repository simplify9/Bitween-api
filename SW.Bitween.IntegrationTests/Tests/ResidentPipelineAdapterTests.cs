using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using SW.Bitween.Domain;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.Bitween.Services.Adapters;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// A RESIDENT adapter used as an ordinary pipeline adapter - a subscription's handler or mapper.
///
/// The two packaged lifecycles are published identically and differ only in their entry point, so
/// nothing about the pipeline's contract changes. What used to happen is that the invoke path did
/// not look at which one an adapter used and sent every packaged adapter down the classic route,
/// where a resident one waits out the command timeout for an answer that never comes.
///
/// These run the real thing: a resident adapter installed from cloud storage, started as a process,
/// and invoked through the same code path a classic adapter goes through.
/// </summary>
[Collection("Bitween")]
public class ResidentPipelineAdapterTests(BitweenFixture fixture)
{
    private const string ResidentHandlerId = "infolink6.handlers.residentsample";

    private async Task InstallAsync()
    {
        await using var scope = fixture.CreateScope();
        var cloudFiles = scope.ServiceProvider.GetRequiredService<ICloudFilesService>();

        await AdapterInstaller.InstallAsync(cloudFiles,
            "SW.Bitween.SampleResidentHandler", ResidentHandlerId,
            "SW.Bitween.SampleResidentHandler.dll",
            new Dictionary<string, string> { ["Protocol"] = "2", ["Lifecycle"] = "resident" });
    }

    /// <summary>
    /// The whole point: a resident adapter answers the ordinary handler contract, through the
    /// ordinary invoke path, with nothing about the call saying which lifecycle it is.
    /// </summary>
    [Fact]
    public async Task A_resident_adapter_answers_the_handler_contract()
    {
        await InstallAsync();

        await using var scope = fixture.CreateScope();
        var invoker = scope.ServiceProvider.GetRequiredService<IAdapterInvoker>();

        var result = await invoker.InvokeAsync<XchangeFile>(
            ResidentHandlerId, AdapterRole.Handler, nameof(IInfolinkHandler.Handle),
            new XchangeFile("""{"hello":"world"}""", "in.json"),
            new Dictionary<string, string> { ["Greeting"] = "from-a-resident" });

        Assert.NotNull(result);

        var body = JObject.Parse(result!.Data);
        Assert.Equal("from-a-resident", body.Value<string>("greeting"));
        Assert.Equal("world", body["echo"]?.Value<string>("hello"));
    }

    /// <summary>
    /// The distinction a single call cannot show: the SAME process serves more than one message.
    ///
    /// A classic adapter is a new process per invocation and would answer 1 every time, so a count
    /// above 1 is the proof that the instance was kept and reused - which is the only reason to
    /// make an adapter resident in the first place.
    /// </summary>
    [Fact]
    public async Task The_same_instance_serves_more_than_one_message()
    {
        await InstallAsync();

        await using var scope = fixture.CreateScope();
        var invoker = scope.ServiceProvider.GetRequiredService<IAdapterInvoker>();

        var counts = new List<int>();
        for (var i = 0; i < 3; i++)
        {
            var result = await invoker.InvokeAsync<XchangeFile>(
                ResidentHandlerId, AdapterRole.Handler, nameof(IInfolinkHandler.Handle),
                new XchangeFile($$"""{"n":{{i}}}""", "in.json"));

            counts.Add(JObject.Parse(result!.Data).Value<int>("handled"));
        }

        Assert.Contains(counts, c => c > 1);
        Assert.Equal(counts.OrderBy(c => c).ToList(), counts);
    }

    /// <summary>
    /// A session holds one instance across several calls, which is what a receiver depends on -
    /// Initialize, the listing and every GetFile have to reach the same place.
    /// </summary>
    [Fact]
    public async Task A_session_keeps_every_call_on_one_instance()
    {
        await InstallAsync();

        await using var scope = fixture.CreateScope();
        var invoker = scope.ServiceProvider.GetRequiredService<IAdapterInvoker>();

        await using var session = await invoker.BeginAsync(ResidentHandlerId, AdapterRole.Handler);

        var first = await session.InvokeAsync<XchangeFile>(
            nameof(IInfolinkHandler.Handle), new XchangeFile("""{"n":1}""", "a.json"));
        var second = await session.InvokeAsync<XchangeFile>(
            nameof(IInfolinkHandler.Handle), new XchangeFile("""{"n":2}""", "b.json"));

        var firstCount = JObject.Parse(first!.Data).Value<int>("handled");
        var secondCount = JObject.Parse(second!.Data).Value<int>("handled");

        Assert.Equal(firstCount + 1, secondCount);
    }

    /// <summary>
    /// And a classic adapter still goes the classic way. The routing has to keep the existing
    /// lifecycle working, or fixing one kind would have broken every adapter already deployed.
    /// </summary>
    [Fact]
    public async Task A_classic_adapter_still_runs_through_the_same_invoker()
    {
        await using var scope = fixture.CreateScope();
        var invoker = scope.ServiceProvider.GetRequiredService<IAdapterInvoker>();

        var result = await invoker.InvokeAsync<XchangeFile>(
            "sw.bitween.samplehandler", AdapterRole.Handler, nameof(IInfolinkHandler.Handle),
            new XchangeFile("hello", "in.txt"),
            new Dictionary<string, string> { ["ContentType"] = "text/plain" });

        Assert.NotNull(result);
        Assert.Equal("hello", result!.Data);
    }

    /// <summary>A native adapter goes through the same door too, resolved in-process.</summary>
    [Fact]
    public async Task A_native_adapter_runs_through_the_same_invoker()
    {
        await using var scope = fixture.CreateScope();
        var invoker = scope.ServiceProvider.GetRequiredService<IAdapterInvoker>();

        // Resolving it is the assertion: the native runtime claims the id and returns a session
        // rather than the classic one trying to find a package under that name.
        await using var session = await invoker.BeginAsync(
            "native.smtp", AdapterRole.Handler,
            new Dictionary<string, string> { ["Host"] = "localhost" });

        Assert.NotNull(session);
    }
}
