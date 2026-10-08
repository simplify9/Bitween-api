using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Domain;
using SW.Bitween.Domain.DataSources;
using SW.Bitween.Domain.Gateway;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Services.Cluster;
using SW.Bitween.Services.DataSources;
using SW.Serverless.Resident;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// The API an operator configures an external bus gateway through.
///
/// Until this existed the whole external-bus feature was reachable only by writing rows into the
/// database by hand, which is how every other test in this suite still sets itself up. The
/// questions worth asking of it are about credentials — a broker password must not come back out
/// of an endpoint, and it must survive an edit that never touched it — and about the two ways a
/// gateway can be wrong: pointed at nothing, or pointed at a queue another gateway already reads.
/// </summary>
[Collection("Bitween")]
public class DataSourceApiTests(BitweenFixture fixture)
{
    // ---------------------------------------------------------------- secrets

    /// <summary>
    /// A password goes in and does not come back. This is the entire reason Get masks: the data
    /// source screen is the only place connection settings are ever served, so it is the only
    /// place a customer's broker credentials could leave the process.
    /// </summary>
    [Fact]
    public async Task A_secret_property_is_never_returned_in_clear()
    {
        var id = await CreateAsync(new Dictionary<string, string>
        {
            ["Host"] = "broker.example.com",
            ["UserName"] = "bitween",
            ["Password"] = "hunter2"
        });

        var row = await GetAsync(id);

        Assert.Equal("broker.example.com", row.Properties["Host"]);
        Assert.Equal("bitween", row.Properties["UserName"]);
        Assert.Equal(AdapterSecretProperties.Sentinel, row.Properties["Password"]);
        Assert.DoesNotContain("hunter2", string.Join("|", row.Properties.Values));
    }

    /// <summary>
    /// Nobody ticked a box; the property is still a password. Relying on an operator to declare
    /// every credential means the one they forget is the one in the JSON response.
    /// </summary>
    [Fact]
    public async Task A_credential_is_masked_even_when_nobody_declared_it()
    {
        var id = await CreateAsync(new Dictionary<string, string>
        {
            ["Region"] = "eu-west-1",
            ["SecretAccessKey"] = "abc/123",
            ["AccessKeyId"] = "AKIAEXAMPLE"
        }, secretProperties: []);      // declared nothing

        var row = await GetAsync(id);

        Assert.Equal("eu-west-1", row.Properties["Region"]);
        Assert.Equal(AdapterSecretProperties.Sentinel, row.Properties["SecretAccessKey"]);
        Assert.Equal(AdapterSecretProperties.Sentinel, row.Properties["AccessKeyId"]);

        // And it is recorded as secret, so the next reader does not have to rediscover it.
        Assert.Contains("SecretAccessKey", row.SecretProperties);
    }

    /// <summary>
    /// The round trip that breaks naive masking: read, change one unrelated field, save. If the
    /// sentinel were stored literally the broker would start authenticating with "__private__" and
    /// the only symptom would be an integration that stopped working.
    /// </summary>
    [Fact]
    public async Task Saving_a_masked_secret_back_keeps_the_stored_value()
    {
        var id = await CreateAsync(new Dictionary<string, string>
        {
            ["Host"] = "broker.example.com",
            ["Password"] = "hunter2",
            ["Prefetch"] = "16"
        });

        var row = await GetAsync(id);
        row.Properties["Prefetch"] = "32";   // the only real edit

        await UpdateAsync(id, row);

        Assert.Equal("hunter2", await StoredPropertyAsync(id, "Password"));
        Assert.Equal("32", await StoredPropertyAsync(id, "Prefetch"));
    }

    /// <summary>
    /// Except when the edit points the connection somewhere else. Kept across that, the password
    /// would go to whoever runs the new host, sent there by someone who never saw it.
    /// </summary>
    [Fact]
    public async Task Pointing_the_connection_elsewhere_drops_the_masked_secret()
    {
        var id = await CreateAsync(new Dictionary<string, string>
        {
            ["Host"] = "broker.example.com",
            ["Password"] = "hunter2"
        });

        var row = await GetAsync(id);
        row.Properties["Host"] = "broker.attacker.example";

        await UpdateAsync(id, row);

        Assert.Null(await StoredPropertyAsync(id, "Password"));
        Assert.Equal("broker.attacker.example", await StoredPropertyAsync(id, "Host"));
    }

