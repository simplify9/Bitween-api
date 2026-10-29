using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SW.PrimitiveTypes;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.Bitween.Services.DataSources;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// What a data source form is built from.
///
/// The front end used to carry this itself: which fields a RabbitMQ connection starts with, what
/// DeclareMode accepts, which properties are credentials. A hand-kept copy of someone else's
/// contract drifts, and it did - the form advertised a "Tls" setting the adapter never read, so an
/// operator could set it, see nothing wrong, and still be authenticating in the clear.
///
/// These tests are about that failure mode. They assert the description comes from the adapter
/// package rather than from anything Bitween wrote down, which is the only property that stops the
/// two disagreeing again.
/// </summary>
[Collection("Bitween")]
public class DataSourceProviderCatalogTests(BitweenFixture fixture)
{
    /// <summary>
    /// The adapters the fixture installed describe themselves. Nothing in Bitween names these
    /// settings - delete the attributes from RabbitOptions and this list empties.
    /// </summary>
    [Fact]
    public async Task A_provider_describes_its_own_settings()
    {
        var rabbit = await DescribeAsync(BusAdapters.RabbitMq);

        Assert.NotNull(rabbit);
        Assert.Equal("RabbitMQ", rabbit.Label);
        Assert.Equal("Broker", rabbit.Kind);

        var names = rabbit.Settings.Select(s => s.Name).ToList();
        Assert.Contains("Host", names);
        Assert.Contains("UseSsl", names);
        Assert.Contains("QueueType", names);
    }

    /// <summary>
    /// The point of the whole exercise: a value the UI could only have guessed at comes from the
    /// adapter, so the menu it renders cannot offer a queue type RabbitMQ would reject.
    /// </summary>
    [Fact]
    public async Task Allowed_values_come_from_the_adapter()
    {
        var rabbit = await DescribeAsync(BusAdapters.RabbitMq);

        var queueType = rabbit.Settings.Single(s => s.Name == "QueueType");
        Assert.Equal(["classic", "quorum", "stream"], queueType.AllowedValues);

        var declareMode = rabbit.Settings.Single(s => s.Name == "DeclareMode");
        Assert.Equal(["none", "assert", "create"], declareMode.AllowedValues);
        Assert.Equal("assert", declareMode.Default);
    }

    /// <summary>
    /// The setting whose absence started this. It must be named UseSsl - what the adapter binds -
    /// and not the "Tls" the old hand-written hint told operators to add.
    /// </summary>
    [Fact]
    public async Task Tls_is_described_by_the_name_the_adapter_actually_binds()
    {
        var rabbit = await DescribeAsync(BusAdapters.RabbitMq);

        Assert.Contains(rabbit.Settings, s => s.Name == "UseSsl");
        Assert.DoesNotContain(rabbit.Settings, s => s.Name == "Tls");
    }

    /// <summary>
    /// A credential is declared as one by the adapter, so the form masks it before anything has
    /// been saved - without the name having to match a heuristic.
    /// </summary>
    [Fact]
    public async Task A_credential_is_declared_secret_by_the_adapter()
    {
        var rabbit = await DescribeAsync(BusAdapters.RabbitMq);
        var sqs = await DescribeAsync(BusAdapters.Sqs);

        Assert.True(rabbit.Settings.Single(s => s.Name == "Password").Secret);
        Assert.True(sqs.Settings.Single(s => s.Name == "SecretAccessKey").Secret);
        Assert.False(rabbit.Settings.Single(s => s.Name == "Host").Secret);
    }

    /// <summary>
    /// Endpoints come from the gateways bound to the data source and Consume is how a connection
    /// test avoids draining a live queue. Neither is an operator's to set, so neither is offered.
    /// </summary>
    [Fact]
    public async Task Host_supplied_settings_are_not_offered_as_fields()
    {
        var rabbit = await DescribeAsync(BusAdapters.RabbitMq);

        Assert.DoesNotContain(rabbit.Settings, s => s.Name == "Endpoints");
        Assert.DoesNotContain(rabbit.Settings, s => s.Name == "Consume");
    }

