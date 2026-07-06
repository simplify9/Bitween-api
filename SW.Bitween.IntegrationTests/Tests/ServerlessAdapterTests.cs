using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.PrimitiveTypes;
using SW.Serverless;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

[Collection("Bitween")]
public class ServerlessAdapterTests
{
    private readonly BitweenFixture _fixture;

    public ServerlessAdapterTests(BitweenFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SampleHandler_echo_returns_input_unchanged()
    {
        await using var scope = _fixture.CreateScope();
        var serverless = scope.ServiceProvider.GetRequiredService<IServerlessService>();

        var correlationId = Guid.NewGuid().ToString();
        await serverless.StartAsync("sw.bitween.samplehandler", correlationId,
            new Dictionary<string, string> { ["ContentType"] = "text/plain" });

        var input = new XchangeFile("hello from integration test");
        var result = await serverless.InvokeAsync<XchangeFile>("Handle", input);

        Assert.NotNull(result);
        Assert.Equal(input.Data, result.Data);
    }

    [Fact]
    public async Task ConfigurableAdapter_with_output_data_overrides_response()
    {
        await using var scope = _fixture.CreateScope();
        var serverless = scope.ServiceProvider.GetRequiredService<IServerlessService>();

        var correlationId = Guid.NewGuid().ToString();
        await serverless.StartAsync("sw.bitween.sampleconfigurableadapter", correlationId,
            new Dictionary<string, string> { ["OutputData"] = "overridden output" });

        var result = await serverless.InvokeAsync<XchangeFile>("Handle", new XchangeFile("{}"));

        Assert.NotNull(result);
        Assert.Equal("overridden output", result.Data);
    }

    [Fact]
    public async Task ConfigurableAdapter_simulate_error_throws_on_invoke()
    {
        await using var scope = _fixture.CreateScope();
        var serverless = scope.ServiceProvider.GetRequiredService<IServerlessService>();

        var correlationId = Guid.NewGuid().ToString();
        await serverless.StartAsync("sw.bitween.sampleconfigurableadapter", correlationId,
            new Dictionary<string, string>
            {
                ["SimulateError"] = "true",
                ["ErrorMessage"] = "test failure from adapter"
            });

        await Assert.ThrowsAnyAsync<Exception>(() =>
            serverless.InvokeAsync<XchangeFile>("Handle", new XchangeFile("{}")));
    }

    [Fact]
    public async Task ConfigurableAdapter_delay_completes_within_tolerance()
    {
        await using var scope = _fixture.CreateScope();
        var serverless = scope.ServiceProvider.GetRequiredService<IServerlessService>();

        var correlationId = Guid.NewGuid().ToString();
        await serverless.StartAsync("sw.bitween.sampleconfigurableadapter", correlationId,
            new Dictionary<string, string> { ["DelayMs"] = "300" });

        var sw = Stopwatch.StartNew();
        var result = await serverless.InvokeAsync<XchangeFile>("Handle", new XchangeFile("{}"));
        sw.Stop();

        Assert.NotNull(result);
        Assert.True(sw.ElapsedMilliseconds >= 300,
            $"Expected at least 300ms delay, actual: {sw.ElapsedMilliseconds}ms");
    }
}