    /// <summary>And a genuine change still goes through, or the field would be uneditable.</summary>
    [Fact]
    public async Task A_secret_that_was_actually_changed_is_saved()
    {
        var id = await CreateAsync(new Dictionary<string, string> { ["Password"] = "old" });

        var row = await GetAsync(id);
        row.Properties["Password"] = "new-one";
        await UpdateAsync(id, row);

        Assert.Equal("new-one", await StoredPropertyAsync(id, "Password"));
    }

    /// <summary>
    /// A data source pasted from a Get response — which is how anyone scripts a second environment
    /// — must not authenticate with the sentinel. There is nothing stored to restore from, so it
    /// is dropped and the field reads as unset rather than as a password nobody chose.
    /// </summary>
    [Fact]
    public async Task A_sentinel_with_nothing_behind_it_is_dropped_rather_than_stored()
    {
        var id = await CreateAsync(new Dictionary<string, string>
        {
            ["Host"] = "broker.example.com",
            ["Password"] = AdapterSecretProperties.Sentinel
        });

        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var stored = await db.Set<DataSource>().AsNoTracking().FirstAsync(d => d.Id == id);

        Assert.False(stored.Properties.ContainsKey("Password"),
            "the sentinel was stored as if it were the password");
    }

    /// <summary>
    /// The list is a table of connections, not of credentials. Even masked, sending every
    /// property to render a row is exposure with no purpose.
    /// </summary>
    [Fact]
    public async Task The_list_carries_no_connection_properties_at_all()
    {
        var id = await CreateAsync(new Dictionary<string, string> { ["Password"] = "hunter2" });

        var rows = await SearchAsync();
        var row = rows.Single(r => r.Id == id);

        Assert.True(row.Properties == null || row.Properties.Count == 0);
        Assert.Equal(BusAdapters.RabbitMq, row.AdapterId);
    }

    // ---------------------------------------------------------------- lifecycle

    [Fact]
    public async Task A_data_source_name_is_unique()
    {
        var name = Unique("ds");
        await CreateAsync(new Dictionary<string, string>(), name: name);

        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => CreateAsync(new Dictionary<string, string>(), name: name));