    /// <summary>
    /// A number is described as one. Coarse on purpose - it decides which input to render, and
    /// anything finer would be the UI knowing about brokers again.
    /// </summary>
    [Fact]
    public async Task A_setting_carries_enough_type_to_pick_an_input()
    {
        var rabbit = await DescribeAsync(BusAdapters.RabbitMq);

        Assert.Equal(DataSourceProviderSetting.NumberType,
            rabbit.Settings.Single(s => s.Name == "Prefetch").Type);
        Assert.Equal(DataSourceProviderSetting.BooleanType,
            rabbit.Settings.Single(s => s.Name == "UseSsl").Type);
        Assert.Equal(DataSourceProviderSetting.StringType,
            rabbit.Settings.Single(s => s.Name == "Host").Type);
    }

    /// <summary>
    /// Both installed providers turn up through the resource an operator's browser calls, which is
    /// what makes the menu self-populating rather than a list someone maintains.
    /// </summary>
    [Fact]
    public async Task The_resource_lists_every_installed_provider()
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var handler = ActivatorUtilities.CreateInstance<Resources.DataSources.Providers>(scope.ServiceProvider);

        var providers = (List<DataSourceProviderDescriptor>)await handler.Handle();
        var ids = providers.Select(p => p.AdapterId).ToList();

        Assert.Contains(BusAdapters.RabbitMq, ids);
        Assert.Contains(BusAdapters.Sqs, ids);
        Assert.All(providers, p => Assert.NotEmpty(p.Settings));
    }

    /// <summary>
    /// An adapter that is not a provider has nothing to describe, and asking about one that was
    /// never installed is not an error either - a broken or absent package must not empty the menu
    /// for the ones that are fine.
    /// </summary>
    [Fact]
    public async Task An_adapter_that_declares_nothing_is_simply_not_a_provider()
    {
        var catalog = fixture.App.Services.GetRequiredService<DataSourceProviderCatalog>();

        Assert.Null(await catalog.DescribeAsync("no.such.adapter.at.all"));
    }

    /// <summary>
    /// A provider nobody at Simplify9 published. Bitween's own adapters are found by their
    /// "bitween." id prefix, which cannot possibly find a third party's - so the installer stamps
    /// what an adapter IS onto the package (SW-Serverless #117, from its [AdapterKind]), and the
    /// catalog reads that instead of the name.
    ///
    /// The id here deliberately shares nothing with the convention: if this adapter is offered,
    /// only the stamp can explain it.
    /// </summary>
    [Fact]
    public async Task A_third_party_provider_is_found_by_its_stamped_kind_not_its_name()
    {
        const string adapterId = "acme.connectors.widgetbus";

        using var scope = fixture.App.Services.CreateScope();
        await AdapterInstaller.InstallAsync(
            scope.ServiceProvider.GetRequiredService<ICloudFilesService>(),
            "SW.Bitween.Adapters.Bus.RabbitMq", adapterId,
            "SW.Bitween.Adapters.Bus.RabbitMq.dll",
            new Dictionary<string, string>
            {
                ["Protocol"] = "2", ["Lifecycle"] = "resident", ["Kind"] = "bus"
            });

        var catalog = fixture.App.Services.GetRequiredService<DataSourceProviderCatalog>();
        var offered = await catalog.ListAsync();

        Assert.Contains(offered, p => p.AdapterId == adapterId);
        Assert.False(adapterId.StartsWith(DataSourceProviderCatalog.ConventionPrefix));
    }

    private async Task<DataSourceProviderDescriptor> DescribeAsync(string adapterId)
    {
        var catalog = fixture.App.Services.GetRequiredService<DataSourceProviderCatalog>();
        var descriptor = await catalog.DescribeAsync(adapterId);

        Assert.NotNull(descriptor);
        return descriptor;
    }
}
