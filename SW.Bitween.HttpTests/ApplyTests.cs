using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using SW.Bitween.Cli;
using SW.Bitween.Cli.Configuration;
using Xunit;

namespace SW.Bitween.HttpTests;

/// <summary>
/// bitween plan, apply and export against a Bitween: a configuration is created, a second apply
/// changes nothing, an edit changes exactly what was edited, an export reads back as the same
/// configuration, prune removes only what apply made, and a service account's token can do it all
/// from the environment alone.
/// </summary>
[Collection("Http")]
public class ApplyTests(HttpFixture fixture) : IDisposable
{
    static readonly SemaphoreSlim OneAtATime = new(1, 1);
    readonly string work = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "bitween-apply-tests", Guid.NewGuid().ToString("N"))).FullName;
    readonly string s = Guid.NewGuid().ToString("N")[..8];

    public void Dispose()
    {
        try { Directory.Delete(work, true); } catch { }
    }

    sealed class FromAddress(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        readonly string address = $"10.{Random.Shared.Next(255)}.{Random.Shared.Next(255)}.{Random.Shared.Next(1, 255)}";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.Add("X-Forwarded-For", address);
            return base.SendAsync(request, cancellationToken);
        }
    }

    async Task<Profiles> SignedInAsync()
    {
        var profiles = new Profiles(Path.Combine(work, "cli.json"));
        var (exit, output) = await Bitween(profiles, HttpFixture.AdminPassword + "\n",
            "login", "https://localhost", "--email", HttpFixture.AdminEmail, "--password-stdin");
        Assert.True(exit == 0, output);
        return profiles;
    }

    async Task<(int Exit, string Output)> Bitween(Profiles profiles, string stdin, params string[] args)
    {
        await OneAtATime.WaitAsync();
        var original = Console.Out;
        using var output = new StringWriter();
        var synchronized = TextWriter.Synchronized(output);
        Console.SetOut(synchronized);
        BitweenApi.HandlerOverride = () => new FromAddress(fixture.App.Server.CreateHandler());
        try
        {
            var exit = await Program.RunAsync(args, profiles, new StringReader(stdin ?? ""), () => "");
            string text;
            lock (synchronized) text = output.ToString();
            return (exit, string.Join("\n", text.Split('\n').Where(l => !l.TrimStart().StartsWith("{\"@t\""))));
        }
        finally
        {
            BitweenApi.HandlerOverride = null;
            Console.SetOut(original);
            OneAtATime.Release();
        }
    }

    string Write(string yaml)
    {
        var file = Path.Combine(work, "config.yaml");
        File.WriteAllText(file, yaml);
        return file;
    }

    string Config(string handlerUrl = "https://warehouse.example.com/orders", string partnerKey = "${secret:LAB_PARTNER_KEY}",
        bool withOldPartner = true, string extraPartnerProperty = "") => $$"""
        apiVersion: bitween/v1
        kind: Configuration
        metadata:
          name: lab-{{s}}
        subscriptionCategories:
          - code: LAB_{{s.ToUpperInvariant()}}
            description: Lab flows
        workGroups:
          - name: Lab lane {{s}}
            busMessageName: lab-lane-{{s}}
        informationTypes:
          - name: Marketplace order {{s}}
            code: MKT_{{s.ToUpperInvariant()}}
            format: Json
            promotedProperties:
              orderId: $.id
        partners:
          - name: Acme marketplace {{s}}
            properties:
              region: eu{{extraPartnerProperty}}
            keys:
              main: {{partnerKey}}
        {{(withOldPartner ? $"  - name: Old partner {s}" : "")}}
        globalValues:
          - id: lab-{{s}}
            name: Lab values
            values:
              warehouse: https://warehouse.example.com
        retryPolicies:
          - name: Partner outage {{s}}
            groups:
              - name: server errors
                priority: 1
                appliesTo: [Error]
                matchers:
                  - type: contains
                    value: "503"
                budget:
                  maxAttemptsPerError: 3
                  maxAttemptsTotal: 5
                  delayStrategy:
                    type: fixed
                    delayMs: 1000
        subscriptions:
          - name: Orders to warehouse {{s}}
            type: GatewayApiCall
            informationType: Marketplace order {{s}}
            category: LAB_{{s.ToUpperInvariant()}}
            workGroup: Lab lane {{s}}
            retryPolicy: Partner outage {{s}}
            handler:
              adapter: NativeHttpHandler
              properties:
                Url: {{handlerUrl}}
                Verb: post
                AuthType: ApiKey
                ApiKey: ${secret:LAB_WAREHOUSE_KEY}
        apiGateways:
          - name: Orders inbound {{s}}
            urlName: lab-orders-{{s}}
            partners:
              - partner: Acme marketplace {{s}}
                subscription: Orders to warehouse {{s}}
        notifiers:
          - name: Ops on failure {{s}}
            handler: NativeHttpHandler
            properties:
              Url: https://ops.example.com/alerts
            runOnFailedResult: true
            subscriptions: [Orders to warehouse {{s}}]
        """;

    async Task<JsonObject> SubscriptionAsync(HttpClient admin, string name)
    {
        var rows = (await Api.Json(await admin.GetAsync("/api/subscriptions?page=0&size=1000")))["result"]!.AsArray();
        var id = (int)rows.Single(r => (string)r!["name"]! == name)!["id"]!;
        return (await Api.Json(await admin.GetAsync($"/api/subscriptions/{id}"))).AsObject();
    }

    [Fact]
    public async Task A_configuration_is_applied_once_and_a_second_apply_changes_nothing()
    {
        Environment.SetEnvironmentVariable("LAB_PARTNER_KEY", "partner-key-" + s + "-0123456789");
        Environment.SetEnvironmentVariable("LAB_WAREHOUSE_KEY", "warehouse-secret-" + s);
        try
        {
            var profiles = await SignedInAsync();
            var file = Write(Config());

            var (exit, output) = await Bitween(profiles, null, "plan", "-f", file, "--detailed-exitcode");
            Assert.True(exit == Commands.Changes, output);
            Assert.Contains($"+ subscription \"Orders to warehouse {s}\"", output);
            // A secret is never printed, not even in a plan.
            Assert.DoesNotContain("warehouse-secret-", output);

            (exit, output) = await Bitween(profiles, null, "apply", "-f", file);
            Assert.True(exit == 0, output);
            Assert.DoesNotContain("warehouse-secret-", output);

            (exit, output) = await Bitween(profiles, null, "plan", "-f", file, "--detailed-exitcode");
            Assert.True(exit == 0, output);
            Assert.Contains("No changes", output);

            // What it made is what the file says, wired together.
            using var admin = await fixture.AdminAsync();
            var subscription = await SubscriptionAsync(admin, $"Orders to warehouse {s}");
            Assert.False((bool)subscription["inactive"]!);
            Assert.Equal("NativeHttpHandler", (string)subscription["handlerId"]!);
            var gateways = (await Api.Json(await admin.GetAsync("/api/apigateways?page=0&size=1000")))["result"]!.AsArray();
            var gatewayId = (int)gateways.Single(g => (string)g!["urlName"]! == $"lab-orders-{s}")!["id"]!;
            var gateway = await Api.Json(await admin.GetAsync($"/api/apigateways/{gatewayId}"));
            Assert.Equal($"Orders to warehouse {s}", (string)gateway["partners"]![0]!["subscriptionName"]!);
            // The partner's key works at the gateway it was attached to.
            var call = await Api.CallSyncAsync(fixture, $"lab-orders-{s}", "partner-key-" + s + "-0123456789");
            Assert.NotEqual(System.Net.HttpStatusCode.Unauthorized, call.StatusCode);

            // An edit changes exactly what was edited.
            file = Write(Config(handlerUrl: "https://warehouse.example.com/v2/orders", extraPartnerProperty: "-west"));
            (exit, output) = await Bitween(profiles, null, "plan", "-f", file);
            Assert.True(exit == 0, output);
            Assert.Contains($"~ subscription \"Orders to warehouse {s}\"", output);
            Assert.Contains("handler:", output);
            Assert.Contains($"~ partner \"Acme marketplace {s}\"", output);
            Assert.Contains("0 to create, 2 to update", output);
            (exit, output) = await Bitween(profiles, null, "apply", "-f", file);
            Assert.True(exit == 0, output);
            subscription = await SubscriptionAsync(admin, $"Orders to warehouse {s}");
            Assert.Contains("v2/orders", subscription["handlerProperties"]!.ToJsonString());
            // The secret it didn't change is still the one it had.
            (exit, output) = await Bitween(profiles, null, "plan", "-f", file, "--detailed-exitcode");
            Assert.True(exit == 0, output);

            // A key that no longer starts as the stored one did is replaced.
            Environment.SetEnvironmentVariable("LAB_PARTNER_KEY", "rotated-key-" + s + "-0123456789");
            (exit, output) = await Bitween(profiles, null, "apply", "-f", file);
            Assert.True(exit == 0, output);
            Assert.Contains($"~ partner \"Acme marketplace {s}\"", output);
            call = await Api.CallSyncAsync(fixture, $"lab-orders-{s}", "partner-key-" + s + "-0123456789");
            Assert.Equal(System.Net.HttpStatusCode.Unauthorized, call.StatusCode);
            call = await Api.CallSyncAsync(fixture, $"lab-orders-{s}", "rotated-key-" + s + "-0123456789");
            Assert.NotEqual(System.Net.HttpStatusCode.Unauthorized, call.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LAB_PARTNER_KEY", null);
            Environment.SetEnvironmentVariable("LAB_WAREHOUSE_KEY", null);
        }
    }

    [Fact]
    public async Task Prune_removes_only_what_apply_made_and_the_file_dropped()
    {
        Environment.SetEnvironmentVariable("LAB_PARTNER_KEY", "partner-key-" + s + "-0123456789");
        Environment.SetEnvironmentVariable("LAB_WAREHOUSE_KEY", "warehouse-secret-" + s);
        try
        {
            var profiles = await SignedInAsync();
            Assert.Equal(0, (await Bitween(profiles, null, "apply", "-f", Write(Config()))).Exit);

            // Made by hand, alongside: apply never touches it.
            using var admin = await fixture.AdminAsync();
            await Api.CreateAsync(admin, "/api/partners", new { name = $"Hand made {s}" });

            var file = Write(Config(withOldPartner: false));
            var (exit, output) = await Bitween(profiles, null, "apply", "-f", file);
            Assert.True(exit == 0, output);
            Assert.Contains("--prune removes them", output);

            (exit, output) = await Bitween(profiles, null, "apply", "-f", file, "--prune");
            Assert.True(exit == 0, output);
            Assert.Contains($"- partner \"Old partner {s}\"", output);
            Assert.DoesNotContain("Hand made", output);
            var partners = (await Api.Json(await admin.GetAsync("/api/partners?page=0&size=5000")))["result"]!.ToJsonString();
            Assert.DoesNotContain($"Old partner {s}", partners);
            Assert.Contains($"Hand made {s}", partners);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LAB_PARTNER_KEY", null);
            Environment.SetEnvironmentVariable("LAB_WAREHOUSE_KEY", null);
        }
    }

    [Fact]
    public async Task An_export_reads_back_as_the_same_configuration()
    {
        Environment.SetEnvironmentVariable("LAB_PARTNER_KEY", "partner-key-" + s + "-0123456789");
        Environment.SetEnvironmentVariable("LAB_WAREHOUSE_KEY", "warehouse-secret-" + s);
        try
        {
            var profiles = await SignedInAsync();
            Assert.Equal(0, (await Bitween(profiles, null, "apply", "-f", Write(Config()))).Exit);

            var exported = Path.Combine(work, "exported.yaml");
            var (exit, output) = await Bitween(profiles, null, "export", "-o", exported, "--name", $"lab-{s}");
            Assert.True(exit == 0, output);
            var all = Yaml.Read(File.ReadAllText(exported)).Single()!.AsObject();

            // Only this test's items, from a Bitween other tests share: and as the export wrote them.
            var mine = new JsonObject { ["apiVersion"] = "bitween/v1", ["kind"] = "Configuration", ["metadata"] = new JsonObject { ["name"] = $"export-{s}" } };
            foreach (var (section, items) in all.Where(p => p.Value is JsonArray))
                mine[section] = new JsonArray(items!.AsArray().Where(i => i!.ToJsonString().Contains(s)).Select(i => i!.DeepClone()).ToArray());
            var subscription = mine["subscriptions"]!.AsArray().Single()!;
            // The secrets it can't show are named, to be supplied.
            var apiKey = (string)subscription["handler"]!["properties"]!["ApiKey"]!;
            Assert.StartsWith("${secret:", apiKey);
            var partnerKey = (string)mine["partners"]!.AsArray().First(p => ((string)p!["name"]!).StartsWith("Acme"))!["keys"]!["main"]!;
            Assert.StartsWith("${secret:", partnerKey);
            Environment.SetEnvironmentVariable(apiKey[9..^1], "warehouse-secret-" + s);
            Environment.SetEnvironmentVariable(partnerKey[9..^1], "partner-key-" + s + "-0123456789");

            (exit, output) = await Bitween(profiles, null, "plan", "-f", Write(Yaml.Write(mine)), "--detailed-exitcode");
            Assert.True(exit == 0, output);
            Assert.Contains("No changes", output);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LAB_PARTNER_KEY", null);
            Environment.SetEnvironmentVariable("LAB_WAREHOUSE_KEY", null);
        }
    }

    [Fact]
    public async Task Adapters_data_sources_bus_routes_responses_and_pausing_apply_and_settle()
    {
        var profiles = await SignedInAsync();
        var name = "LabPy" + s;
        var id = SW.Serverless.Tooling.Scaffolding.Scaffolder.IdFrom(name);
        Assert.Equal(0, (await Bitween(profiles, null, "adapter", "init", name, "--kind", "handler", "--lang", "python", "--dir", work)).Exit);
        var build = await Bitween(profiles, null, "adapter", "build", Path.Combine(work, name));
        Assert.True(build.Exit == 0, build.Output);
        File.Copy(Path.Combine(work, name, "bin", "serverless", $"{id}-0.1.0.zip"), Path.Combine(work, "handler.zip"));
        Environment.SetEnvironmentVariable("LAB_DB_PASSWORD", "db-password-" + s);

        string Config(bool paused) => $"""
            apiVersion: bitween/v1
            kind: Configuration
            metadata:
              name: wiring-{s}
            adapters:
              - package: handler.zip
                current: true
            informationTypes:
              - name: Stock update {s}
                format: Json
                busEnabled: true
                busMessageTypeName: LabStock{s}
            partners:
              - name: Shop {s}
            dataSources:
              - name: ERP {s}
                adapter: sw.bitween.adapters.db.postgresql
                kind: Relational
                inactive: true
                properties:
                  Host: erp.example.com
                  Password: ${"{"}secret:LAB_DB_PASSWORD{"}"}
            dataSourceStatements:
              - dataSource: ERP {s}
                name: new orders
                sql: select * from orders where id > :cursor
                cursorColumn: id
            subscriptions:
              - name: Stock to shop {s}
                type: ApiCall
                informationType: Stock update {s}
                partner: Shop {s}
                responseSubscription: Shop answers {s}
                paused: {(paused ? "true" : "false")}
                handler:
                  adapter: {id}
                  version: 0.1.0
                  properties:
                    ApiKey: k
              - name: Shop answers {s}
                type: Response
                informationType: Stock update {s}
              - name: Stock from bus {s}
                type: BusGateway
                informationType: Stock update {s}
            busGateways:
              - name: Stock bus {s}
                informationType: Stock update {s}
                routes:
                  - subscription: Stock from bus {s}
                    partner: Shop {s}
                    matchExpression:
                      type: one_of
                      path: $.warehouse
                      values: [north]
            """;
        try
        {
            var file = Write(Config(paused: true));
            var (exit, output) = await Bitween(profiles, null, "apply", "-f", file);
            Assert.True(exit == 0, output);
            Assert.Contains($"+ publish adapter {id} 0.1.0, current", output);
            // The response subscription is made before the one that answers to it, though listed after.
            Assert.True(output.IndexOf($"Shop answers {s}", StringComparison.Ordinal) < output.IndexOf($"Stock to shop {s}", StringComparison.Ordinal), output);

            (exit, output) = await Bitween(profiles, null, "plan", "-f", file, "--detailed-exitcode");
            Assert.True(exit == 0, output);
            // The same package again: Bitween re-zips what it takes, so its hash isn't this file's, and that isn't a difference.
            Assert.DoesNotContain("different package", output);

            using var admin = await fixture.AdminAsync();
            var rows = (await Api.Json(await admin.GetAsync("/api/subscriptions?page=0&size=2000")))["result"]!.AsArray();
            var stock = rows.Single(r => (string)r!["name"]! == $"Stock to shop {s}")!;
            Assert.NotNull((string?)stock["pausedOn"]);
            Assert.Equal("0.1.0", (string)stock["handlerVersion"]!);

            (exit, output) = await Bitween(profiles, null, "apply", "-f", Write(Config(paused: false)));
            Assert.True(exit == 0, output);
            Assert.Contains("paused: true -> false", output);
            rows = (await Api.Json(await admin.GetAsync("/api/subscriptions?page=0&size=2000")))["result"]!.AsArray();
            Assert.Null((string?)rows.Single(r => (string)r!["name"]! == $"Stock to shop {s}")!["pausedOn"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LAB_DB_PASSWORD", null);
        }
    }

    [Fact]
    public async Task A_service_token_in_the_environment_is_enough_to_apply()
    {
        using var admin = await fixture.AdminAsync();
        var accountId = await Api.CreateAsync(admin, "/api/serviceaccounts", new { name = $"lab apply {s}", roleIds = new[] { 1 } });
        var token = (string)(await Api.Json(await admin.PostAsJsonAsync($"/api/serviceaccounts/{accountId}/issueToken", new { name = "job" })))["token"]!;

        var file = Write($"""
            apiVersion: bitween/v1
            kind: Configuration
            metadata:
              name: token-{s}
            informationTypes:
              - name: Token made {s}
                format: Json
            """);
        // No profile anywhere: what a job in a container has.
        var noProfiles = new Profiles(Path.Combine(work, "none.json"));
        Environment.SetEnvironmentVariable("BITWEEN_URL", "https://localhost");
        Environment.SetEnvironmentVariable("BITWEEN_TOKEN", token);
        try
        {
            var (exit, output) = await Bitween(noProfiles, null, "apply", "-f", file);
            Assert.True(exit == 0, output);
            Assert.Contains($"+ information type \"Token made {s}\"", output);
            (exit, output) = await Bitween(noProfiles, null, "whoami");
            Assert.Contains("service account", output);

            Environment.SetEnvironmentVariable("BITWEEN_TOKEN", "bwt_" + new string('n', 40));
            (exit, output) = await Bitween(noProfiles, null, "plan", "-f", file);
            Assert.Equal(Commands.Failure, exit);
            Assert.Contains("refused the service token", output);
        }
        finally
        {
            Environment.SetEnvironmentVariable("BITWEEN_URL", null);
            Environment.SetEnvironmentVariable("BITWEEN_TOKEN", null);
        }
        Assert.False(File.Exists(Path.Combine(work, "none.json")));
    }

    [Fact]
    public async Task A_file_that_names_what_isnt_there_or_changes_what_cant_change_is_refused_before_anything_changes()
    {
        var profiles = await SignedInAsync();
        var file = Write($"""
            apiVersion: bitween/v1
            kind: Configuration
            metadata:
              name: broken-{s}
            informationTypes:
              - name: Refused {s}
                format: Json
            subscriptions:
              - name: Nowhere {s}
                type: ApiCall
                informationType: Refused {s}
                partner: Nobody {s}
            """);
        var (exit, output) = await Bitween(profiles, null, "apply", "-f", file);
        Assert.Equal(Commands.Failure, exit);
        Assert.Contains($"there's no partner \"Nobody {s}\"", output);
        using var admin = await fixture.AdminAsync();
        Assert.DoesNotContain($"Refused {s}", (await Api.Json(await admin.GetAsync("/api/documents?page=0&size=5000"))).ToJsonString());

        Assert.Equal(1, (await Bitween(profiles, null, "plan", "-f", Write("apiVersion: bitween/v1\nkind: Configuration\nmetadata: { name: x }\nwidgets: []\n"))).Exit);
    }
}