        Assert.Contains("already exists", error.Message);
    }

    /// <summary>
    /// The database refuses this too, but a raw foreign-key violation names a constraint rather
    /// than the gateway standing in the way, and an operator cannot act on a constraint name.
    /// </summary>
    [Fact]
    public async Task Deleting_a_data_source_that_feeds_a_gateway_says_which_gateway()
    {
        var id = await CreateAsync(new Dictionary<string, string>());
        var gatewayName = Unique("gw");
        await CreateGatewayAsync(id, Unique("q"), gatewayName);

        var error = await Assert.ThrowsAnyAsync<Exception>(() => DeleteAsync(id));

        Assert.Contains(gatewayName, error.Message);
    }

    [Fact]
    public async Task A_data_source_nothing_uses_can_be_deleted()
    {
        var id = await CreateAsync(new Dictionary<string, string>());
        await DeleteAsync(id);

        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        Assert.False(await db.Set<DataSource>().AnyAsync(d => d.Id == id));
    }

    // ---------------------------------------------------------------- gateways

    /// <summary>
    /// The default that keeps every gateway already in a database working: no data source means
    /// the internal bus, exactly as before.
    /// </summary>
    [Fact]
    public async Task A_gateway_created_without_a_data_source_is_internal()
    {
        var documentId = await CreateDocumentAsync();

        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var create = ActivatorUtilities.CreateInstance<Resources.BusGateways.Create>(scope.ServiceProvider);
        var gatewayId = (int)await create.Handle(new BusGatewayCreate
        {
            Name = Unique("gw"), DocumentId = documentId
        });

        var row = await GetGatewayAsync(gatewayId);

        Assert.Null(row.DataSourceId);
        Assert.Null(row.DataSourceName);
        Assert.Null(row.Endpoint);
    }

    [Fact]
    public async Task A_gateway_pointed_at_a_data_source_reports_it()
    {
        var dataSourceId = await CreateAsync(new Dictionary<string, string>());
        var queue = Unique("q");
        var gatewayId = await CreateGatewayAsync(dataSourceId, queue);

        var row = await GetGatewayAsync(gatewayId);

        Assert.Equal(dataSourceId, row.DataSourceId);
        Assert.Equal(queue, row.Endpoint);
        Assert.NotNull(row.DataSourceName);
    }

    /// <summary>
    /// An external gateway with no endpoint is a gateway that can never receive anything: the
    /// supervisor builds the adapter's consume list from endpoints, so it would sit there
    /// connected and idle, with nothing anywhere saying why.
    /// </summary>
    [Fact]
    public async Task An_external_gateway_without_an_endpoint_is_refused()
    {
        var dataSourceId = await CreateAsync(new Dictionary<string, string>());
        var documentId = await CreateDocumentAsync();

        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var create = ActivatorUtilities.CreateInstance<Resources.BusGateways.Create>(scope.ServiceProvider);

        var error = await Assert.ThrowsAnyAsync<Exception>(() => create.Handle(new BusGatewayCreate
        {
            Name = Unique("gw"), DocumentId = documentId, DataSourceId = dataSourceId
        }));

        Assert.Contains("endpoint", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Two gateways on one endpoint would both be candidates for every message and only one would
    /// ever run — a silent misroute rather than an error, which is the failure mode this whole
    /// area keeps producing when it is left unguarded.
    /// </summary>
    [Fact]
    public async Task Two_gateways_cannot_read_the_same_endpoint_on_one_data_source()
    {
        var dataSourceId = await CreateAsync(new Dictionary<string, string>());
        var queue = Unique("q");
        await CreateGatewayAsync(dataSourceId, queue);

        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => CreateGatewayAsync(dataSourceId, queue));

        Assert.Contains("already reads", error.Message);
    }

    /// <summary>Moving a gateway between the internal bus and a broker is the point of the field.</summary>
    [Fact]
    public async Task A_gateway_can_be_moved_from_internal_to_external_and_back()
    {
        var dataSourceId = await CreateAsync(new Dictionary<string, string>());
        var documentId = await CreateDocumentAsync();
        var queue = Unique("q");

        int gatewayId;
        await using (var scope = fixture.CreateScope())
        {
            scope.Superuser();
            var create = ActivatorUtilities.CreateInstance<Resources.BusGateways.Create>(scope.ServiceProvider);
            gatewayId = (int)await create.Handle(new BusGatewayCreate
            {
                Name = Unique("gw"), DocumentId = documentId
            });
        }

        await UpdateGatewayAsync(gatewayId, dataSourceId, queue);
        var external = await GetGatewayAsync(gatewayId);
        Assert.Equal(dataSourceId, external.DataSourceId);
        Assert.Equal(queue, external.Endpoint);

        await UpdateGatewayAsync(gatewayId, dataSourceId: null, endpoint: null);
        var internalAgain = await GetGatewayAsync(gatewayId);
        Assert.Null(internalAgain.DataSourceId);

        // The endpoint goes with it. Leaving one behind would show an internal gateway claiming to
        // read a queue, which is the sort of thing that survives for a year before anyone asks.
        Assert.Null(internalAgain.Endpoint);
    }

    // ---------------------------------------------------------------- memory ceilings

    /// <summary>
    /// The ceilings have to reach the adapter's process, not just the database.
    ///
    /// They are applied at launch — a GC heap hard limit on the child process — so the supervisor
    /// has to fold them into the fingerprint it compares each pass. Otherwise raising a limit
    /// saves cleanly, changes nothing, and the adapter keeps running under the old one with
    /// nothing to say it did not take.
    /// </summary>
    [Fact]
    public async Task Changing_a_memory_ceiling_restarts_the_adapter()
    {
        var dataSourceId = await CreateAsync(
            new Dictionary<string, string>(fixture.ExternalRabbitProperties));
        await CreateGatewayAsync(dataSourceId, Unique("mem"));

        var host = fixture.App.Services.GetRequiredService<IResidentAdapterHost>();

        // Disposed with the test: the election holds the exclusive queue that IS the lock, so
        // leaking one leaves this data source owned by a node that no longer exists — and every
        // later reconcile quietly declines to start it.
        using var election = Node();
        var supervisor = Supervisor(host, election);

        try
        {
            await supervisor.ReconcileAsync();

            var before = host.Describe()
                .FirstOrDefault(h => h.InstanceKey == dataSourceId.ToString())?.ProcessId;
            Assert.NotNull(before);

            var row = await GetAsync(dataSourceId);
            row.HardMemoryLimitMb = 512;
            await UpdateAsync(dataSourceId, row);

            await supervisor.ReconcileAsync();

            var after = host.Describe()
                .FirstOrDefault(h => h.InstanceKey == dataSourceId.ToString())?.ProcessId;
            Assert.NotNull(after);
            Assert.NotEqual(before, after);
        }
        finally
        {
            try { await supervisor.StopAsync(default); } catch { }
            try { await host.StopAsync(BusAdapters.RabbitMq, dataSourceId.ToString(), drain: false); }
            catch { }
            supervisor.Dispose();
        }
    }

    /// <summary>
    /// A soft ceiling above the hard one can never fire: the runtime fails the allocation before
    /// the supervisor ever sees the soft breach, so the graceful recycle it was configured for
    /// silently never happens.
    /// </summary>
    [Fact]
    public async Task A_soft_ceiling_above_the_hard_one_is_refused()
    {
        var dataSourceId = await CreateAsync(new Dictionary<string, string>());

        var row = await GetAsync(dataSourceId);
        row.SoftMemoryLimitMb = 900;
        row.HardMemoryLimitMb = 256;

        var error = await Assert.ThrowsAnyAsync<Exception>(() => UpdateAsync(dataSourceId, row));
        Assert.Contains("hard limit", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Zero_means_leave_the_host_default_alone()
    {
        var dataSourceId = await CreateAsync(new Dictionary<string, string>());
        var row = await GetAsync(dataSourceId);

        Assert.Equal(0, row.SoftMemoryLimitMb);
        Assert.Equal(0, row.HardMemoryLimitMb);
    }

    /// <summary>
    /// The CPU ceiling has to reach the adapter's process the same way the memory ones do, which
    /// means the supervisor has to fold it into the fingerprint. Otherwise it saves cleanly,
    /// changes nothing, and the adapter runs on under the old rule with nothing to say so.
    /// </summary>
    [Fact]
    public async Task Changing_the_cpu_ceiling_restarts_the_adapter()
    {
        var dataSourceId = await CreateAsync(
            new Dictionary<string, string>(fixture.ExternalRabbitProperties));
        await CreateGatewayAsync(dataSourceId, Unique("cpu"));

        var host = fixture.App.Services.GetRequiredService<IResidentAdapterHost>();

        // Disposed with the test: the election holds the exclusive queue that IS the lock, so
        // leaking one leaves this data source owned by a node that no longer exists — and every
        // later reconcile quietly declines to start it.
        using var election = Node();
        var supervisor = Supervisor(host, election);

        try
        {
            await supervisor.ReconcileAsync();

            var before = host.Describe()
                .FirstOrDefault(h => h.InstanceKey == dataSourceId.ToString())?.ProcessId;
            Assert.NotNull(before);

            var row = await GetAsync(dataSourceId);
            row.CpuPercentLimit = 40;
            row.CpuLimitSamples = 5;
            await UpdateAsync(dataSourceId, row);

            await supervisor.ReconcileAsync();

            var after = host.Describe()
                .FirstOrDefault(h => h.InstanceKey == dataSourceId.ToString())?.ProcessId;
            Assert.NotNull(after);
            Assert.NotEqual(before, after);
        }
        finally
        {
            try { await supervisor.StopAsync(default); } catch { }
            try { await host.StopAsync(BusAdapters.RabbitMq, dataSourceId.ToString(), drain: false); }
            catch { }
            supervisor.Dispose();
        }
    }

    /// <summary>
    /// The CPU figure is a share of the whole node, so anything above 100 can never be reached —
    /// the ceiling would sit there looking configured and never fire once.
    /// </summary>
    [Fact]
    public async Task A_cpu_ceiling_above_one_hundred_percent_is_refused()
    {
        var dataSourceId = await CreateAsync(new Dictionary<string, string>());

        var row = await GetAsync(dataSourceId);
        row.CpuPercentLimit = 250;

        var error = await Assert.ThrowsAnyAsync<Exception>(() => UpdateAsync(dataSourceId, row));
        Assert.Contains("never be reached", error.Message);
    }

    // ---------------------------------------------------------------- inspect

    /// <summary>
    /// Discover and GetStats are relayed; Publish is not.
    ///
    /// The adapter exposes Publish too, and it writes to the customer's broker. This endpoint is
    /// guarded by View, so a passthrough would let a read-level grant publish by naming it in a
    /// request body.
    /// </summary>
    [Fact]
    public async Task Only_read_only_commands_are_relayed()
    {
        var dataSourceId = await CreateAsync(new Dictionary<string, string>());

        var error = await Assert.ThrowsAnyAsync<Exception>(() => InspectAsync(dataSourceId, "Publish"));
        Assert.Contains("Publish", error.Message);

        // The allowed ones get through to the "is it running here" answer rather than being
        // rejected out of hand.
        var discover = await InspectAsync(dataSourceId, "Discover");
        Assert.False(discover.Ran);
        Assert.Contains("not running", discover.Error, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A data source no node is running answers plainly rather than erroring. A broker connection
    /// is exclusive, so "not here" is the normal answer on every node but one.
    /// </summary>
    [Fact]
    public async Task Inspecting_a_connection_this_node_does_not_hold_says_so()
    {
        var dataSourceId = await CreateAsync(new Dictionary<string, string>());

        var result = await InspectAsync(dataSourceId, "GetStats");

        Assert.False(result.Ran);
        Assert.NotNull(result.Error);
    }

    /// <summary>
    /// A data source is not only a broker: a resident adapter holding a database session is one
    /// too, and one of those has no queue for a gateway to consume. Refusing it here matters more
    /// than the menu that hides it — the menu is a courtesy, this is the rule.
    /// </summary>
    [Fact]
    public async Task A_bus_gateway_cannot_read_from_a_non_broker_data_source()
    {
        var dataSourceId = await CreateAsync(new Dictionary<string, string>(), kind: "Relational");
        var documentId = await CreateDocumentAsync();

        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var create = ActivatorUtilities.CreateInstance<Resources.BusGateways.Create>(scope.ServiceProvider);

        var error = await Assert.ThrowsAnyAsync<Exception>(() => create.Handle(new BusGatewayCreate
        {
            Name = Unique("gw"),
            DocumentId = documentId,
            DataSourceId = dataSourceId,
            Endpoint = "orders"
        }));

        Assert.Contains("Broker", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// And the same rule on the way in through an edit, which is the path that would otherwise
    /// move a working gateway onto a database connection.
    /// </summary>
    [Fact]
    public async Task A_bus_gateway_cannot_be_moved_onto_a_non_broker_data_source()
    {
        var broker = await CreateAsync(new Dictionary<string, string>());
        var database = await CreateAsync(new Dictionary<string, string>(), kind: "Relational");
        var gatewayId = await CreateGatewayAsync(broker, Unique("q"));

        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => UpdateGatewayAsync(gatewayId, database, "orders"));

        Assert.Contains("Broker", error.Message, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- helpers

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..20];

    private async Task<int> CreateAsync(Dictionary<string, string> properties,
        string name = null, List<string> secretProperties = null, string kind = "Broker")
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var handler = ActivatorUtilities.CreateInstance<Resources.DataSources.Create>(scope.ServiceProvider);

        return (int)await handler.Handle(new DataSourceCreate
        {
            Name = name ?? Unique("ds"),
            AdapterId = BusAdapters.RabbitMq,
            Kind = kind,
            Properties = properties,
            SecretProperties = secretProperties ?? ["Password"]
        });
    }

    private async Task<DataSourceRow> GetAsync(int id)
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var handler = ActivatorUtilities.CreateInstance<Resources.DataSources.Get>(scope.ServiceProvider);
        return (DataSourceRow)await handler.Handle(id);
    }

    private async Task UpdateAsync(int id, DataSourceRow row)
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var handler = ActivatorUtilities.CreateInstance<Resources.DataSources.Update>(scope.ServiceProvider);
        await handler.Handle(id, new DataSourceUpdate
        {
            Name = row.Name,
            AdapterId = row.AdapterId,
            Kind = row.Kind,
            Properties = row.Properties,
            SecretProperties = row.SecretProperties,
            Inactive = row.Inactive,
            DeduplicationWindowDays = row.DeduplicationWindowDays,
            SoftMemoryLimitMb = row.SoftMemoryLimitMb,
            HardMemoryLimitMb = row.HardMemoryLimitMb,
            CpuPercentLimit = row.CpuPercentLimit,
            CpuLimitSamples = row.CpuLimitSamples
        });
    }

    private async Task DeleteAsync(int id)
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var handler = ActivatorUtilities.CreateInstance<Resources.DataSources.Delete>(scope.ServiceProvider);
        await handler.Handle(id);
    }

    private async Task<List<DataSourceRow>> SearchAsync()
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var handler = ActivatorUtilities.CreateInstance<Resources.DataSources.Search>(scope.ServiceProvider);
        var response = (SearchyResponse<DataSourceRow>)await handler.Handle(new SearchyRequest { PageSize = 500 });
        return response.Result.ToList();
    }

    private async Task<string> StoredPropertyAsync(int id, string name)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var stored = await db.Set<DataSource>().AsNoTracking().FirstAsync(d => d.Id == id);
        return stored.Properties.TryGetValue(name, out var value) ? value : null;
    }

    private RabbitMqLeaderElection Node() => new(
        fixture.App.Services.GetRequiredService<IConfiguration>(),
        fixture.App.Services,
        fixture.App.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger<RabbitMqLeaderElection>());

    private BusProviderSupervisor Supervisor(IResidentAdapterHost host, ILeaderElection election) => new(
        fixture.App.Services, host, election,
        fixture.App.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger<BusProviderSupervisor>());

    private async Task<DataSourceInspectResult> InspectAsync(int id, string command)
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var handler = ActivatorUtilities.CreateInstance<Resources.DataSources.Inspect>(scope.ServiceProvider);
        return (DataSourceInspectResult)await handler.Handle(id, new DataSourceInspectRequest { Command = command });
    }

    private async Task<int> CreateDocumentAsync()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var document = new Document(null, Unique("doc"), DocumentFormat.Json);
        db.Add(document);
        await db.SaveChangesAsync();
        return document.Id;
    }

    private async Task<int> CreateGatewayAsync(int dataSourceId, string endpoint, string name = null)
    {
        var documentId = await CreateDocumentAsync();

        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var handler = ActivatorUtilities.CreateInstance<Resources.BusGateways.Create>(scope.ServiceProvider);

        return (int)await handler.Handle(new BusGatewayCreate
        {
            Name = name ?? Unique("gw"),
            DocumentId = documentId,
            DataSourceId = dataSourceId,
            Endpoint = endpoint
        });
    }

    private async Task UpdateGatewayAsync(int gatewayId, int? dataSourceId, string endpoint)
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var handler = ActivatorUtilities.CreateInstance<Resources.BusGateways.Update>(scope.ServiceProvider);

        var current = await GetGatewayAsync(gatewayId);
        await handler.Handle(gatewayId, new BusGatewayUpdate
        {
            Name = current.Name,
            DocumentId = current.DocumentId,
            DataSourceId = dataSourceId,
            Endpoint = endpoint
        });
    }

    private async Task<BusGatewayRow> GetGatewayAsync(int gatewayId)
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var handler = ActivatorUtilities.CreateInstance<Resources.BusGateways.Get>(scope.ServiceProvider);
        return (BusGatewayRow)await handler.Handle(gatewayId);
    }
}
