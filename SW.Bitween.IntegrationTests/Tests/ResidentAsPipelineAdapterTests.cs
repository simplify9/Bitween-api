using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// A resident adapter used where a CLASSIC one is expected - as a subscription's handler, mapper,
/// receiver or validator.
///
/// The two lifecycles are installed identically: same zip, same cloud key, same metadata bag. What
/// differs is the entry point - Runner.Run speaks the classic stdin/stdout protocol, while
/// Runner.RunResident dials back over a socket and waits for the resident host. Nothing in the
/// adapter catalog or the invoke path looks at which one an adapter uses, so the question these
/// tests answer is what an operator actually gets when they pick one from the dropdown.
/// </summary>
[Collection("Bitween")]
public class ResidentAsPipelineAdapterTests(BitweenFixture fixture)
{
    // The catalog lists by key prefix - infolink6.{handlers|mappers|receivers|validators} - so an
    // adapter has to be installed under one of those to be selectable at all.
    private const string ResidentHandlerId = "infolink6.handlers.residenttest";
    private const string ClassicHandlerId = "infolink6.handlers.classictest";

    private async Task InstallAsync()
    {
        await using var scope = fixture.CreateScope();
        var cloudFiles = scope.ServiceProvider.GetRequiredService<ICloudFilesService>();

        // The same sample twice, under two names. The only difference the host could possibly see
        // is the metadata, which is the point.
        await AdapterInstaller.InstallAsync(cloudFiles,
            "SW.Bitween.SampleHandler", ClassicHandlerId, "SW.Bitween.SampleHandler.dll");

        await AdapterInstaller.InstallAsync(cloudFiles,
            "SW.Bitween.Adapters.Bus.RabbitMq", ResidentHandlerId,
            "SW.Bitween.Adapters.Bus.RabbitMq.dll",
            new Dictionary<string, string> { ["Protocol"] = "2", ["Lifecycle"] = "resident" });
    }

    /// <summary>
    /// Both show up. The catalog groups cloud keys by name and never reads the metadata, so
    /// residency makes no difference to whether an operator can pick it.
    /// </summary>
    [Fact]
    public async Task A_resident_adapter_appears_in_the_handler_dropdown()
    {
        await InstallAsync();

        var listed = await ListAsync("handlers");

        Assert.Contains(ClassicHandlerId, listed);
        Assert.Contains(ResidentHandlerId, listed);
    }

    /// <summary>
    /// And here is the problem. The dropdown offers it, and asking for its startup values - which
    /// is how the UI builds the properties form - goes down the classic path: spawn the process,
    /// talk stdin/stdout, wait. A resident adapter is not listening on stdin; it dialled out and is
    /// waiting for a host that, on this path, never calls.
    ///
    /// The UI swallows this and shows an adapter with no properties, so what an operator sees is a
    /// selectable adapter that simply has nothing to configure - not a broken one.
    /// </summary>
    [Fact]
    public async Task Asking_a_resident_adapter_for_its_startup_values_does_not_work()
    {
        await InstallAsync();

        // The classic one answers.
        var classic = await StartupValuesAsync(ClassicHandlerId);
        Assert.NotNull(classic);

        // The resident one does not - it either fails outright or hands back nothing, and either
        // way the properties form is empty.
        var resident = await Record.ExceptionAsync(() => StartupValuesAsync(ResidentHandlerId));

        if (resident == null)
        {
            var values = await StartupValuesAsync(ResidentHandlerId);
            Assert.True(values is not { Count: > 0 },
                "a resident adapter answered the classic startup-values handshake, which would mean "
                + "the two lifecycles are interchangeable after all");
        }
    }

    /// <summary>
    /// An adapter named nothing like the convention still appears, because it SAID what it is.
    ///
    /// This is the point of stamping the kind at publish time: the id stops having to carry the
    /// classification, so an adapter can be reclassified without being renamed - and a rename is
    /// not free, because every subscription stores the id it was configured with.
    /// </summary>
    [Fact]
    public async Task An_adapter_that_declares_its_kind_appears_whatever_it_is_called()
    {
        const string oddlyNamedId = "acme.orders.processor";

        await using (var scope = fixture.CreateScope())
        {
            var cloudFiles = scope.ServiceProvider.GetRequiredService<ICloudFilesService>();
            await AdapterInstaller.InstallAsync(cloudFiles,
                "SW.Bitween.SampleHandler", oddlyNamedId, "SW.Bitween.SampleHandler.dll",
                new Dictionary<string, string> { ["Kind"] = "handler" });
        }

        var handlers = await ListAsync("handlers");
        Assert.Contains(oddlyNamedId, handlers);

        // And it is not offered as something it never claimed to be.
        var validators = await ListAsync("validators");
        Assert.DoesNotContain(oddlyNamedId, validators);
    }

    /// <summary>
    /// The old convention still works, or the catalog would empty itself on any deployment whose
    /// adapters have not been republished since the stamp existed.
    /// </summary>
    [Fact]
    public async Task An_adapter_with_no_declared_kind_is_still_found_by_its_name()
    {
        await InstallAsync();

        var listed = await ListAsync("handlers");

        // Installed with no Kind at all - found purely by the infolink6.handlers. prefix.
        Assert.Contains(ClassicHandlerId, listed);
    }

    // ---------------------------------------------------------------- helpers

    private async Task<List<string>> ListAsync(string prefix)
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();

        var handler = ActivatorUtilities.CreateInstance<Resources.Adapters.SearchVersioned>(scope.ServiceProvider);
        var result = await handler.Handle(new AdapterSearchRequest { Prefix = prefix });

        // The handler returns an anonymous-typed sequence; go through JSON rather than reflect.
        var json = JsonConvert.SerializeObject(result);
        var rows = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(json) ?? new();

        return rows
            .Select(r => r.TryGetValue("Key", out var k) ? k?.ToString() : null)
            .Where(k => !string.IsNullOrEmpty(k))
            .Select(k => k!)
            .ToList();
    }

    private async Task<IDictionary<string, string>?> StartupValuesAsync(string adapterId)
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();

        var handler = ActivatorUtilities.CreateInstance<Resources.Adapters.GetProperties>(scope.ServiceProvider);
        var result = await handler.Handle(adapterId);
        return result as IDictionary<string, string>;
    }
}
